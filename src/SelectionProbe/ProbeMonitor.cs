using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ContextNotes.SelectionProbe;

internal sealed class ProbeMonitor(bool fixtures, bool exerciseTimeout)
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

    public async Task RunAsync(TimeSpan duration, CancellationToken cancellation)
    {
        Write($"ready seconds={duration.TotalSeconds} sample_ms={PollInterval.TotalMilliseconds} timeout_ms={RequestTimeout.TotalMilliseconds}");
        try
        {
            while (!cancellation.IsCancellationRequested && clock.Elapsed < duration)
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
            ResetWorker();
            Write("stopped");
        }
    }

    private void Tick()
    {
        WindowIdentity current = NativeWindows.Foreground();
        if (current != foreground)
        {
            ResetWorker();
            foreground = current;
            isCodex = NativeWindows.IsCodex(foreground);
            eventsUnavailable = false;
            nextSample = clock.Elapsed;
            Write(isCodex
                ? $"foreground=codex hwnd=0x{foreground.Handle:X} pid={foreground.ProcessId}"
                : $"foreground=inactive hwnd=0x{foreground.Handle:X} pid={foreground.ProcessId}; selection invalidated");
        }
        if (!isCodex)
            return;

        if (request is not null)
        {
            if (request.IsCompleted)
                Receive();
            else if (clock.Elapsed - requestedAt >= RequestTimeout)
            {
                Write($"watchdog operation={operation} worker={worker!.Id} elapsed_ms={(clock.Elapsed - requestedAt).TotalMilliseconds:F0}; selection invalidated");
                eventsUnavailable |= operation == Operation.Subscribe;
                ResetWorker();
                nextSample = clock.Elapsed + PollInterval;
            }
            return;
        }

        if (clock.Elapsed < nextSample)
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
        request = worker.RequestAsync(operation);
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

            SelectionSample sample = reply.Sample ?? throw new InvalidDataException("Missing selection sample.");
            Record(sample);
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException)
        {
            Write($"worker fault={error.GetType().Name}; selection invalidated");
            ResetWorker();
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
                    Write(match.Evidence());
                if (exerciseTimeout && !stallExercised && match is { Exact: true, Fixture.Name: "prose" })
                    stallPending = true;
            }
        }
        lastSample = sample;
        lastGeometry = geometry;
    }

    private void ResetWorker()
    {
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
