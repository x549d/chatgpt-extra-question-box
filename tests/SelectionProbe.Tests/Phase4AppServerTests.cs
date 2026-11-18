using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContextNotes.SelectionProbe;
using Xunit;

namespace SelectionProbe.Tests;

internal sealed class DuplexPipeTestHarness : IDisposable
{
    private readonly AnonymousPipeServerStream serverOut;
    private readonly AnonymousPipeClientStream clientIn;
    private readonly AnonymousPipeServerStream clientOut;
    private readonly AnonymousPipeClientStream serverIn;

    public DuplexPipeTestHarness()
    {
        serverOut = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
        clientIn = new AnonymousPipeClientStream(PipeDirection.In, serverOut.GetClientHandleAsString());
        clientOut = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
        serverIn = new AnonymousPipeClientStream(PipeDirection.In, clientOut.GetClientHandleAsString());

        Transport = new StreamAppServerTransport(clientIn, clientOut, processId: 9999);
        ServerReader = new StreamReader(serverIn, new UTF8Encoding(false), false, 1024, leaveOpen: true);
        ServerWriter = new StreamWriter(serverOut, new UTF8Encoding(false), 1024, leaveOpen: true)
        {
            NewLine = "\n",
            AutoFlush = true
        };
    }

    public StreamAppServerTransport Transport { get; }
    public StreamReader ServerReader { get; }
    public StreamWriter ServerWriter { get; }
    public Stream RawServerOutput => serverOut;

    public void CloseServer()
    {
        try { ServerWriter.Dispose(); } catch { }
        try { serverOut.Dispose(); } catch { }
    }

    public void Dispose()
    {
        Transport.Dispose();
        CloseServer();
        try { ServerReader.Dispose(); } catch { }
        try { serverIn.Dispose(); } catch { }
        try { clientIn.Dispose(); } catch { }
        try { clientOut.Dispose(); } catch { }
    }
}

public sealed class Phase4AppServerTests
{
    [Fact]
    public async Task Initialize_SendsClientInfoAndInitializedNotification()
    {
        using var harness = new DuplexPipeTestHarness();
        using var client = new CodexAppServerClient(harness.Transport);

        Task serverTask = Task.Run(async () =>
        {
            string? initReq = await harness.ServerReader.ReadLineAsync();
            Assert.NotNull(initReq);
            using var doc = JsonDocument.Parse(initReq);
            Assert.Equal("initialize", doc.RootElement.GetProperty("method").GetString());
            long id = doc.RootElement.GetProperty("id").GetInt64();
            Assert.Equal("context-notes-native",
                doc.RootElement.GetProperty("params").GetProperty("clientInfo").GetProperty("name").GetString());

            await harness.ServerWriter.WriteLineAsync(
                $$$"""{"id":{{{id}}},"result":{"codexHome":"C:\\Users\\test\\.codex","platformFamily":"windows","platformOs":"windows","userAgent":"test"}}""");

            string? initializedNotif = await harness.ServerReader.ReadLineAsync();
            Assert.NotNull(initializedNotif);
            using var notifDoc = JsonDocument.Parse(initializedNotif);
            Assert.Equal("initialized", notifDoc.RootElement.GetProperty("method").GetString());
        });

        await client.InitializeAsync(TimeSpan.FromSeconds(5));
        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(client.IsInitialized);
    }

