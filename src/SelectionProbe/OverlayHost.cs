using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;

namespace ContextNotes.SelectionProbe;

internal sealed record OverlaySelection(
    WindowIdentity Window,
    ScreenRectangle WindowBounds,
    ScreenRectangle Viewport,
    ScreenRectangle[] Rectangles,
    string SelectedText = "",
    SelectionTrace? Trace = null);

internal sealed class OverlayHost : IDisposable
{
    private readonly Thread thread;
    private readonly Dispatcher dispatcher;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly object updates = new();
    private readonly List<AnnotationWindow> annotations = [];
    private readonly AnnotationService annotationService;
    private readonly bool ownsService;
    private OverlaySelection? latest;
    private SelectionSnapshot? boundSnapshot;
    private string hideReason = "not_foreground";
    private SelectionTrace latestTrace = SelectionTrace.Empty("not_foreground");
    private long generation;
    private bool queued;
    private bool disposed;
    private nint handle;
    private nint[] annotationHandles = [];
    private QuestionOverlay? window;
    private WindowIdentity owner;
    private ButtonPlacement? lastPlacement;
    private string? lastHiddenReason;
    private long lastHiddenSelectionId;
    private long lastDeferredSelectionId = -1;
    private string? activeTrackingAnnotationId;
    private int nextAnnotationNumber;
    private readonly HistoryPersistence? history;
    private HistoryHotkey? historyHotkey;
    private HistoryWindow? historyWindow;

