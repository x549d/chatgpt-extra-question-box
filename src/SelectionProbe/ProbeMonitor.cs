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
    private bool annotationFocused;
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
        Write($"ready pid={Environment.ProcessId} seconds={limit} sample_ms={PollInterval.TotalMilliseconds} timeout_ms={RequestTimeout.TotalMilliseconds}");
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
        if (overlay is not null && overlay.IsAnnotationWindow(current.Handle))
        {
            if (!annotationFocused)
            {
                annotationFocused = true;
                overlay.Publish(null, "annotation_focused");
            }
            if (isCodex && !foreground.IsEmpty)
                CheckWindowAndMouse(overlay);
            return;
        }

        if (annotationFocused)
        {
            annotationFocused = false;
            nextSample = clock.Elapsed;
        }

        if (current != foreground)
        {
            ResetWorker("not_foreground");
            foreground = current;
            isCodex = NativeWindows.IsCodex(foreground);
            windowBounds = default;
            selectingWithMouse = false;
            eventsUnavailable = false;
            nextSample = clock.Elapsed;
            if (isCodex && overlay is not null)
            {
                windowBounds = NativeWindows.WindowBounds(foreground.Handle);
                overlay.NotifyOwnerGeometry(foreground, windowBounds);
            }
            Write(isCodex
                ? $"foreground_window=desktop hwnd=0x{foreground.Handle:X} pid={foreground.ProcessId}"
                : "foreground_window=other selection_valid=false reason=not_foreground");
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
                Write($"watchdog operation={operation} worker={worker!.Id} uia_latency_ms={(clock.Elapsed - requestedAt).TotalMilliseconds:F0} selection_valid=false");
                eventsUnavailable |= operation == Operation.Subscribe;
                ResetWorker("uia_timeout");
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
        bool dragging = NativeWindows.LeftButtonDown() && !overlay.PointerOverButton && !overlay.PointerOverAnnotation;
        if (bounds == windowBounds && dragging == selectingWithMouse)
            return;

        selectionGeneration++;
        if (bounds != windowBounds && bounds.IsValid)
            overlay.NotifyOwnerGeometry(foreground, bounds);
        if (dragging || bounds != windowBounds)
            overlay.Publish(null, dragging ? "pointer_selection" : "window_moved");
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
            WindowIdentity current = NativeWindows.Foreground();
            if (current != foreground && overlay?.IsAnnotationWindow(current.Handle) != true)
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
            Record(sample, clock.Elapsed - requestedAt);
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException)
        {
            Write($"worker fault={error.GetType().Name}; selection invalidated");
            ResetWorker("uia_worker_fault");
            nextSample = clock.Elapsed + RequestTimeout;
        }
    }

    private void Record(SelectionSample sample, TimeSpan latency)
    {
        ScreenRectangle[] rectangles = sample.Ranges.SelectMany(range => range.Rectangles).ToArray();
        WindowGeometry geometry = NativeWindows.Geometry(foreground, rectangles);
        WindowIdentity current = NativeWindows.Foreground();
        if (current != foreground && overlay?.IsAnnotationWindow(current.Handle) != true)
            return;

        bool selectionChanged = lastSample is null || sample.State != lastSample.State || sample.Mode != lastSample.Mode
            || !sample.Ranges.Select(range => (range.Text, range.Region)).SequenceEqual(
                lastSample.Ranges.Select(range => (range.Text, range.Region)));
        bool geometryChanged = geometry != lastGeometry || sample.Viewport != lastSample?.Viewport
            || !rectangles.SequenceEqual(lastSample?.Ranges.SelectMany(range => range.Rectangles) ?? []);
        long eventDelta = sample.SelectionEvents - (lastSample?.SelectionEvents ?? 0);
        bool assistant = sample.State == SelectionSample.Selected && sample.Ranges.Length > 0
            && sample.Ranges.All(range => range.Region == SelectionRegion.Assistant
                && !string.IsNullOrWhiteSpace(range.Text));
        bool valid = assistant && rectangles.Any(OverlayPlacement.IsCandidate) && sample.Viewport.IsValid
            && (overlay is null || geometry.Bounds == windowBounds);
        string role = sample.Ranges.Length > 0 ? sample.Ranges[0].Region.ToString().ToLowerInvariant() : "none";
        string reason = valid ? "eligible" : assistant ? "invalid_rectangle"
            : sample.State == SelectionSample.Selected ? $"role_{role}" : sample.State;
        if (overlay is not null)
        {
            string selectedText = valid ? string.Join("\n", sample.Ranges.Select(r => r.Text)) : "";
            overlay.Publish(
                valid && !annotationFocused
                    ? new OverlaySelection(foreground, geometry.Bounds, sample.Viewport, rectangles, selectedText)
                    : null,
                annotationFocused ? "annotation_focused" : reason);
            overlay.NotifySelectionSample(foreground, geometry.Bounds, sample);
            if (valid && exerciseTimeout && !stallExercised)
                stallPending = true;
        }

        if (selectionChanged || geometryChanged || eventDelta != 0)
        {
            Write($"mode_detection={sample.Mode} selection_detected={sample.Ranges.Length > 0} selection_role={role} " +
                $"selection_rect_count={rectangles.Length} selection_valid={valid} reason={reason} uia_latency_ms={latency.TotalMilliseconds:F0} " +
                $"changed={selectionChanged} events={sample.SelectionEvents} event_delta={eventDelta} " +
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