    [Fact]
    public async Task SegmentedStdout_ReassemblesChunkedAndBatchedJsonLines()
    {
        using var harness = new DuplexPipeTestHarness();
        using var client = new CodexAppServerClient(harness.Transport);
        var notifications = new ConcurrentQueue<(string Method, string Delta)>();
        client.NotificationReceived += (method, parameters) =>
        {
            if (method == "item/agentMessage/delta" && parameters.TryGetProperty("delta", out JsonElement d))
                notifications.Enqueue((method, d.GetString() ?? ""));
        };

        Task serverTask = Task.Run(async () =>
        {
            string? initLine = await harness.ServerReader.ReadLineAsync();
            long initId = JsonDocument.Parse(initLine!).RootElement.GetProperty("id").GetInt64();

            // Write the initialize response split across 4 separate partial byte writes without newlines until the end
            string initReply = $$$"""{"id":{{{initId}}},"result":{"codexHome":"C:\\Users\\test\\.codex","platformFamily":"windows","platformOs":"windows","userAgent":"test"}}""" + "\n";
            byte[] bytes = Encoding.UTF8.GetBytes(initReply);
            int chunk = bytes.Length / 4;
            await harness.RawServerOutput.WriteAsync(bytes.AsMemory(0, chunk));
            await harness.RawServerOutput.FlushAsync();
            await Task.Delay(15);
            await harness.RawServerOutput.WriteAsync(bytes.AsMemory(chunk, chunk));
            await harness.RawServerOutput.FlushAsync();
            await Task.Delay(15);
            await harness.RawServerOutput.WriteAsync(bytes.AsMemory(chunk * 2));
            await harness.RawServerOutput.FlushAsync();

            _ = await harness.ServerReader.ReadLineAsync(); // initialized notification

            string? reqLine = await harness.ServerReader.ReadLineAsync();
            long reqId = JsonDocument.Parse(reqLine!).RootElement.GetProperty("id").GetInt64();

            // Write two notifications and one response batched in a single write
            string batched =
                """{"method":"item/agentMessage/delta","params":{"threadId":"t1","turnId":"u1","itemId":"i1","delta":"第一段"}}""" + "\n" +
                """{"method":"item/agentMessage/delta","params":{"threadId":"t1","turnId":"u1","itemId":"i1","delta":"第二段"}}""" + "\n" +
                $$$"""{"id":{{{reqId}}},"result":{"data":[]}}""" + "\n";
            byte[] batchedBytes = Encoding.UTF8.GetBytes(batched);
            await harness.RawServerOutput.WriteAsync(batchedBytes);
            await harness.RawServerOutput.FlushAsync();
        });

        IReadOnlyList<AppServerModelInfo> models = await client.ListModelsAsync(TimeSpan.FromSeconds(5));
        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(models);
        Assert.Equal(2, notifications.Count);
        Assert.Equal(["第一段", "第二段"], notifications.Select(n => n.Delta).ToArray());
    }

    [Fact]
    public async Task ConcurrentRequests_AreIsolatedByIdEvenWhenResponsesArriveOutOfOrder()
    {
        using var harness = new DuplexPipeTestHarness();
        using var client = new CodexAppServerClient(harness.Transport);

        Task serverTask = Task.Run(async () =>
        {
            string? initLine = await harness.ServerReader.ReadLineAsync();
            long initId = JsonDocument.Parse(initLine!).RootElement.GetProperty("id").GetInt64();
            await harness.ServerWriter.WriteLineAsync(
                $$$"""{"id":{{{initId}}},"result":{"codexHome":"C:\\test","platformFamily":"windows","platformOs":"windows","userAgent":"test"}}""");
            _ = await harness.ServerReader.ReadLineAsync(); // initialized

            string? first = await harness.ServerReader.ReadLineAsync();
            string? second = await harness.ServerReader.ReadLineAsync();
            long id1 = JsonDocument.Parse(first!).RootElement.GetProperty("id").GetInt64();
            long id2 = JsonDocument.Parse(second!).RootElement.GetProperty("id").GetInt64();

            // Reply to id2 BEFORE id1
            await harness.ServerWriter.WriteLineAsync($$$"""{"id":{{{id2}}},"result":{"tag":"second_reply"}}""");
            await harness.ServerWriter.WriteLineAsync($$$"""{"id":{{{id1}}},"result":{"tag":"first_reply"}}""");
        });

        await client.InitializeAsync(TimeSpan.FromSeconds(5));
        Task<JsonElement> req1 = client.SendRequestAsync("custom/one", new JsonObject(), TimeSpan.FromSeconds(5));
        Task<JsonElement> req2 = client.SendRequestAsync("custom/two", new JsonObject(), TimeSpan.FromSeconds(5));

        await Task.WhenAll(req1, req2, serverTask);
        Assert.Equal("first_reply", (await req1).GetProperty("tag").GetString());
        Assert.Equal("second_reply", (await req2).GetProperty("tag").GetString());
    }

