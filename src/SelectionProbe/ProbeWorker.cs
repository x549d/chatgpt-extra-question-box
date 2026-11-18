using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace ContextNotes.SelectionProbe;

internal sealed record WorkerReply(SelectionSample? Sample = null, bool Subscribed = false);

internal enum Operation { Sample, Subscribe, Stall, Ping }

internal sealed class ProbeWorker : IDisposable
{
    private readonly Process process;
    private readonly CancellationTokenSource lifetime = new();

    public ProbeWorker(WindowIdentity window, bool test = false)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing process executable.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (string.Equals(Path.GetFileName(executable), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add(test ? "--test-worker" : "--uia-worker");
        start.ArgumentList.Add(window.Handle.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add(window.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        process = Process.Start(start) ?? throw new InvalidOperationException("Worker did not start.");
        process.BeginErrorReadLine();
    }

    public int Id => process.Id;

    public async Task<WorkerReply> RequestAsync(Operation operation)
    {
        CancellationToken cancellation = lifetime.Token;
        await process.StandardInput.WriteLineAsync(operation.ToString().AsMemory(), cancellation);
        await process.StandardInput.FlushAsync(cancellation);
        string? line = await process.StandardOutput.ReadLineAsync(cancellation);
        return line is null
            ? throw new EndOfStreamException("Selection worker exited.")
            : JsonSerializer.Deserialize<WorkerReply>(line) ?? throw new InvalidDataException("Empty worker response.");
    }

    public void Dispose()
    {
        lifetime.Cancel();
        try
        {
            if (!process.HasExited)
                process.Kill();
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            // The worker exited between HasExited and Kill.
        }
        finally
        {
            process.Dispose();
            lifetime.Dispose();
        }
    }

    public static int Run(WindowIdentity window, bool test)
    {
        if (!test && (!NativeWindows.IsCodex(window) || NativeWindows.Foreground() != window))
            return 1;

        using UiaSelection? selection = test ? null : new UiaSelection(window);
        while (Console.ReadLine() is { } command)
        {
            if (!Enum.TryParse(command, out Operation operation))
                return 2;

            WorkerReply reply;
            switch (operation)
            {
                case Operation.Sample when selection is not null:
                    reply = new WorkerReply(selection.Read());
                    break;
                case Operation.Subscribe when selection is not null:
                    reply = new WorkerReply(Subscribed: selection.Subscribe());
                    break;
                case Operation.Ping when test:
                    reply = new WorkerReply();
                    break;
                case Operation.Stall:
                    // Acceptance fault injection. The normal watchdog owns this process.
                    Thread.Sleep(Timeout.Infinite);
                    return 3;
                default:
                    return 2;
            }

            Console.WriteLine(JsonSerializer.Serialize(reply));
        }
        return 0;
    }
}
