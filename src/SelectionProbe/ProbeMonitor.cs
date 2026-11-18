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
    private static long nextSelectionId;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private WindowIdentity foreground;
    private bool foregroundObserved;
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
    private SelectionTrace lastTrace = SelectionTrace.Empty("waiting_for_foreground");

    public async Task RunAsync(TimeSpan duration, CancellationToken cancellation)
    {
        string limit = duration == Timeout.InfiniteTimeSpan ? "continuous" : $"{duration.TotalSeconds}";
        Write($"ready pid={Environment.ProcessId} seconds={limit} sample_ms={PollInterval.TotalMilliseconds} timeout_ms={RequestTimeout.TotalMilliseconds}");
        lastTrace.Write("probe_monitor_started", false);
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
                overlay.Publish(null, "annotation_focused", lastTrace);
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

        if (!foregroundObserved || current != foreground)
        {
            foregroundObserved = true;
            ResetWorker(current.IsEmpty ? "no_foreground_window" : "not_foreground");
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
                : current.IsEmpty ? "foreground_window=none selection_valid=false reason=no_foreground_window"
                : "foreground_window=other selection_valid=false reason=not_foreground");
            (lastTrace with { Reason = isCodex ? "codex_foreground" : lastTrace.Reason }).Write("foreground_classified", false);
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
                (lastTrace with { Reason = "uia_timeout" }).Write("probe_request_failed", false, typeof(TimeoutException));
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
            catch (Win32Exception error)
            {
                Write("worker could not start; will retry");
                (lastTrace with { Reason = "uia_worker_start_failed" }).Write("probe_request_failed", false, error.GetType());
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
            overlay.Publish(null, dragging ? "pointer_selection" : "window_moved", lastTrace);
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
            {
                (lastTrace with { Reason = "foreground_changed" }).Write("selection_sample_dropped", false);
                return;
            }
            if (operation == Operation.Subscribe)
            {
                eventsUnavailable = !reply.Subscribed;
                Write($"selection events subscribed={reply.Subscribed}");
                (lastTrace with { Reason = reply.Subscribed ? "subscribed" : "polling_only" }).Write("uia_subscription_ready");
                return;
            }
            if (selectingWithMouse || requestedGeneration != selectionGeneration)
            {
                nextSample = clock.Elapsed;
                (lastTrace with { Reason = selectingWithMouse ? "pointer_selection" : "selection_generation_changed" })
                    .Write("selection_sample_dropped", false);
                return;
            }

            SelectionSample sample = reply.Sample ?? throw new InvalidDataException("Missing selection sample.");
            Record(sample, clock.Elapsed - requestedAt);
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException)
        {
            Write($"worker fault={error.GetType().Name}; selection invalidated");
            (lastTrace with { Reason = "uia_worker_fault" }).Write("probe_request_failed", false, error.GetType());
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
        {
            (lastTrace with { Reason = "foreground_changed" }).Write("selection_sample_dropped", false);
            return;
        }

        bool selectionChanged = lastSample is null || sample.State != lastSample.State || sample.Mode != lastSample.Mode
            || !sample.Ranges.Select(range => (range.Text, range.Region)).SequenceEqual(
                lastSample.Ranges.Select(range => (range.Text, range.Region)));
        bool geometryChanged = geometry != lastGeometry || sample.Viewport != lastSample?.Viewport
            || !rectangles.SequenceEqual(lastSample?.Ranges.SelectMany(range => range.Rectangles) ?? []);
        long eventDelta = sample.SelectionEvents - (lastSample?.SelectionEvents ?? 0);
        string role = sample.Ranges.Length > 0 ? sample.Ranges[0].Region.ToString().ToLowerInvariant() : "none";
        string reason = EvaluateSelection(sample, rectangles, overlay is null || geometry.Bounds == windowBounds);
        bool valid = reason == "eligible";
        long id = sample.Ranges.Length == 0 ? 0 : selectionChanged ? Interlocked.Increment(ref nextSelectionId) : lastTrace.Id;
        var trace = new SelectionTrace(sample.Mode, id, sample.Ranges.Sum(range => range.Text.Length), rectangles.Length, role,
            annotationFocused ? "annotation_focused" : reason);
        bool changed = selectionChanged || geometryChanged || eventDelta != 0 || trace != lastTrace;
        if (eventDelta > 0)
            trace.Write("selection_event_received");
        if (changed)
        {
            if (trace.TextLength > 0)
                trace.Write("selection_text_read");
            if (trace.Id != 0)
                trace.Write("selection_role_classified");
            trace.Write(trace.Reason == "eligible" ? "selection_eligible" : "selection_ineligible");
        }
        lastTrace = trace;
        if (overlay is not null)
        {
            overlay.Publish(CreateOverlaySelection(foreground, geometry.Bounds, sample, rectangles, trace), trace.Reason, trace);
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
        overlay?.Publish(null, reason, lastTrace);
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
        lastTrace = SelectionTrace.Empty(reason);
    }

    internal static string EvaluateSelection(SelectionSample sample, ScreenRectangle[] rectangles, bool boundsMatch)
    {
        if (sample.State != SelectionSample.Selected)
            return sample.State;
        if (sample.Mode != "chat")
            return "chat_mode_unconfirmed";
        foreach (SelectedRange range in sample.Ranges)
            if (range.Region != SelectionRegion.Assistant)
                return $"role_{range.Region.ToString().ToLowerInvariant()}";
        if (sample.Ranges.Length == 0 || sample.Ranges.Any(range => string.IsNullOrWhiteSpace(range.Text)))
            return "selection_empty_text";
        if (!boundsMatch)
            return "window_moved";
        if (!sample.Viewport.IsValid)
            return "invalid_viewport";
        return rectangles.Any(OverlayPlacement.IsCandidate) ? "eligible" : "invalid_rectangle";
    }

    internal static OverlaySelection? CreateOverlaySelection(WindowIdentity owner, ScreenRectangle bounds, SelectionSample sample,
        ScreenRectangle[] rectangles, SelectionTrace trace) => trace.Reason == "eligible"
        ? new(owner, bounds, sample.Viewport, rectangles, string.Join("\n", sample.Ranges.Select(range => range.Text)), trace)
        : null;

    private static string Coordinates(ScreenRectangle rectangle) =>
        FormattableString.Invariant($"[{rectangle.X},{rectangle.Y},{rectangle.Width},{rectangle.Height}]");

    private void Write(string message)
    {
        Console.WriteLine($"t={clock.Elapsed.TotalSeconds:F3} {message}");
        Console.Out.Flush();
    }
}
