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

    internal static string SessionName
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
        if (!Diagnostics.RequireInteractiveDesktop("companion"))
            return Diagnostics.NonInteractiveDesktopExitCode;

        string name = SessionName;
        using var instance = new Mutex(initiallyOwned: true, name + ".instance", out bool ownsInstance);
        if (!ownsInstance)
            return 0;

        using var log = new Diagnostics.RotatingLog(LogPath);
        TextWriter previous = Console.Out;
        Console.SetOut(log);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            log.WriteLine(SelectionTrace.Empty("companion_fatal").Format("companion_failed", false, args.ExceptionObject.GetType()));
        try
        {
            using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, name + ".stop");
            using var cancellation = new CancellationTokenSource();
            RegisteredWaitHandle registration = ThreadPool.RegisterWaitForSingleObject(stop,
                (_, _) => cancellation.Cancel(), null, Timeout.Infinite, executeOnlyOnce: true);
            using var watchdog = new Timer(_ => WatchCodex(cancellation), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            log.WriteLine($"companion started {Diagnostics.ProcessSummary()}");
            SelectionTrace.Empty("waiting_for_foreground").Write("companion_started", false);
            try
            {
                using var history = new HistoryPersistence(new HistoryDatabase(HistoryDatabase.DefaultPath));
                using var overlay = new OverlayHost(history: history);
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
                        SelectionTrace.Empty("monitor_restart").Write("probe_monitor_failed", false, error.GetType());
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
    /// Entry point for the plugin hook. The launcher detaches the companion and bounds the readiness check.
    /// SessionStart stdout would become model context, so all status goes to local diagnostics.
    /// </summary>
    public static int Launch() => Launch("hook_fired", ReadHookSource(), Environment.GetEnvironmentVariable("PLUGIN_DATA"));

    public static int LaunchFromDesktop() => Launch("desktop_startup", "windows_foreground", null);

    private static int Launch(string entry, string source, string? pluginData)
    {
        using var launchGate = new Mutex(false, SessionName + ".launch");
        bool entered;
        try
        {
            entered = launchGate.WaitOne(TimeSpan.FromSeconds(4));
        }
        catch (AbandonedMutexException)
        {
            entered = true;
        }
        if (!entered)
            return 1;

        try
        {
            using var primary = new Diagnostics.RotatingLog(Path.Combine(Diagnostics.LogDirectory, "launch.log"));
            // The plugin data mirror is visible even if the hook runs in a different Windows profile.
            using var mirror = string.IsNullOrEmpty(pluginData) ? null
                : new Diagnostics.RotatingLog(Path.Combine(pluginData, "launch.log"));
            void Log(string line)
            {
                string record = $"{entry} source={source} {line}";
                primary.WriteLine(record);
                mirror?.WriteLine(record);
            }

            if (Mutex.TryOpenExisting(SessionName + ".instance", out Mutex? existing))
            {
                existing.Dispose();
                Log("companion=already_running");
                return 0;
            }

            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing process path.");
            return LaunchChild(CompanionStartInfo(executable, typeof(Companion).Assembly.Location), IsReady, Log);
        }
        finally
        {
            launchGate.ReleaseMutex();
        }
    }

    internal static ProcessStartInfo CompanionStartInfo(string executable, string assembly)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = AppContext.BaseDirectory
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("--companion");
        return start;
    }

    internal static int LaunchChild(ProcessStartInfo command, Func<bool> isReady, Action<string> log)
    {
        try
        {
            using Process? process = Process.Start(command);
            var elapsed = Stopwatch.StartNew();
            do
            {
                if (process is { HasExited: false } && isReady())
                {
                    log($"companion=started pid={process.Id}");
                    return 0;
                }
            } while (process is not null && !process.WaitForExit(50) && elapsed.Elapsed < TimeSpan.FromSeconds(2));
            log(process is { HasExited: true }
                ? $"companion=start_failed reason=exit_before_ready exit_code={process.ExitCode}"
                : "companion=start_failed reason=ready_timeout");
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            log($"companion=start_failed error_type={error.GetType().Name}");
        }
        return 1;
    }

    private static bool IsReady()
    {
        if (!EventWaitHandle.TryOpenExisting(SessionName + ".stop", out EventWaitHandle? stop))
            return false;
        using (stop)
            return !stop.WaitOne(0);
    }

    private static string ReadHookSource()
    {
        try
        {
            if (!Console.IsInputRedirected)
                return "manual";
            Task<string> read = Task.Run(static () => Console.In.ReadToEnd());
            if (!read.Wait(TimeSpan.FromMilliseconds(350)))
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
