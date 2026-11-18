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
    string SelectedText = "");

internal sealed class OverlayHost : IDisposable
{
    private readonly Thread thread;
    private readonly Dispatcher dispatcher;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly object updates = new();
    private readonly List<AnnotationWindow> annotations = [];
    private OverlaySelection? latest;
    private SelectionSnapshot? boundSnapshot;
    private string hideReason = "not_foreground";
    private long generation;
    private bool queued;
    private bool disposed;
    private nint handle;
    private nint[] annotationHandles = [];
    private QuestionOverlay? window;
    private WindowIdentity owner;
    private ButtonPlacement? lastPlacement;
    private string? lastHiddenReason;
    private string? activeTrackingAnnotationId;
    private int nextAnnotationNumber;

    public OverlayHost()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        thread = new Thread(() =>
        {
            Dispatcher.CurrentDispatcher.UnhandledException += (_, args) =>
            {
                // A rendering fault must hide the entry, never take down the companion.
                args.Handled = true;
                Write($"overlay fault error_type={args.Exception.GetType().Name}");
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

    public void Publish(OverlaySelection? selection, string reason = "selection_empty")
    {
        lock (updates)
        {
            if (disposed)
                return;
            latest = selection;
            hideReason = reason;
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
        long expected;
        lock (updates)
        {
            queued = false;
            selection = latest;
            reason = hideReason;
            expected = generation;
        }

        if (selection is null)
        {
            // Do not hide the entry button mid-click while the user is pressing it.
            if (reason is not ("not_foreground" or "annotation_opened")
                && window is { IsVisible: true }
                && boundSnapshot is not null
                && PointerOverButton
                && NativeWindows.LeftButtonDown())
            {
                return;
            }
            Hide(reason);
            if (reason == "not_foreground")
                SuspendAnnotationsByForeground();
            return;
        }
        if (!IsCurrent(selection, expected))
        {
            Hide("stale_selection");
            return;
        }

        ScreenRectangle viewport = selection.Viewport.Intersect(selection.WindowBounds);
        DisplayArea? display = OverlayPlacement.ChooseDisplay(selection.Rectangles, viewport,
            NativeWindows.Displays());
        if (display is not { } screen)
        {
            Hide("selection_outside_viewport");
            return;
        }

        try
        {
            if (window is null)
            {
                window = new QuestionOverlay { Clicked = OnClick };
                Volatile.Write(ref handle, window.Handle);
            }
            if (NativeWindows.WindowDisplay(window.Handle) != screen.Handle)
            {
                Hide("display_change");
                if (!window.StageOn(screen))
                {
                    Hide("overlay_position_invalid");
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
                Hide(anchor is null ? "invalid_rectangle" : "overlay_position_invalid");
                return;
            }
            if (!IsCurrent(selection, expected))
            {
                Hide("stale_selection");
                return;
            }

            bool showing = !window.IsVisible;
            if ((showing || target != lastPlacement) && !window.Place(target.Bounds))
            {
                Hide("overlay_position_invalid");
                return;
            }
            owner = selection.Window;
            if (!string.IsNullOrWhiteSpace(selection.SelectedText))
                boundSnapshot = SelectionSnapshot.FromSelection(selection, validAnchor);
            if (showing)
            {
                window.Show();
                if (NativeWindows.WindowBounds(window.Handle) != target.Bounds && !window.Place(target.Bounds))
                {
                    Hide("overlay_position_invalid");
                    return;
                }
            }
            if (showing || target != lastPlacement)
            {
                var bounds = NativeWindows.WindowBounds(window.Handle);
                Write(FormattableString.Invariant(
                    $"overlay_visible=true change={(showing ? "shown" : "moved")} side={target.Side} px=[{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}] viewport=[{viewport.X},{viewport.Y},{viewport.Width},{viewport.Height}] dpi={dpi.PixelsPerInchX:F0}"));
            }
            lastPlacement = target;
            lastHiddenReason = null;
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            Hide("overlay_position_invalid");
            Write($"overlay error={error.GetType().Name}");
        }
    }

    private bool IsCurrent(OverlaySelection selection, long expected)
    {
        lock (updates)
        {
            if (disposed || expected != generation)
                return false;
        }
        WindowIdentity currentForeground = NativeWindows.Foreground();
        return (currentForeground == selection.Window || IsAnnotationWindow(currentForeground.Handle))
            && NativeWindows.WindowBounds(selection.Window.Handle) == selection.WindowBounds
            && (!NativeWindows.LeftButtonDown() || PointerOverButton || PointerOverAnnotation);
    }

    private void OnClick()
    {
        WindowIdentity currentForeground = NativeWindows.Foreground();
        bool codexForeground = currentForeground == owner
            || NativeWindows.IsCodex(currentForeground)
            || IsAnnotationWindow(currentForeground.Handle);
        if (!codexForeground || window is not { IsActive: false })
            return;

        SelectionSnapshot? snapshot = boundSnapshot;
        boundSnapshot = null;
        Write($"annotation_entry_clicked codex_foreground=true has_snapshot={snapshot is not null} selected_text_length={snapshot?.SelectedText.Length ?? 0}");
        Hide("annotation_opened");
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

        string id = $"ann-{++nextAnnotationNumber}";
        var item = new AnnotationItem(id, snapshot);
        var popup = new AnnotationWindow(item)
        {
            DiagnosticLogged = (_, message) => Write(message),
            ClosedByUser = OnAnnotationClosed,
            LayoutChanged = OnAnnotationLayoutChanged
        };

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

        ScreenRectangle actualBounds = NativeWindows.WindowBounds(popup.Handle);
        Write(FormattableString.Invariant(
            $"annotation_created annotation_id={item.AnnotationId} side={placement.Side} selected_text_length={item.SelectedText.Length} px=[{actualBounds.X:0},{actualBounds.Y:0},{actualBounds.Width:0},{actualBounds.Height:0}] focus_requested={focusRequested} active_count={annotations.Count}"));
        return popup;
    }

    private void OnAnnotationClosed(AnnotationWindow popup)
    {
        bool wasActive = NativeWindows.Foreground().Handle == popup.Handle || popup.IsActive;
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

    private void Hide(string reason)
    {
        if (window is { IsVisible: true })
            window.Hide();
        if (reason is "not_foreground" or "annotation_opened")
            boundSnapshot = null;
        if (lastHiddenReason != reason)
            Write($"overlay_visible=false overlay_hidden_reason={reason}");
        lastHiddenReason = reason;
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
            foreach (AnnotationWindow popup in annotations.ToArray())
            {
                popup.ClosedByUser = null;
                popup.Close();
            }
            annotations.Clear();
            Volatile.Write(ref annotationHandles, []);
            window?.Close();
            Volatile.Write(ref handle, 0);
        });
        dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
        thread.Join();
    }
}
