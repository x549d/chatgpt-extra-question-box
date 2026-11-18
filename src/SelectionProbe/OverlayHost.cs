using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;

namespace ContextNotes.SelectionProbe;

internal sealed record OverlaySelection(WindowIdentity Window, ScreenRectangle WindowBounds, ScreenRectangle[] Rectangles);

internal sealed class OverlayHost : IDisposable
{
    private readonly Thread thread;
    private readonly Dispatcher dispatcher;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly object updates = new();
    private OverlaySelection? latest;
    private string hideReason = "inactive";
    private long generation;
    private bool queued;
    private bool disposed;
    private nint handle;
    private QuestionOverlay? window;
    private WindowIdentity owner;
    private ButtonPlacement? lastPlacement;

    public OverlayHost()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        thread = new Thread(() =>
        {
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        }) { IsBackground = true, Name = "Context Notes Overlay" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        dispatcher = ready.Task.GetAwaiter().GetResult();
    }

    public bool PointerOverButton => NativeWindows.PointerOverWindow(Volatile.Read(ref handle));

    public void Publish(OverlaySelection? selection, string reason = "invalid-selection")
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
            Hide(reason);
            return;
        }
        if (!IsCurrent(selection, expected))
        {
            Hide("stale-selection");
            return;
        }

        DisplayArea? display = OverlayPlacement.ChooseDisplay(selection.Rectangles, selection.WindowBounds,
            NativeWindows.Displays());
        if (display is not { } screen)
        {
            Hide("no-visible-rectangle");
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
                Hide("display-change");
                if (!window.StageOn(screen))
                    return;
            }

            var dpi = VisualTreeHelper.GetDpi(window);
            ScreenRectangle visible = selection.WindowBounds.Intersect(screen.WorkArea);
            ScreenRectangle? anchor = OverlayPlacement.GetAnchorRectangle(selection.Rectangles, visible,
                dpi.DpiScaleX, dpi.DpiScaleY);
            ButtonPlacement? placement = anchor is { } box
                ? OverlayPlacement.Place(box, screen.WorkArea, (int)Math.Ceiling(window.Width * dpi.DpiScaleX),
                    (int)Math.Ceiling(window.Height * dpi.DpiScaleY), 7 * dpi.DpiScaleX, 7 * dpi.DpiScaleY)
                : null;
            if (placement is not { } target)
            {
                Hide("no-fit");
                return;
            }
            if (!IsCurrent(selection, expected))
            {
                Hide("stale-selection");
                return;
            }

            bool showing = !window.IsVisible;
            if ((showing || target != lastPlacement) && !window.Place(target.Bounds))
            {
                Hide("position-failed");
                return;
            }
            owner = selection.Window;
            if (showing)
            {
                window.Show();
                if (NativeWindows.WindowBounds(window.Handle) != target.Bounds && !window.Place(target.Bounds))
                {
                    Hide("position-failed");
                    return;
                }
            }
            if (showing || target != lastPlacement)
            {
                var bounds = NativeWindows.WindowBounds(window.Handle);
                Write(FormattableString.Invariant(
                    $"overlay={(showing ? "shown" : "moved")} side={target.Side} px=[{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}] dpi={dpi.PixelsPerInchX:F0}"));
            }
            lastPlacement = target;
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            Hide("window-error");
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
        return NativeWindows.Foreground() == selection.Window
            && NativeWindows.WindowBounds(selection.Window.Handle) == selection.WindowBounds
            && (!NativeWindows.LeftButtonDown() || PointerOverButton);
    }

    private void OnClick()
    {
        if (NativeWindows.Foreground() == owner && window is { IsActive: false })
            Write("overlay=clicked codex_foreground=true");
    }

    private void Hide(string reason)
    {
        if (window is { IsVisible: true })
        {
            window.Hide();
            Write($"overlay=hidden reason={reason}");
        }
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
            window?.Close();
            Volatile.Write(ref handle, 0);
        });
        dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
        thread.Join();
    }
}
