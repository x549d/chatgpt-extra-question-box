using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;

namespace ContextNotes.SelectionProbe;

/// <summary>
/// Long-running background companion. Started by the Codex plugin SessionStart hook (via --launch), keeps running
/// until --stop, sign-out/shutdown, or Codex has been closed for a while. One instance per Windows user session.
/// </summary>
internal static class Companion
{
    private static readonly TimeSpan CodexAbsentLimit = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(2);

    private static string SessionName
    {
        get
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return $@"Local\ContextNotesNative.{identity.User?.Value}";
        }
    }

    private static string LogPath => Path.Combine(Diagnostics.LogDirectory, "companion.log");

    public static int Run()
    {
        string name = SessionName;
        using var instance = new Mutex(initiallyOwned: true, name + ".instance", out bool ownsInstance);
        if (!ownsInstance)
            return 0;

        using var log = new Diagnostics.RotatingLog(LogPath);
        TextWriter previous = Console.Out;
        Console.SetOut(log);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            log.WriteLine($"companion fatal error_type={args.ExceptionObject.GetType().Name}");
        try
        {
            using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, name + ".stop");
            using var cancellation = new CancellationTokenSource();
            RegisteredWaitHandle registration = ThreadPool.RegisterWaitForSingleObject(stop,
                (_, _) => cancellation.Cancel(), null, Timeout.Infinite, executeOnlyOnce: true);
            using var watchdog = new Timer(_ => WatchCodex(cancellation), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            log.WriteLine($"companion started {Diagnostics.ProcessSummary()}");
            try
            {
                using var overlay = new OverlayHost();
                while (!cancellation.IsCancellationRequested)
                {
                    try
                    {
                        new ProbeMonitor(fixtures: false, exerciseTimeout: false, overlay)
                            .RunAsync(Timeout.InfiniteTimeSpan, cancellation.Token).GetAwaiter().GetResult();
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        // Recover in place instead of exiting; the user should never need to restart anything.
                        log.WriteLine($"companion monitor_restart error_type={error.GetType().Name} hresult=0x{error.HResult:X8}");
                        overlay.Publish(null, "monitor_restart");
                        cancellation.Token.WaitHandle.WaitOne(RestartDelay);
                    }
                }
            }
            finally
            {
                using var unregistered = new ManualResetEvent(false);
                if (registration.Unregister(unregistered))
                    unregistered.WaitOne();
                log.WriteLine($"companion stopped reason={stopReason}");
            }
            return 0;
        }
        finally
        {
            Console.SetOut(previous);
            instance.ReleaseMutex();
        }
    }

    private static string stopReason = "stop_requested";
    private static long codexAbsentSince;

    private static void WatchCodex(CancellationTokenSource cancellation)
    {
        if (cancellation.IsCancellationRequested)
            return;
        if (NativeWindows.CodexRunning())
        {
            Interlocked.Exchange(ref codexAbsentSince, 0);
            return;
        }
        long now = Environment.TickCount64;
        long since = Interlocked.CompareExchange(ref codexAbsentSince, now, 0);
        if (since != 0 && now - since >= CodexAbsentLimit.TotalMilliseconds)
        {
            stopReason = "codex_closed";
            cancellation.Cancel();
        }
    }

    /// <summary>
    /// Entry point for the plugin hook. Returns immediately, writes nothing to stdout (SessionStart stdout would
    /// become model context) and starts the companion without inheriting the hook's pipes.
    /// </summary>
    public static int Launch()
    {
        string source = ReadHookSource();
        using var primary = new Diagnostics.RotatingLog(Path.Combine(Diagnostics.LogDirectory, "launch.log"));
        // Mirror into the plugin data directory so the hook result is visible even if Codex runs hooks under
        // a different Windows profile.
        string? pluginData = Environment.GetEnvironmentVariable("PLUGIN_DATA");
        using var mirror = string.IsNullOrEmpty(pluginData) ? null
            : new Diagnostics.RotatingLog(Path.Combine(pluginData, "launch.log"));
        void Log(string line)
        {
            primary.WriteLine(line);
            mirror?.WriteLine(line);
        }

        if (Mutex.TryOpenExisting(SessionName + ".instance", out Mutex? existing))
        {
            existing.Dispose();
            Log($"hook_fired source={source} companion=already_running");
            return 0;
        }
        try
        {
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing process path.");
            var start = new ProcessStartInfo(executable, "--companion")
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = AppContext.BaseDirectory
            };
            using Process? process = Process.Start(start);
            Log($"hook_fired source={source} companion=started pid={process?.Id} {Diagnostics.ProcessSummary()}");
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log($"hook_fired source={source} companion=start_failed error_type={error.GetType().Name}");
        }
        return 0;
    }

    private static string ReadHookSource()
    {
        try
        {
            if (!Console.IsInputRedirected)
                return "manual";
            Task<string> read = Console.In.ReadToEndAsync();
            if (!read.Wait(TimeSpan.FromMilliseconds(500)))
                return "no_input";
            using JsonDocument input = JsonDocument.Parse(read.Result);
            if (!input.RootElement.TryGetProperty("hook_event_name", out JsonElement hookEvent)
                || hookEvent.GetString() != "SessionStart"
                || !input.RootElement.TryGetProperty("source", out JsonElement source))
                return "unrecognized";
            return source.GetString() switch
            {
                "startup" => "SessionStart:startup",
                "resume" => "SessionStart:resume",
                "clear" => "SessionStart:clear",
                "compact" => "SessionStart:compact",
                "fork" => "SessionStart:fork",
                _ => "SessionStart:unrecognized"
            };
        }
        catch (Exception error) when (error is JsonException or IOException or AggregateException or InvalidOperationException)
        {
            return "unparsed";
        }
    }

    public static int Status(bool stopRequested)
    {
        if (!EventWaitHandle.TryOpenExisting(SessionName + ".stop", out EventWaitHandle? stop))
        {
            Console.WriteLine("companion_not_running");
            return 3;
        }

        using (stop)
        {
            if (stopRequested)
                stop.Set();
            Console.WriteLine(stop.WaitOne(0) ? "companion_stopping" : "companion_running");
            Console.WriteLine($"diagnostics={LogPath}");
            return 0;
        }
    }
}
