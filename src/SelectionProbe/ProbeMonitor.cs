using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ContextNotes.SelectionProbe;

internal sealed class ProbeMonitor(bool fixtures, bool exerciseTimeout, OverlayHost? overlay = null)
{
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan LoopInterval = TimeSpan.FromMilliseconds(55);
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private WindowIdentity foreground;
    private bool isCodex;
    private ProbeWorker? worker;
    private bool eventsAttempted;
    private bool eventsUnavailable;
    private bool stallPending;
    private bool stallExercised;
    private Task<WorkerReply>? request;
    private Operation operation;
    private TimeSpan requestedAt;
    private TimeSpan nextSample;
    private SelectionSample? lastSample;
    private WindowGeometry? lastGeometry;
    private ScreenRectangle windowBounds;
    private bool selectingWithMouse;
    private long selectionGeneration;
    private long requestedGeneration;

    public async Task RunAsync(TimeSpan duration, CancellationToken cancellation)
    {
        string limit = duration == Timeout.InfiniteTimeSpan ? "continuous" : $"{duration.TotalSeconds}";
        Write($"ready seconds={limit} sample_ms={PollInterval.TotalMilliseconds} timeout_ms={RequestTimeout.TotalMilliseconds}");
        try
        {
            while (!cancellation.IsCancellationRequested
                && (duration == Timeout.InfiniteTimeSpan || clock.Elapsed < duration))
            {
                Tick();
                await Task.Delay(LoopInterval, cancellation);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Ctrl+C ends the probe cleanly.
        }
        finally
        {
            ResetWorker("stopped");
            Write("stopped");
        }
    }

    private void Tick()
    {
        WindowIdentity current = NativeWindows.Foreground();
        if (current != foreground)
        {
            ResetWorker("foreground-change");
            foreground = current;
            isCodex = NativeWindows.IsCodex(foreground);
            windowBounds = default;
            selectingWithMouse = false;
            eventsUnavailable = false;
            nextSample = clock.Elapsed;
            Write(isCodex
                ? $"foreground=codex hwnd=0x{foreground.Handle:X} pid={foreground.ProcessId}"
                : $"foreground=inactive hwnd=0x{foreground.Handle:X} pid={foreground.ProcessId}; selection invalidated");
        }
        if (!isCodex)
            return;

        if (overlay is not null)
            CheckWindowAndMouse(overlay);

        if (request is not null)
        {
            if (request.IsCompleted)
                Receive();
            else if (clock.Elapsed - requestedAt >= RequestTimeout)
            {
                Write($"watchdog operation={operation} worker={worker!.Id} elapsed_ms={(clock.Elapsed - requestedAt).TotalMilliseconds:F0}; selection invalidated");
                eventsUnavailable |= operation == Operation.Subscribe;
                ResetWorker("uia-timeout");
                nextSample = clock.Elapsed + PollInterval;
            }
            return;
        }

        if (selectingWithMouse || clock.Elapsed < nextSample)
            return;

        if (worker is null)
        {
            try
            {
                worker = new ProbeWorker(foreground);
                Write($"worker started pid={worker.Id}");
            }
            catch (Win32Exception)
            {
                Write("worker could not start; will retry");
                nextSample = clock.Elapsed + RequestTimeout;
                return;
            }
        }

        operation = Operation.Sample;
        if (stallPending)
        {
            stallPending = false;
            stallExercised = true;
            operation = Operation.Stall;
            Write("induced worker stall started");
        }
        else if (!eventsAttempted && !eventsUnavailable && lastSample is not null)
        {
            eventsAttempted = true;
            operation = Operation.Subscribe;
        }

        requestedAt = clock.Elapsed;
        requestedGeneration = selectionGeneration;
        request = worker.RequestAsync(operation);
    }

    private void CheckWindowAndMouse(OverlayHost overlay)
    {
        ScreenRectangle bounds = NativeWindows.WindowBounds(foreground.Handle);
        bool dragging = NativeWindows.LeftButtonDown() && !overlay.PointerOverButton;
        if (bounds == windowBounds && dragging == selectingWithMouse)
            return;

        selectionGeneration++;
        if (dragging || bounds != windowBounds)
            overlay.Publish(null, dragging ? "pointer-selection" : "window-moved");
        windowBounds = bounds;
        selectingWithMouse = dragging;
        nextSample = clock.Elapsed;
    }

    private void Receive()
    {
        Task<WorkerReply> completed = request!;
        request = null;
        nextSample = clock.Elapsed + PollInterval;
        try
        {
            WorkerReply reply = completed.GetAwaiter().GetResult();
            if (NativeWindows.Foreground() != foreground)
                return;
            if (operation == Operation.Subscribe)
            {
                eventsUnavailable = !reply.Subscribed;
                Write($"selection events subscribed={reply.Subscribed}");
                return;
            }
            if (selectingWithMouse || requestedGeneration != selectionGeneration)
            {
                nextSample = clock.Elapsed;
                return;
            }

            SelectionSample sample = reply.Sample ?? throw new InvalidDataException("Missing selection sample.");
            Record(sample);
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException)
        {
            Write($"worker fault={error.GetType().Name}; selection invalidated");
            ResetWorker("worker-fault");
            nextSample = clock.Elapsed + RequestTimeout;
        }
    }

    private void Record(SelectionSample sample)
    {
        ScreenRectangle[] rectangles = sample.Ranges.SelectMany(range => range.Rectangles).ToArray();
        WindowGeometry geometry = NativeWindows.Geometry(foreground, rectangles);
        if (NativeWindows.Foreground() != foreground)
            return;

        bool selectionChanged = lastSample is null || sample.State != lastSample.State
            || !sample.Ranges.Select(range => (range.Text, range.Region)).SequenceEqual(
                lastSample.Ranges.Select(range => (range.Text, range.Region)));
        bool geometryChanged = geometry != lastGeometry
            || !rectangles.SequenceEqual(lastSample?.Ranges.SelectMany(range => range.Rectangles) ?? []);
        long eventDelta = sample.SelectionEvents - (lastSample?.SelectionEvents ?? 0);
        if (overlay is not null)
        {
            bool assistant = sample.State == SelectionSample.Selected && sample.Ranges.Length > 0
                && sample.Ranges.All(range => range.Region == SelectionRegion.Assistant
                    && !string.IsNullOrWhiteSpace(range.Text));
            bool valid = assistant && rectangles.Any(OverlayPlacement.IsCandidate) && geometry.Bounds == windowBounds;
            overlay.Publish(valid ? new OverlaySelection(foreground, geometry.Bounds, rectangles) : null,
                assistant ? "no-visible-rectangle" : sample.State == SelectionSample.Selected ? "non-assistant" : sample.State);
            if (valid && exerciseTimeout && !stallExercised)
                stallPending = true;
        }

        if (selectionChanged || geometryChanged || eventDelta != 0)
        {
            Write($"state={sample.State} changed={selectionChanged} events={sample.SelectionEvents} event_delta={eventDelta} " +
                $"dpi={geometry.Dpi} monitor=0x{geometry.Monitor:X} monitor_count={geometry.MonitorCount} " +
                $"cursor_rect={geometry.CursorRectangle} window={Coordinates(geometry.Bounds)}");
            foreach (SelectedRange range in sample.Ranges)
            {
                Write($"range utf16={range.Text.Length} region={range.Region} path={string.Join('/', range.ControlPath)} " +
                    $"rectangles={string.Join(' ', range.Rectangles.Select(Coordinates))}");
                FixtureMatch? match = fixtures ? Fixtures.Match(range.Text) : null;
                if (match is not null)
                    Write($"fixture={match.Name} comparison={(match.Exact ? "ordinal-equal" : "line-endings-differ")}");
                if (exerciseTimeout && !stallExercised && match is { Exact: true, Name: "prose" })
                    stallPending = true;
            }
        }
        lastSample = sample;
        lastGeometry = geometry;
    }

    private void ResetWorker(string reason)
    {
        selectionGeneration++;
        overlay?.Publish(null, reason);
        worker?.Dispose();
        worker = null;
        if (request is { } abandoned)
        {
            _ = abandoned.ContinueWith(task => _ = task.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        request = null;
        eventsAttempted = false;
        stallPending = false;
        lastSample = null;
        lastGeometry = null;
    }

    private static string Coordinates(ScreenRectangle rectangle) =>
        FormattableString.Invariant($"[{rectangle.X},{rectangle.Y},{rectangle.Width},{rectangle.Height}]");

    private void Write(string message)
    {
        Console.WriteLine($"t={clock.Elapsed.TotalSeconds:F3} {message}");
        Console.Out.Flush();
    }
}