    [Fact]
    public void AccountAndModelParsing_ProducesRedactedFingerprintAndDefaultModel()
    {
        using var acctDoc1 = JsonDocument.Parse("""
            {"requiresOpenaiAuth":true,"userId":"user-123","account":{"type":"chatgpt","email":"alice@example.com","planType":"plus","workspaceName":null}}
            """);
        using var acctDoc2 = JsonDocument.Parse("""
            {"requiresOpenaiAuth":true,"userId":"user-456","account":{"type":"chatgpt","email":"bob@example.com","planType":"pro","workspaceName":null}}
            """);
        using var loggedOutDoc = JsonDocument.Parse("""
            {"requiresOpenaiAuth":true,"userId":null,"account":null}
            """);

        AppServerAccountState state1 = CodexAppServerClient.ParseAccountResponse(acctDoc1.RootElement);
        AppServerAccountState state2 = CodexAppServerClient.ParseAccountResponse(acctDoc2.RootElement);
        AppServerAccountState outState = CodexAppServerClient.ParseAccountResponse(loggedOutDoc.RootElement);

        Assert.Equal(AppServerLoginStatus.LoggedIn, state1.Status);
        Assert.Equal("chatgpt", state1.AccountType);
        Assert.Equal("plus", state1.PlanType);
        Assert.StartsWith("acct-", state1.ScopeFingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain("alice", state1.ScopeFingerprint, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alice", state1.DisplaySummary, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(state1.ScopeFingerprint, state2.ScopeFingerprint);
        Assert.Equal(AppServerLoginStatus.LoggedOut, outState.Status);

        using var modelsDoc = JsonDocument.Parse("""
            {"data":[
                {"id":"m1","model":"gpt-6.1-sol","displayName":"GPT-6.1 Sol","description":"Base","isDefault":false,"hidden":false,"defaultReasoningEffort":"medium","supportedReasoningEfforts":[]},
                {"id":"m2","model":"gpt-6.2-sol","displayName":"GPT-6.2 Sol","description":"Default","isDefault":true,"hidden":false,"defaultReasoningEffort":"medium","supportedReasoningEfforts":[]}
            ]}
            """);
        IReadOnlyList<AppServerModelInfo> models = CodexAppServerClient.ParseModelListResponse(modelsDoc.RootElement);
        Assert.Equal(2, models.Count);
        Assert.Equal("gpt-6.2-sol", models.First(m => m.IsDefault).Model);
    }

    [Fact]
    public async Task DisconnectAndTimeout_FailPendingRequestsCleanly()
    {
        using var harness = new DuplexPipeTestHarness();
        using var client = new CodexAppServerClient(harness.Transport);

        Task serverTask = Task.Run(async () =>
        {
            string? initLine = await harness.ServerReader.ReadLineAsync();
            long initId = JsonDocument.Parse(initLine!).RootElement.GetProperty("id").GetInt64();
            await harness.ServerWriter.WriteLineAsync(
                $$$"""{"id":{{{initId}}},"result":{"codexHome":"C:\\test","platformFamily":"windows","platformOs":"windows","userAgent":"test"}}""");
            _ = await harness.ServerReader.ReadLineAsync();

            // Read first request and never reply so it times out
            _ = await harness.ServerReader.ReadLineAsync();

            // Read second request and close the server stream to simulate process exit
            _ = await harness.ServerReader.ReadLineAsync();
            harness.CloseServer();
        });

        await client.InitializeAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<TimeoutException>(() =>
            client.SendRequestAsync("slow/method", new JsonObject(), TimeSpan.FromMilliseconds(120)));

        await Assert.ThrowsAnyAsync<IOException>(() =>
            client.SendRequestAsync("disconnect/method", new JsonObject(), TimeSpan.FromSeconds(5)));

        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
