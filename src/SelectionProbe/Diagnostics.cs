using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace ContextNotes.SelectionProbe;

// IDs and lengths correlate production events without writing the selected text.
internal readonly record struct SelectionTrace(string Mode, long Id, int TextLength, int RectangleCount, string Role, string Reason)
{
    public static SelectionTrace Empty(string reason) => new("unknown", 0, 0, 0, "none", reason);

    public string Format(string eventName, bool? visible = null, Type? error = null) => FormattableString.Invariant(
        $"event={eventName} pid={Environment.ProcessId} mode={Mode} selection_id={Id} text_length={TextLength} rectangle_count={RectangleCount} role={Role} eligibility_reason={Reason} overlay_visible={visible?.ToString().ToLowerInvariant() ?? "unknown"} error_type={error?.Name ?? "none"}");

    public void Write(string eventName, bool? visible = null, Type? error = null) => Console.WriteLine(Format(eventName, visible, error));
}

/// <summary>Metadata-only diagnostics. Callers never pass selected text, tokens or cookies.</summary>
internal static class Diagnostics
{
    public const int NonInteractiveDesktopExitCode = 4;
    public static string LogDirectory => ResolveLogDirectory();

    public static bool RequireInteractiveDesktop(string operation)
    {
        DesktopContext desktop = NativeWindows.CurrentDesktop();
        if (desktop.CanRunOverlay)
            return true;

        string record = SelectionTrace.Empty("non_interactive_desktop").Format("startup_rejected", false) +
            $" operation={operation} interactive_station={desktop.InteractiveStation} default_desktop={desktop.DefaultDesktop} win32_error={desktop.Error}";
        string fallback = Path.Combine(Path.GetTempPath(), "ContextNotesNative", "logs");
        foreach (string directory in new[] { LogDirectory, fallback }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var log = new RotatingLog(Path.Combine(directory, "startup-rejections.log"));
                log.WriteLine(record);
                return false;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A restricted desktop may also lack access to the normal profile log.
            }
        }
        Console.Error.WriteLine(record);
        return false;
    }

    private static string ResolveLogDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            string candidate = Path.Combine(localAppData, "ContextNotesNative", "logs");
            try
            {
                Directory.CreateDirectory(candidate);
                return candidate;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
        return Path.Combine(Path.GetTempPath(), "ContextNotesNative", "logs");
    }

    public static string ProcessSummary()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        using Process current = Process.GetCurrentProcess();
        string rid = identity.User?.Value is { } sid ? sid[(sid.LastIndexOf('-') + 1)..] : "?";
        return $"process pid={current.Id} session={current.SessionId} user_rid={rid} integrity={NativeWindows.IntegrityLevel()} " +
            $"codex_foreground={NativeWindows.IsCodex(NativeWindows.Foreground())} codex_running={NativeWindows.CodexRunning()}";
    }

    /// <summary>Size-capped append-only log with one rotation.</summary>
    internal sealed class RotatingLog : TextWriter
    {
        private const long MaxBytes = 512 * 1024;
        private readonly string path;
        private StreamWriter? writer;

        public RotatingLog(string path)
        {
            this.path = path;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            writer = Open();
        }

        public string FilePath => path;

        public override Encoding Encoding => Encoding.UTF8;

        public override void WriteLine(string? value)
        {
            lock (this)
            {
                try
                {
                    if (writer is null)
                        return;
                    if (writer.BaseStream.Length >= MaxBytes)
                    {
                        writer.Dispose();
                        writer = null;
                        File.Move(path, path + ".previous", overwrite: true);
                        writer = Open();
                    }
                    writer.WriteLine($"{DateTimeOffset.Now:yyyy-MM-ddTHH:mm:ss.fffzzz} {value}");
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    writer?.Dispose();
                    writer = null;
                }
            }
        }

        public override void Write(string? value) => WriteLine(value);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                lock (this)
                    writer?.Dispose();
            base.Dispose(disposing);
        }

        private StreamWriter Open() =>
            new(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete),
                new UTF8Encoding(false)) { AutoFlush = true };
    }
}

/// <summary>Runs the read-only structure inspector against the foreground Codex window.</summary>
internal static class InspectRunner
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(8);

    public static int Run(TimeSpan duration)
    {
        string file = Path.Combine(Diagnostics.LogDirectory, $"inspect-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        using var log = new Diagnostics.RotatingLog(file);
        void Write(string line)
        {
            log.WriteLine(line);
            Console.WriteLine(line);
        }

        Write($"inspect_ready seconds={duration.TotalSeconds} file={Path.GetFileName(file)}");
        Write(Diagnostics.ProcessSummary());
        var clock = Stopwatch.StartNew();
        WindowIdentity owner = default;
        ProbeWorker? worker = null;
        try
        {
            while (clock.Elapsed < duration)
            {
                WindowIdentity foreground = NativeWindows.Foreground();
                if (foreground != owner)
                {
                    worker?.Dispose();
                    worker = null;
                    owner = foreground;
                    bool codex = NativeWindows.IsCodex(foreground);
                    var geometry = NativeWindows.Geometry(foreground, []);
                    Write($"foreground codex={codex} dpi={geometry.Dpi} monitor_count={geometry.MonitorCount} " +
                        $"window={UiaSelection.Coordinates(geometry.Bounds)}");
                    if (codex)
                        worker = new ProbeWorker(foreground);
                }
                if (worker is not null)
                {
                    try
                    {
                        WorkerReply reply = worker.RequestAsync(Operation.Inspect).WaitAsync(Deadline).GetAwaiter().GetResult();
                        foreach (string line in reply.Report ?? [])
                            Write(line);
                    }
                    catch (Exception error) when (error is TimeoutException or IOException or InvalidOperationException
                        or System.Text.Json.JsonException)
                    {
                        Write($"inspect worker_fault={error.GetType().Name}; restarting worker");
                        worker.Dispose();
                        worker = null;
                        owner = default;
                    }
                }
                Thread.Sleep(Interval);
            }
        }
        finally
        {
            worker?.Dispose();
            Write("inspect_stopped");
        }
        return 0;
    }
}
