using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace ContextNotes.SelectionProbe;

internal static class Program
{
    [MTAThread]
    private static int Main(string[] args)
    {
        try
        {
            NativeWindows.UsePhysicalPixels();
            if (args.Length == 3 && args[0] is "--uia-worker" or "--test-worker"
                && long.TryParse(args[1], CultureInfo.InvariantCulture, out long handle)
                && uint.TryParse(args[2], CultureInfo.InvariantCulture, out uint pid))
            {
                return ProbeWorker.Run(new WindowIdentity((nint)handle, pid), args[0] == "--test-worker");
            }
            if (args is ["--self-test"])
                return SelfTestAsync().GetAwaiter().GetResult();

            int seconds = 120;
            bool fixtures = false;
            bool exerciseTimeout = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--seconds" when i + 1 < args.Length
                        && int.TryParse(args[i + 1], out seconds) && seconds is >= 1 and <= 600:
                        i++;
                        break;
                    case "--fixtures":
                        fixtures = true;
                        break;
                    case "--exercise-timeout":
                        exerciseTimeout = true;
                        break;
                    default:
                        return Usage();
                }
            }
            if (exerciseTimeout && !fixtures)
                return Usage();

            using var stopping = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, key) =>
            {
                key.Cancel = true;
                stopping.Cancel();
            };
            Console.CancelKeyPress += cancel;
            try
            {
                new ProbeMonitor(fixtures, exerciseTimeout)
                    .RunAsync(TimeSpan.FromSeconds(seconds), stopping.Token).GetAwaiter().GetResult();
            }
            finally
            {
                Console.CancelKeyPress -= cancel;
            }
            return 0;
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Probe stopped with {error.GetType().Name}, HRESULT 0x{error.HResult:X8}.");
            return 1;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("SelectionProbe [--seconds 1..600] [--fixtures [--exercise-timeout]] | --self-test");
        return 2;
    }

    private static async Task<int> SelfTestAsync()
    {
        const string prose = "P1 中文，标点。 English, test!";
        const string code = "P1 = \"中  文, English!\"\n  P2 = \"空格; test?\"";
        Require(Fixtures.Match(prose) is { Exact: true }, "ordinal Chinese, English and punctuation");
        Require(Fixtures.Match(code) is { Exact: true }, "ordinal spaces, indentation and LF");
        Require(Fixtures.Match(code.Replace("\n", "\r\n")) is { Exact: false }, "CRLF preserved as a difference");
        Require(Fixtures.Match(code + "\n") is { Exact: false }, "terminal LF preserved as a difference");
        Require(Fixtures.Match(code.Replace("中  文", "中 文")) is null, "consecutive spaces are significant");
        Require(Fixtures.Match(prose.Replace('，', ',')) is null, "full-width punctuation is significant");
        Require(Fixtures.Match("arbitrary selection") is null, "arbitrary text is excluded from evidence");

        var rectangle = new ScreenRectangle(-650, -40, 329, 24);
        Require(rectangle.IsValid && rectangle.Contains(-500, -30), "negative physical coordinates");
        Require(!rectangle.Intersects(new ScreenRectangle(0, 0, 1920, 1080)), "separate monitors");
        Require(rectangle.Intersects(new ScreenRectangle(-1920, -100, 1920, 1080)), "visible on a left monitor");
        Require(!new ScreenRectangle(0, 0, double.NaN, 20).IsValid, "invalid rectangle");

        const string programFiles = @"C:\Program Files";
        const string protectedImage = @"C:\Program Files\WindowsApps\OpenAI.Codex_26.1111.2228.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe";
        Require(NativeWindows.IsProtectedCodexImage(protectedImage, programFiles), "protected WindowsApps identity fallback");
        Require(!NativeWindows.IsProtectedCodexImage(protectedImage.Replace("Program Files", "Temp"), programFiles),
            "lookalike image outside WindowsApps is rejected");
        Require(!NativeWindows.IsProtectedCodexImage(protectedImage.Replace("2p2nqsd0c76g0", "other"), programFiles),
            "lookalike publisher is rejected");

        var elapsed = Stopwatch.StartNew();
        Task<WorkerReply> pending;
        int oldPid;
        using (var stalled = new ProbeWorker(default, test: true))
        {
            oldPid = stalled.Id;
            await stalled.RequestAsync(Operation.Ping).WaitAsync(ProbeMonitor.RequestTimeout);
            elapsed.Restart();
            pending = stalled.RequestAsync(Operation.Stall);
            try
            {
                await pending.WaitAsync(ProbeMonitor.RequestTimeout);
                throw new InvalidOperationException("Stalled worker returned unexpectedly.");
            }
            catch (TimeoutException)
            {
                Console.WriteLine($"PASS isolated worker deadline at {elapsed.ElapsedMilliseconds} ms");
            }
        }
        try
        {
            await pending.WaitAsync(ProbeMonitor.RequestTimeout);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException)
        {
            Console.WriteLine("PASS stalled worker pipe closed");
        }

        using var fresh = new ProbeWorker(default, test: true);
        await fresh.RequestAsync(Operation.Ping).WaitAsync(ProbeMonitor.RequestTimeout);
        Require(fresh.Id != oldPid, "fresh worker responds after timeout");
        return 0;
    }

    private static void Require(bool succeeded, string description)
    {
        if (!succeeded)
            throw new InvalidOperationException(description);
        Console.WriteLine($"PASS {description}");
    }
}