    public OverlayHost(AnnotationService? service = null, HistoryPersistence? history = null)
    {
        this.history = history;
        ownsService = service is null;
        annotationService = service ?? new AnnotationService(diagnosticLogger: Write);
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        thread = new Thread(() =>
        {
            Dispatcher.CurrentDispatcher.UnhandledException += (_, args) =>
            {
                // A rendering fault must hide the entry, never take down the companion.
                args.Handled = true;
                Write($"overlay fault error_type={args.Exception.GetType().Name}");
                SelectionTrace failed;
                lock (updates)
                    failed = latestTrace with { Reason = "dispatcher_fault" };
                failed.Write("overlay_creation_failed", false, args.Exception.GetType());
                try
                {
                    window?.Hide();
                }
                catch (InvalidOperationException)
                {
                    window = null;
                }
                lastPlacement = null;
            };
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        }) { IsBackground = true, Name = "Context Notes Overlay" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        dispatcher = ready.Task.GetAwaiter().GetResult();
        if (history is not null) dispatcher.Invoke(() => historyHotkey = new HistoryHotkey(ShowHistory));
    }

    public bool PointerOverButton => NativeWindows.PointerOverWindow(Volatile.Read(ref handle));

    public bool PointerOverAnnotation
    {
        get
        {
            nint[] handles = Volatile.Read(ref annotationHandles);
            for (int i = 0; i < handles.Length; i++)
            {
                if (NativeWindows.PointerOverWindow(handles[i]))
                    return true;
            }
            return false;
        }
    }

    public bool IsAnnotationWindow(nint hwnd) =>
        hwnd != 0 && Volatile.Read(ref annotationHandles).Contains(hwnd);

    public int AnnotationCount => dispatcher.Invoke(() => annotations.Count(a => !a.Item.IsClosed));

    internal IReadOnlyList<AnnotationWindow> ActiveAnnotationsForTest => annotations.Where(a => !a.Item.IsClosed).ToArray();

    internal void InvokeForTest(Action action) => dispatcher.Invoke(action);

    internal void UpdateAnnotationsForSampleForTest(WindowIdentity codexWindow, ScreenRectangle windowBounds, SelectionSample sample) =>
        UpdateAnnotationsForSample(codexWindow, windowBounds, sample);

    internal void SuspendAnnotationsForTest() => SuspendAnnotationsByForeground();

    internal void UpdateAnnotationsForOwnerForTest(WindowIdentity codexWindow, ScreenRectangle windowBounds) =>
        UpdateAnnotationsForOwner(codexWindow, windowBounds);

    public void Publish(OverlaySelection? selection, string reason = "selection_empty", SelectionTrace? trace = null)
    {
        lock (updates)
        {
            if (disposed)
                return;
            latest = selection;
            hideReason = reason;
            latestTrace = (trace ?? selection?.Trace ?? SelectionTrace.Empty(reason)) with { Reason = reason };
            generation++;
            if (!queued)
            {
                queued = true;
                dispatcher.BeginInvoke(DispatcherPriority.Input, RenderLatest);
            }
        }
    }

    public void NotifyOwnerGeometry(WindowIdentity codexWindow, ScreenRectangle windowBounds)
    {
        lock (updates)
        {
            if (disposed)
                return;
        }
        dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (disposed || codexWindow.IsEmpty)
                return;
            owner = codexWindow;
            UpdateAnnotationsForOwner(codexWindow, windowBounds);
        }));
    }

    public void NotifySelectionSample(WindowIdentity codexWindow, ScreenRectangle windowBounds, SelectionSample sample)
    {
        lock (updates)
        {
            if (disposed)
                return;
        }
        dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (disposed || codexWindow.IsEmpty || annotations.Count == 0)
                return;
            UpdateAnnotationsForSample(codexWindow, windowBounds, sample);
        }));
    }

    private void RenderLatest()
    {
        OverlaySelection? selection;
        string reason;
        SelectionTrace trace;
        long expected;
        lock (updates)
        {
            queued = false;
            selection = latest;
            reason = hideReason;
            trace = latestTrace;
            expected = generation;
        }

        if (selection is null)
        {
            // Do not hide the entry button mid-click while the user is pressing it.
            if (reason is not ("not_foreground" or "no_foreground_window" or "annotation_opened")
                && window is { IsVisible: true }
                && boundSnapshot is not null
                && PointerOverButton
                && NativeWindows.LeftButtonDown())
            {
                if (lastDeferredSelectionId != trace.Id)
                {
                    (trace with { Reason = "button_press_in_progress" }).Write("overlay_hide_deferred", true);
                    lastDeferredSelectionId = trace.Id;
                }
                return;
            }
            Hide(reason, trace);
            if (reason is "not_foreground" or "no_foreground_window")
                SuspendAnnotationsByForeground();
            return;
        }
        if (PublishBlocker(selection, expected) is { } blocker)
        {
            Hide(blocker, trace);
            return;
        }

        ScreenRectangle viewport = selection.Viewport.Intersect(selection.WindowBounds);
        DisplayArea? display = OverlayPlacement.ChooseDisplay(selection.Rectangles, viewport,
            NativeWindows.Displays());
        if (display is not { } screen)
        {
            Hide("selection_outside_viewport", trace);
            return;
        }

        try
        {
            if (window is null)
            {
                trace.Write("overlay_create_started", false);
                var created = new QuestionOverlay { Clicked = OnClick };
                if (history is not null)
                {
                    created.ContextMenu = new System.Windows.Controls.ContextMenu();
                    var entry = new System.Windows.Controls.MenuItem { Header = "旁注历史（Ctrl+Alt+H）" };
                    entry.Click += (_, _) => ShowHistory();
                    created.ContextMenu.Items.Add(entry);
                }
                nint createdHandle = created.Handle;
                window = created;
                Volatile.Write(ref handle, createdHandle);
                trace.Write("overlay_created", false);
            }
            if (NativeWindows.WindowDisplay(window.Handle) != screen.Handle)
            {
                Hide("display_change", trace);
                if (!window.StageOn(screen))
                {
                    Hide("display_stage_failed", trace);
                    return;
                }
            }

            var dpi = VisualTreeHelper.GetDpi(window);
            ScreenRectangle work = viewport.Intersect(screen.WorkArea);
            ScreenRectangle? anchor = OverlayPlacement.GetAnchorRectangle(selection.Rectangles, work,
                dpi.DpiScaleX, dpi.DpiScaleY);
            ButtonPlacement? placement = anchor is { } box
                ? OverlayPlacement.Place(box, work, (int)Math.Ceiling(window.Width * dpi.DpiScaleX),
                    (int)Math.Ceiling(window.Height * dpi.DpiScaleY), 7 * dpi.DpiScaleX, 7 * dpi.DpiScaleY)
                : null;
            if (placement is not { } target || anchor is not { } validAnchor)
            {
                Hide(anchor is null ? "invalid_rectangle" : "overlay_position_invalid", trace);
                return;
            }
            if (PublishBlocker(selection, expected) is { } lateBlocker)
            {
                Hide(lateBlocker, trace);
                return;
            }

            bool showing = !window.IsVisible;
            bool placementChanged = target != lastPlacement;
            if ((showing || placementChanged) && !window.Place(target.Bounds))
            {
                Hide("overlay_position_failed", trace);
                return;
            }
            owner = selection.Window;
            if (!string.IsNullOrWhiteSpace(selection.SelectedText))
            {
                bool newSnapshot = boundSnapshot?.Trace?.Id != trace.Id || showing || placementChanged;
                boundSnapshot = SelectionSnapshot.FromSelection(selection, validAnchor);
                if (newSnapshot)
                    trace.Write("selection_snapshot_bound", !showing);
            }
            if (showing)
            {
                window.Show();
                if (NativeWindows.WindowBounds(window.Handle) != target.Bounds && !window.Place(target.Bounds))
                {
                    Hide("overlay_position_failed", trace);
                    return;
                }
            }
            if (!NativeWindows.IsWindowVisible(window.Handle))
            {
                Hide("native_window_not_visible", trace);
                return;
            }
            if (showing || placementChanged)
            {
                var bounds = NativeWindows.WindowBounds(window.Handle);
                if (!bounds.IsValid || bounds.Intersect(work) != bounds || bounds.Intersects(validAnchor))
                {
                    Hide("native_window_bounds_invalid", trace);
                    return;
                }
                trace.Write(showing ? "overlay_shown" : "overlay_moved", true);
                Write(FormattableString.Invariant(
                    $"overlay_visible=true change={(showing ? "shown" : "moved")} side={target.Side} px=[{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}] viewport=[{viewport.X},{viewport.Y},{viewport.Width},{viewport.Height}] dpi={dpi.PixelsPerInchX:F0}"));
            }
            lastPlacement = target;
            lastHiddenReason = null;
            lastDeferredSelectionId = -1;
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            Hide("overlay_creation_or_render_fault", trace);
            (trace with { Reason = "overlay_creation_or_render_fault" }).Write("overlay_creation_failed", false, error.GetType());
            Write($"overlay error={error.GetType().Name}");
        }
    }

    private string? PublishBlocker(OverlaySelection selection, long expected)
    {
        lock (updates)
        {
            if (disposed)
                return "overlay_disposed";
            if (expected != generation)
                return "selection_generation_changed";
        }
        WindowIdentity currentForeground = NativeWindows.Foreground();
        if (currentForeground != selection.Window && !IsAnnotationWindow(currentForeground.Handle))
            return "foreground_changed";
        if (NativeWindows.WindowBounds(selection.Window.Handle) != selection.WindowBounds)
            return "window_moved";
        return NativeWindows.LeftButtonDown() && !PointerOverButton && !PointerOverAnnotation ? "pointer_selection" : null;
    }

    private void OnClick()
    {
        WindowIdentity currentForeground = NativeWindows.Foreground();
        bool codexForeground = currentForeground == owner
            || NativeWindows.IsCodex(currentForeground)
            || IsAnnotationWindow(currentForeground.Handle);
        if (!codexForeground || window is not { IsActive: false })
        {
            ((boundSnapshot?.Trace ?? SelectionTrace.Empty("selection_empty")) with
            {
                Reason = codexForeground ? "entry_activated" : "foreground_changed"
            }).Write("overlay_click_ignored");
            return;
        }

        SelectionSnapshot? snapshot = boundSnapshot;
        boundSnapshot = null;
        SelectionTrace trace = snapshot?.Trace ?? SelectionTrace.Empty("snapshot_missing");
        Write($"annotation_entry_clicked codex_foreground=true has_snapshot={snapshot is not null} selected_text_length={snapshot?.SelectedText.Length ?? 0}");
        trace.Write("overlay_clicked", true);
        Hide("annotation_opened", trace);
        if (snapshot is not null)
            _ = OpenAnnotationFromSnapshot(snapshot);
    }

    public AnnotationWindow? OpenAnnotationFromSnapshot(SelectionSnapshot snapshot)
    {
        // Reuse existing annotation if the exact same selection is already open (prevents duplicate windows).
        AnnotationWindow? existing = annotations.FirstOrDefault(
            a => !a.Item.IsClosed && a.Item.OwnerWindow == snapshot.Window
                && a.Item.MatchesSelection(snapshot.SelectedText, snapshot.AnchorRectangle));
        if (existing is not null)
        {
            activeTrackingAnnotationId = existing.Item.AnnotationId;
            existing.Item.HiddenByForeground = false;
            existing.Item.HiddenByViewport = false;
            if (!existing.Item.ExpandedState)
                existing.SetExpanded(true);
            if (!existing.IsVisible)
                existing.Show();
            _ = existing.FocusInput();
            Write($"annotation_reused annotation_id={existing.Item.AnnotationId}");
            return existing;
        }

        string id = history is null ? $"ann-{++nextAnnotationNumber}" : $"ann-{Guid.NewGuid():N}";
        var item = new AnnotationItem(id, snapshot);
        if (history is not null) item.Persist = history.Save;
        var popup = new AnnotationWindow(item, annotationService)
        {
            DiagnosticLogged = (_, message) => Write(message),
            ClosedByUser = OnAnnotationClosed,
            LayoutChanged = OnAnnotationLayoutChanged
        };
        popup.HistoryRequested = ShowHistory;
        if (history is not null) _ = BindAccount(item);

        ScreenRectangle viewport = snapshot.Viewport.IsValid
            ? snapshot.Viewport.Intersect(snapshot.WindowBounds)
            : snapshot.WindowBounds;
        DisplayArea[] displays = NativeWindows.Displays();
        DisplayArea? display = OverlayPlacement.ChooseDisplay(snapshot.Rectangles, viewport, displays)
            ?? (displays.Length > 0 ? displays[0] : null);
        if (display is { } screen)
            _ = popup.StageOn(screen);

        var dpi = VisualTreeHelper.GetDpi(popup);
        var (widthPx, heightPx) = popup.MeasurePhysicalSize(dpi.DpiScaleX, dpi.DpiScaleY);
        ScreenRectangle workArea = display is { } disp ? disp.WorkArea : snapshot.WindowBounds;
        ScreenRectangle viewportWork = viewport.Intersect(workArea);
        ScreenRectangle windowWork = snapshot.WindowBounds.Intersect(workArea);
        ScreenRectangle[] occupied = [.. annotations.Where(a => !a.Item.IsClosed && a.IsVisible).Select(a => a.Item.WindowPosition)];

        ButtonPlacement placement = OverlayPlacement.PlaceAnnotation(
            snapshot.AnchorRectangle, viewportWork, windowWork, widthPx, heightPx,
            8 * dpi.DpiScaleX, 8 * dpi.DpiScaleY, occupied)
            ?? new ButtonPlacement(OverlayPlacement.ClampToArea(
                new ScreenRectangle(snapshot.AnchorRectangle.X, snapshot.AnchorRectangle.Bottom + 8, widthPx, heightPx),
                workArea), "below");

        item.PlacementSide = placement.Side;
        item.WindowPosition = placement.Bounds;
        annotations.Add(popup);
        activeTrackingAnnotationId = item.AnnotationId;
        RefreshAnnotationHandles();

        _ = popup.Place(placement.Bounds, activate: false);
        popup.Show();
        _ = popup.Place(placement.Bounds, activate: true);
        bool focusRequested = popup.FocusInput();
        item.Persist?.Invoke(item, true);

        ScreenRectangle actualBounds = NativeWindows.WindowBounds(popup.Handle);
        Write(FormattableString.Invariant(
            $"annotation_created annotation_id={item.AnnotationId} side={placement.Side} selected_text_length={item.SelectedText.Length} px=[{actualBounds.X:0},{actualBounds.Y:0},{actualBounds.Width:0},{actualBounds.Height:0}] focus_requested={focusRequested} active_count={annotations.Count}"));
        if (snapshot.Trace is { } trace)
        {
            bool quoteMatches = string.Equals(popup.QuoteControl.Text, snapshot.SelectedText, StringComparison.Ordinal);
            Console.WriteLine(trace.Format("annotation_opened", false) +
                $" annotation_visible={NativeWindows.IsWindowVisible(popup.Handle)} quote_exact={quoteMatches} focus_requested={focusRequested}");
        }
        return popup;
    }

    private async Task BindAccount(AnnotationItem item)
    {
        try
        {
            var account = await annotationService.ReadCurrentAccountAsync();
            if (disposed || item.IsClosed) return;
            if (account.Status == AppServerLoginStatus.LoggedIn && item.AccountScopeId is null)
            {
                item.AccountScopeId = account.ScopeFingerprint; item.AccountSummary = account.DisplaySummary;
                item.Persist?.Invoke(item, true);
            }
        }
        catch (Exception) { Write("event=annotation_account_binding_deferred"); }
    }

    private void ShowHistory()
    {
        if (history is null || disposed) return;
        if (historyWindow is null)
        {
            historyWindow = new HistoryWindow(history, annotationService, RestoreHistory, CloseHistoryAnnotation);
            historyWindow.Closed += (_, _) => historyWindow = null;
        }
        historyWindow.Show(); historyWindow.Activate();
    }

    private void RestoreHistory(HistoryRecord record)
    {
        if (history is null || disposed) return;
        var existing = annotations.FirstOrDefault(a => a.Item.AnnotationId == record.Id && !a.Item.IsClosed);
        if (existing is not null) { existing.Show(); existing.FocusInput(); return; }
        if (owner.IsEmpty || !NativeWindows.IsWindowVisible(owner.Handle))
        {
            System.Windows.MessageBox.Show(historyWindow, "请先打开 Codex 的 Chat 页面，再恢复旁注。", "恢复旁注");
            return;
        }
        var item = record.Restore(owner, NativeWindows.WindowBounds(owner.Handle));
        history.Resume(record.Id);
        item.Persist = history.Save;
        var popup = new AnnotationWindow(item, annotationService)
        {
            ClosedByUser = OnAnnotationClosed, LayoutChanged = OnAnnotationLayoutChanged,
            DiagnosticLogged = (_, message) => Write(message), HistoryRequested = ShowHistory
        };
        var displays = NativeWindows.Displays();
        var screen = displays.FirstOrDefault(d => d.WorkArea.Intersects(record.Position));
        if (!screen.WorkArea.IsValid) screen = displays.FirstOrDefault();
        if (screen.WorkArea.IsValid) popup.StageOn(screen);
        var dpi = VisualTreeHelper.GetDpi(popup);
        var size = popup.MeasurePhysicalSize(dpi.DpiScaleX, dpi.DpiScaleY);
        var position = record.Position.IsValid ? record.Position : item.LastOwnerBounds;
        position = OverlayPlacement.ClampToArea(new ScreenRectangle(position.X, position.Y, size.WidthPx, size.HeightPx), screen.WorkArea.IsValid ? screen.WorkArea : item.LastOwnerBounds);
        annotations.Add(popup); RefreshAnnotationHandles();
        popup.Place(position); popup.Show(); popup.Place(position, true);
        item.RecordManualMove(position, item.LastOwnerBounds);
        popup.FocusInput();
    }

    private void CloseHistoryAnnotation(string id)
    {
        var popup = annotations.FirstOrDefault(a => a.Item.AnnotationId == id);
        popup?.CloseByUser();
        history?.Flush(); history?.Suspend(id);
    }

    private void OnAnnotationClosed(AnnotationWindow popup)
    {
        popup.Item.Persist?.Invoke(popup.Item, true);
        bool wasActive = NativeWindows.Foreground().Handle == popup.Handle || popup.IsActive;
        annotationService.NotifyAnnotationClosed(popup.Item);
        annotations.Remove(popup);
        if (activeTrackingAnnotationId == popup.Item.AnnotationId)
            activeTrackingAnnotationId = annotations.LastOrDefault()?.Item.AnnotationId;
        RefreshAnnotationHandles();
        Write($"annotation_closed annotation_id={popup.Item.AnnotationId} active_count={annotations.Count}");

        if (wasActive && !owner.IsEmpty)
            NativeWindows.RestoreForeground(owner.Handle);
    }

    private void OnAnnotationLayoutChanged(AnnotationWindow popup, string reason)
    {
        if (popup.Item.IsClosed || !popup.IsVisible)
            return;
        var dpi = VisualTreeHelper.GetDpi(popup);
        var (widthPx, heightPx) = popup.MeasurePhysicalSize(dpi.DpiScaleX, dpi.DpiScaleY);
        ScreenRectangle current = popup.Item.WindowPosition;
        if (!current.IsValid)
            return;

        nint monitor = NativeWindows.WindowDisplay(popup.Handle);
        DisplayArea display = NativeWindows.Displays().FirstOrDefault(d => d.Handle == monitor);
        ScreenRectangle area = display.WorkArea.IsValid ? display.WorkArea : popup.Item.LastOwnerBounds;
        var resized = OverlayPlacement.ClampToArea(new ScreenRectangle(current.X, current.Y, widthPx, heightPx), area);
        if (resized != current)
        {
            _ = popup.Place(resized, activate: false);
            Write(FormattableString.Invariant(
                $"position_updated annotation_id={popup.Item.AnnotationId} reason={reason} px=[{resized.X:0},{resized.Y:0},{resized.Width:0},{resized.Height:0}]"));
        }
    }

    private void UpdateAnnotationsForOwner(WindowIdentity codexWindow, ScreenRectangle windowBounds)
    {
        for (int i = 0; i < annotations.Count; i++)
        {
            AnnotationWindow popup = annotations[i];
            AnnotationItem item = popup.Item;
            if (item.IsClosed || item.OwnerWindow != codexWindow)
                continue;

            bool boundsChanged = windowBounds.IsValid && windowBounds != item.LastOwnerBounds;
            if (boundsChanged)
            {
                item.ApplyOwnerDelta(windowBounds);
                RepositionAnnotation(popup, "window_moved", i);
            }

            if (item.HiddenByForeground)
            {
                item.HiddenByForeground = false;
                if (!item.HiddenByViewport && !popup.IsVisible)
                {
                    popup.Show();
                    _ = popup.Place(item.WindowPosition, activate: false);
                    Write($"annotation_visibility=restored annotation_id={item.AnnotationId} reason=codex_foreground");
                }
            }
        }
    }

    private void UpdateAnnotationsForSample(WindowIdentity codexWindow, ScreenRectangle windowBounds, SelectionSample sample)
    {
        if (windowBounds.IsValid)
            UpdateAnnotationsForOwner(codexWindow, windowBounds);

        foreach (AnnotationWindow popup in annotations)
        {
            if (!popup.Item.IsClosed && popup.Item.OwnerWindow == codexWindow && sample.Viewport.IsValid)
                popup.Item.LastViewport = sample.Viewport.Intersect(windowBounds);
        }

        if (sample.State == SelectionSample.Selected && sample.Ranges.Length > 0)
        {
            string selectedText = string.Join("\n", sample.Ranges.Select(r => r.Text));
            ScreenRectangle[] rects = [.. sample.Ranges.SelectMany(r => r.Rectangles)];
            ScreenRectangle viewport = sample.Viewport.IsValid ? sample.Viewport.Intersect(windowBounds) : windowBounds;
            DisplayArea[] displays = NativeWindows.Displays();
            DisplayArea screen = OverlayPlacement.ChooseDisplay(rects, viewport, displays)
                ?? (displays.Length > 0 ? displays[0] : new DisplayArea(1, windowBounds, windowBounds));
            ScreenRectangle screenWork = screen.WorkArea.Intersects(viewport) ? screen.WorkArea : windowBounds;

            for (int i = 0; i < annotations.Count; i++)
            {
                AnnotationWindow popup = annotations[i];
                AnnotationItem item = popup.Item;
                if (item.IsClosed || item.OwnerWindow != codexWindow
                    || !string.Equals(item.SelectedText, selectedText, StringComparison.Ordinal))
                    continue;

                var dpi = VisualTreeHelper.GetDpi(popup);
                ScreenRectangle? newAnchor = OverlayPlacement.GetAnchorRectangle(
                    rects, viewport.Intersect(screenWork), dpi.DpiScaleX, dpi.DpiScaleY);
                if (newAnchor is not { } validAnchor)
                    continue;

                activeTrackingAnnotationId = item.AnnotationId;
                bool anchorMoved = Math.Abs(validAnchor.X - item.CurrentAnchorRect.X) >= 1
                    || Math.Abs(validAnchor.Y - item.CurrentAnchorRect.Y) >= 1
                    || Math.Abs(validAnchor.Width - item.CurrentAnchorRect.Width) >= 1
                    || Math.Abs(validAnchor.Height - item.CurrentAnchorRect.Height) >= 1;

                item.CurrentAnchorRect = validAnchor;
                if (item.HiddenByViewport)
                {
                    item.HiddenByViewport = false;
                    RepositionAnnotation(popup, "scroll_into_viewport", i);
                    if (!item.HiddenByForeground && !popup.IsVisible)
                    {
                        popup.Show();
                        _ = popup.Place(item.WindowPosition, activate: false);
                    }
                    Write($"annotation_visibility=restored annotation_id={item.AnnotationId} reason=scroll_into_viewport");
                }
                else if (anchorMoved && !item.IsManuallyMoved)
                {
                    RepositionAnnotation(popup, "scroll", i);
                }
            }
        }
        else if (sample.State == "selection_outside_viewport" && activeTrackingAnnotationId is { } trackedId)
        {
            AnnotationWindow? tracked = annotations.FirstOrDefault(
                a => !a.Item.IsClosed && a.Item.AnnotationId == trackedId && !a.Item.IsManuallyMoved);
            if (tracked is not null && !tracked.Item.HiddenByViewport)
            {
                tracked.Item.HiddenByViewport = true;
                if (tracked.IsVisible)
                    tracked.Hide();
                Write($"annotation_visibility=hidden annotation_id={tracked.Item.AnnotationId} reason=selection_outside_viewport");
            }
        }
    }

    private void RepositionAnnotation(AnnotationWindow popup, string reason, int index = -1)
    {
        AnnotationItem item = popup.Item;
        var dpi = VisualTreeHelper.GetDpi(popup);
        var (widthPx, heightPx) = popup.MeasurePhysicalSize(dpi.DpiScaleX, dpi.DpiScaleY);
        nint monitor = NativeWindows.WindowDisplay(popup.Handle);
        DisplayArea[] displays = NativeWindows.Displays();
        DisplayArea display = displays.FirstOrDefault(d => d.Handle == monitor);
        if (display.Handle == 0 && displays.Length > 0)
            display = displays[0];
        ScreenRectangle workArea = display.WorkArea.IsValid ? display.WorkArea : item.LastOwnerBounds;

        ScreenRectangle targetBounds;
        if (item.IsManuallyMoved)
        {
            var raw = new ScreenRectangle(
                item.LastOwnerBounds.X + item.ManualOffsetX,
                item.LastOwnerBounds.Y + item.ManualOffsetY,
                widthPx,
                heightPx);
            targetBounds = OverlayPlacement.ClampToArea(raw, workArea);
        }
        else
        {
            ScreenRectangle viewportWork = (item.LastViewport.IsValid ? item.LastViewport : item.LastOwnerBounds).Intersect(workArea);
            ScreenRectangle windowWork = item.LastOwnerBounds.Intersect(workArea);
            int limit = index >= 0 ? index : annotations.IndexOf(popup);
            ScreenRectangle[] occupied = [.. annotations
                .Where((a, idx) => idx != limit && !a.Item.IsClosed && a.IsVisible)
                .Select(a => a.Item.WindowPosition)];

            ButtonPlacement? placed = OverlayPlacement.PlaceAnnotation(
                item.CurrentAnchorRect, viewportWork, windowWork, widthPx, heightPx,
                8 * dpi.DpiScaleX, 8 * dpi.DpiScaleY, occupied);
            if (placed is null)
                return;
            item.PlacementSide = placed.Value.Side;
            targetBounds = placed.Value.Bounds;
        }

        if (targetBounds != item.WindowPosition)
        {
            _ = popup.Place(targetBounds, activate: false);
            Write(FormattableString.Invariant(
                $"position_updated annotation_id={item.AnnotationId} reason={reason} px=[{targetBounds.X:0},{targetBounds.Y:0},{targetBounds.Width:0},{targetBounds.Height:0}]"));
        }
    }

    private void SuspendAnnotationsByForeground()
    {
        foreach (AnnotationWindow popup in annotations)
        {
            if (popup.Item.IsClosed || popup.Item.HiddenByForeground)
                continue;
            popup.Item.HiddenByForeground = true;
            if (popup.IsVisible)
                popup.Hide();
            Write($"annotation_visibility=hidden annotation_id={popup.Item.AnnotationId} reason=not_foreground");
        }
    }

    private void RefreshAnnotationHandles()
    {
        nint[] handles = [.. annotations.Where(a => !a.Item.IsClosed).Select(a => a.Handle)];
        Volatile.Write(ref annotationHandles, handles);
    }

    private void Hide(string reason, SelectionTrace? metadata = null)
    {
        if (window is { IsVisible: true })
            window.Hide();
        if (reason is "not_foreground" or "no_foreground_window" or "annotation_opened")
            boundSnapshot = null;
        SelectionTrace trace = (metadata ?? SelectionTrace.Empty(reason)) with { Reason = reason };
        if (lastHiddenReason != reason || lastHiddenSelectionId != trace.Id)
        {
            Write($"overlay_visible=false overlay_hidden_reason={reason}");
            trace.Write("overlay_hidden", false);
        }
        lastHiddenReason = reason;
        lastHiddenSelectionId = trace.Id;
        lastPlacement = null;
    }

    private void Write(string message)
    {
        Console.WriteLine(FormattableString.Invariant($"overlay_t={clock.Elapsed.TotalSeconds:F3} {message}"));
        Console.Out.Flush();
    }

    public void Dispose()
    {
        lock (updates)
            disposed = true;
        dispatcher.Invoke(() =>
        {
            historyWindow?.Close();
            historyHotkey?.Dispose();
            foreach (AnnotationWindow popup in annotations.ToArray())
            {
                popup.ClosedByUser = null;
                annotationService.NotifyAnnotationClosed(popup.Item);
                popup.Close();
            }
            annotations.Clear();
            Volatile.Write(ref annotationHandles, []);
            window?.Close();
            Volatile.Write(ref handle, 0);
        });
        if (ownsService)
            annotationService.Dispose();
        history?.Flush();
        dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
        thread.Join();
    }
}
