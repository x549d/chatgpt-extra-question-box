using System.IO;

namespace ContextNotes.SelectionProbe;

/// <summary>Starts the companion when the verified Codex desktop first comes to the foreground.</summary>
internal static class DesktopStartup
{
    private static string StopName => Companion.SessionName + ".watch.stop";
    internal static string RelaunchName => Companion.SessionName + ".watch.relaunch";

    public static int Watch()
    {
        using var instance = new Mutex(false, Companion.SessionName + ".watch.instance", out bool first);
        if (!first)
            return 0;

        using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, StopName);
        using var relaunch = new EventWaitHandle(false, EventResetMode.AutoReset, RelaunchName);
        using var log = new Diagnostics.RotatingLog(Path.Combine(Diagnostics.LogDirectory, "desktop-startup.log"));
        log.WriteLine($"desktop_watcher=running pid={Environment.ProcessId}");
        var gate = new DesktopStartupGate();
        uint foregroundPid = 0;
        bool verified = false;
        do
        {
            if (relaunch.WaitOne(0))
            {
                int relaunchResult = Companion.LaunchFromDesktop();
                log.WriteLine($"desktop_launch source=relaunch_signal launcher_exit={relaunchResult}");
            }

            WindowIdentity foreground = NativeWindows.Foreground();
            if (foreground.ProcessId != foregroundPid)
            {
                foregroundPid = foreground.ProcessId;
                verified = NativeWindows.IsCodex(foreground);
            }
            if (verified && gate.ShouldLaunch(foregroundPid, Environment.TickCount64))
            {
                int result = Companion.LaunchFromDesktop();
                gate.Record(foregroundPid, result == 0, Environment.TickCount64);
                log.WriteLine($"desktop_launch codex_pid={foregroundPid} launcher_exit={result}");
            }
        } while (!stop.WaitOne(TimeSpan.FromSeconds(1)));

        log.WriteLine("desktop_watcher=stopped");
        return 0;
    }

    public static int LaunchWatcher()
    {
        if (Status(stopRequested: false) == 0)
            return 0;
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing process path.");
        var start = new System.Diagnostics.ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            WorkingDirectory = AppContext.BaseDirectory
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(DesktopStartup).Assembly.Location);
        start.ArgumentList.Add("--watch-codex");
        using var process = System.Diagnostics.Process.Start(start);
        return 0;
    }

    public static int Status(bool stopRequested)
    {
        if (!EventWaitHandle.TryOpenExisting(StopName, out EventWaitHandle? stop))
        {
            Console.WriteLine("desktop_watcher_not_running");
            return 3;
        }
        using (stop)
        {
            if (stopRequested)
                stop.Set();
            Console.WriteLine(stop.WaitOne(0) ? "desktop_watcher_stopping" : "desktop_watcher_running");
            return 0;
        }
    }
}

internal sealed class DesktopStartupGate
{
    private uint lastPid;
    private long retryAt;
    private bool started;

    public bool ShouldLaunch(uint pid, long now) => pid != 0 && (pid != lastPid || (!started && now >= retryAt));

    public void Record(uint pid, bool success, long now)
    {
        lastPid = pid;
        started = success;
        retryAt = now + 10_000;
    }
}
