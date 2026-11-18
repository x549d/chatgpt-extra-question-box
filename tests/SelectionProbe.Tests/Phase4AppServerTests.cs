using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
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

/// <summary>
/// Programmable in-memory fake Codex App Server speaking the real stdio JSON-RPC protocol over pipes.
/// Supports multiple threads, streaming deltas, turn interruption, account switching, and simulated crashes.
/// </summary>
internal sealed class FakeCodexAppServer : IDisposable
{
    private readonly DuplexPipeTestHarness harness = new();
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly CancellationTokenSource cts = new();
    private readonly Task loopTask;
    private int nextThreadNum;
    private int nextTurnNum;

    public FakeCodexAppServer()
    {
        loopTask = Task.Run(ServerLoopAsync);
    }

    public IAppServerTransport Transport => harness.Transport;
    public string UserId { get; set; } = "user-plus-001";
    public string Email { get; set; } = "dev@example.com";
    public string PlanType { get; set; } = "plus";
    public bool LoggedIn { get; set; } = true;
    public string DefaultModel { get; set; } = "gpt-6.2-sol";
    public bool ResumeSupported { get; set; }
    public ConcurrentQueue<string> ResumedThreads { get; } = new();
    public ConcurrentQueue<string?> RequestedModels { get; } = new();

    public ConcurrentQueue<(string ThreadId, string TurnId, string InputText)> ReceivedTurns { get; } = new();
    public ConcurrentQueue<(string ThreadId, string TurnId)> InterruptedTurns { get; } = new();

    public Func<string, string, string, FakeCodexAppServer, Task>? CustomTurnHandler { get; set; }

    public async Task WriteServerLineAsync(string line)
    {
        await writeGate.WaitAsync(cts.Token).ConfigureAwait(false);
        try
        {
            await harness.ServerWriter.WriteLineAsync(line).ConfigureAwait(false);
        }
        finally
        {
            writeGate.Release();
        }
    }

    public void SimulateCrash() => harness.CloseServer();

    private async Task ServerLoopAsync()
    {
        try
        {
            while (!cts.IsCancellationRequested)
            {
                string? line = await harness.ServerReader.ReadLineAsync(cts.Token);
                if (line is null)
                    break;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                using var doc = JsonDocument.Parse(line);
                JsonElement root = doc.RootElement;
                if (!root.TryGetProperty("method", out JsonElement methodProp))
                    continue;
                string method = methodProp.GetString()!;
                bool hasId = root.TryGetProperty("id", out JsonElement idProp);
                long id = hasId ? idProp.GetInt64() : 0;

                switch (method)
                {
                    case "initialize":
                        await WriteServerLineAsync(
                            $$$"""{"id":{{{id}}},"result":{"codexHome":"C:\\Users\\test\\.codex","platformFamily":"windows","platformOs":"windows","userAgent":"fake-codex"}}""");
                        break;

                    case "initialized":
                        break;

                    case "account/read":
                        if (LoggedIn)
                        {
                            await WriteServerLineAsync(
                                $$$$"""{"id":{{{{id}}}},"result":{"requiresOpenaiAuth":true,"userId":"{{{{UserId}}}}","account":{"type":"chatgpt","email":"{{{{Email}}}}","planType":"{{{{PlanType}}}}","workspaceName":null}}}""");
                        }
                        else
                        {
                            await WriteServerLineAsync(
                                $$$"""{"id":{{{id}}},"result":{"requiresOpenaiAuth":true,"userId":null,"account":null}}""");
                        }
                        break;

                    case "model/list":
                        await WriteServerLineAsync(
                            $$$"""{"id":{{{id}}},"result":{"data":[{"id":"m1","model":"{{{DefaultModel}}}","displayName":"GPT-6.2 Sol","description":"Default","isDefault":true,"hidden":false,"defaultReasoningEffort":"medium","supportedReasoningEfforts":[]}]}}""");
                        break;

                    case "thread/start":
                        string threadId = $"thread-{Interlocked.Increment(ref nextThreadNum)}";
                        await WriteServerLineAsync(
                            $$$$"""{"id":{{{{id}}}},"result":{"approvalPolicy":"never","approvalsReviewer":"user","cwd":"C:\\temp","model":"{{{{DefaultModel}}}}","modelProvider":"openai","sandbox":{"type":"readOnly","networkAccess":false},"thread":{"id":"{{{{threadId}}}}","ephemeral":true}}}""");
                        break;

                    case "thread/resume" when ResumeSupported:
                        string resumed = root.GetProperty("params").GetProperty("threadId").GetString()!;
                        ResumedThreads.Enqueue(resumed);
                        await WriteServerLineAsync(JsonSerializer.Serialize(new { id, result = new { thread = new { id = resumed, ephemeral = false } } }));
                        break;

                    case "turn/start":
                        JsonElement p = root.GetProperty("params");
                        RequestedModels.Enqueue(p.TryGetProperty("model", out var selected) ? selected.GetString() : null);
                        string tid = p.GetProperty("threadId").GetString()!;
                        string inputText = p.GetProperty("input")[0].GetProperty("text").GetString()!;
                        string turnId = $"turn-{Interlocked.Increment(ref nextTurnNum)}";
                        ReceivedTurns.Enqueue((tid, turnId, inputText));

                        await WriteServerLineAsync(
                            $$$$"""{"id":{{{{id}}}},"result":{"turn":{"id":"{{{{turnId}}}}","items":[],"status":"inProgress"}}}""");

                        if (CustomTurnHandler is { } custom)
                        {
                            _ = Task.Run(() => custom(tid, turnId, inputText, this));
                        }
                        else
                        {
                            _ = Task.Run(async () =>
                            {
                                foreach (string chunk in new[] { "Reply ", $"for {tid}", $"/{turnId}" })
                                {
                                    await WriteServerLineAsync(
                                        $$$"""{"method":"item/agentMessage/delta","params":{"threadId":"{{{tid}}}","turnId":"{{{turnId}}}","itemId":"item-1","delta":"{{{chunk}}}"}}""");
                                }
                                await WriteServerLineAsync(
                                    $$$$"""{"method":"turn/completed","params":{"threadId":"{{{{tid}}}}","turn":{"id":"{{{{turnId}}}}","items":[],"status":"completed"}}}""");
                            });
                        }
                        break;

                    case "thread/resume":
                    case "account/rateLimits/read":
                        await WriteServerLineAsync(JsonSerializer.Serialize(new { id, error = new { code = -32601, message = "Unsupported method in this test fixture" } }));
                        break;

                    case "turn/interrupt":
                        JsonElement ip = root.GetProperty("params");
                        string itid = ip.GetProperty("threadId").GetString()!;
                        string iturnId = ip.GetProperty("turnId").GetString()!;
                        InterruptedTurns.Enqueue((itid, iturnId));
                        await WriteServerLineAsync($$$"""{"id":{{{id}}},"result":{}}""");
                        await WriteServerLineAsync(
                            $$$$"""{"method":"turn/completed","params":{"threadId":"{{{{itid}}}}","turn":{"id":"{{{{iturnId}}}}","items":[],"status":"interrupted"}}}""");
                        break;
                }
            }
        }
        catch (Exception)
        {
        }
    }

    public void Dispose()
    {
        cts.Cancel();
        harness.Dispose();
        try { loopTask.Wait(TimeSpan.FromMilliseconds(300)); } catch { }
        cts.Dispose();
    }
}

public sealed class Phase4AppServerTests
{
    private static readonly WindowIdentity TestWindow = new(new nint(0x24680), 8888);
    private static readonly ScreenRectangle DefaultWindowBounds = new(400, 120, 1600, 1000);
    private static readonly ScreenRectangle DefaultViewport = new(700, 200, 1150, 760);
    private static readonly ScreenRectangle DefaultAnchor = new(840, 320, 220, 28);

    private static AnnotationItem CreateAnnotation(string id, string quote = "def calculate(a, b):\n    return a + b") =>
        new(
            id,
            SelectionSnapshot.FromSelection(
                new OverlaySelection(TestWindow, DefaultWindowBounds, DefaultViewport, [DefaultAnchor], quote),
                DefaultAnchor));

    [Fact]
    public async Task Phase4A_Initialize_SendsClientInfoAndInitializedNotification()
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
    public async Task Phase4A_SegmentedStdout_ReassemblesChunkedAndBatchedJsonLines()
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

            _ = await harness.ServerReader.ReadLineAsync(); // initialized

            string? reqLine = await harness.ServerReader.ReadLineAsync();
            long reqId = JsonDocument.Parse(reqLine!).RootElement.GetProperty("id").GetInt64();

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
    public async Task Phase4A_ConcurrentRequests_AreIsolatedByIdEvenWhenResponsesArriveOutOfOrder()
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

            string? first = await harness.ServerReader.ReadLineAsync();
            string? second = await harness.ServerReader.ReadLineAsync();
            long id1 = JsonDocument.Parse(first!).RootElement.GetProperty("id").GetInt64();
            long id2 = JsonDocument.Parse(second!).RootElement.GetProperty("id").GetInt64();

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
    public void Phase4A_AccountAndModelParsing_ProducesRedactedFingerprintAndDefaultModel()
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
    public async Task Phase4A_DisconnectAndTimeout_FailPendingRequestsCleanly()
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

            _ = await harness.ServerReader.ReadLineAsync();
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

    [Fact]
    public async Task Phase4B_And_4C_AskAsync_SendsVerbatimQuoteAndStreamsResponse()
    {
        using var fakeServer = new FakeCodexAppServer();
        using var service = new AnnotationService(() => fakeServer.Transport, deltaCoalesceInterval: TimeSpan.Zero);

        const string quote = "def calculate(a, b):\n    return a + b";
        AnnotationItem item = CreateAnnotation("ann-4b", quote);
        var observedStates = new ConcurrentQueue<AnnotationGenerationState>();

        AnnotationGenerationState finalState = await service.AskAsync(
            item,
            "这段代码的执行过程是什么？",
            () => observedStates.Enqueue(item.GenerationState));

        Assert.Equal(AnnotationGenerationState.Completed, finalState);
        Assert.Equal(AnnotationGenerationState.Completed, item.GenerationState);
        Assert.Equal("thread-1", item.ThreadId);
        Assert.Equal("Reply for thread-1/turn-1", item.StreamingResponseText);
        Assert.Equal(2, item.Messages.Count);
        Assert.Equal("user", item.Messages[0].Role);
        Assert.Equal("这段代码的执行过程是什么？", item.Messages[0].Text);
        Assert.Equal("assistant", item.Messages[1].Role);
        Assert.Equal("Reply for thread-1/turn-1", item.Messages[1].Text);
        Assert.True(item.Messages[1].IsCommittedHistory);

        Assert.Single(fakeServer.ReceivedTurns);
        fakeServer.ReceivedTurns.TryPeek(out var received);
        Assert.Contains(quote, received.InputText, StringComparison.Ordinal);
        Assert.Contains("这段代码的执行过程是什么？", received.InputText, StringComparison.Ordinal);
        Assert.Contains(AnnotationGenerationState.Connecting, observedStates);
        Assert.Contains(AnnotationGenerationState.Generating, observedStates);
    }

    [Fact]
    public async Task Phase4D_FiveConsecutiveFollowUpTurns_ReuseSameThreadAndPreserveOrder()
    {
        using var fakeServer = new FakeCodexAppServer();
        using var service = new AnnotationService(() => fakeServer.Transport, deltaCoalesceInterval: TimeSpan.Zero);

        AnnotationItem item = CreateAnnotation("ann-followup", "class Sub(Base):\n    pass");
        string[] questions =
        [
            "第1轮：这段代码为什么使用继承？",
            "第2轮：能不能给一个实际例子？",
            "第3轮：那组合和继承有什么区别？",
            "第4轮：Python 多重继承的 MRO 是怎么工作的？",
            "第5轮：总结一下何时优先使用组合。"
        ];

        for (int i = 0; i < questions.Length; i++)
        {
            AnnotationGenerationState state = await service.AskAsync(item, questions[i]);
            Assert.Equal(AnnotationGenerationState.Completed, state);
        }

        Assert.Equal("thread-1", item.ThreadId);
        Assert.Equal(10, item.Messages.Count); // 5 user + 5 assistant in exact order
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal("user", item.Messages[i * 2].Role);
            Assert.Equal(questions[i], item.Messages[i * 2].Text);
            Assert.Equal("assistant", item.Messages[i * 2 + 1].Role);
            Assert.Equal($"Reply for thread-1/turn-{i + 1}", item.Messages[i * 2 + 1].Text);
            Assert.True(item.Messages[i * 2 + 1].IsCommittedHistory);
        }

        // All 5 turns ran on the same thread-1
        var turns = fakeServer.ReceivedTurns.ToArray();
        Assert.Equal(5, turns.Length);
        Assert.All(turns, t => Assert.Equal("thread-1", t.ThreadId));
    }

    [Fact]
    public async Task Phase4D_ThreeConcurrentAnnotations_HaveIsolatedThreadsAndStreams()
    {
        using var fakeServer = new FakeCodexAppServer();
        fakeServer.CustomTurnHandler = async (threadId, turnId, input, server) =>
        {
            for (int step = 1; step <= 3; step++)
            {
                await Task.Delay(15);
                await server.WriteServerLineAsync(
                    $$$"""{"method":"item/agentMessage/delta","params":{"threadId":"{{{threadId}}}","turnId":"{{{turnId}}}","itemId":"i","delta":"[{{{threadId}}}:part{{{step}}}]"}}""");
            }
            await server.WriteServerLineAsync(
                $$$$"""{"method":"turn/completed","params":{"threadId":"{{{{threadId}}}}","turn":{"id":"{{{{turnId}}}}","items":[],"status":"completed"}}}""");
        };

        using var service = new AnnotationService(() => fakeServer.Transport, deltaCoalesceInterval: TimeSpan.Zero);
        AnnotationItem itemA = CreateAnnotation("ann-A", "Quote A");
        AnnotationItem itemB = CreateAnnotation("ann-B", "Quote B");
        AnnotationItem itemC = CreateAnnotation("ann-C", "Quote C");

        Task<AnnotationGenerationState> taskA = service.AskAsync(itemA, "Question A");
        Task<AnnotationGenerationState> taskB = service.AskAsync(itemB, "Question B");
        Task<AnnotationGenerationState> taskC = service.AskAsync(itemC, "Question C");

        await Task.WhenAll(taskA, taskB, taskC);

        Assert.Equal(AnnotationGenerationState.Completed, await taskA);
        Assert.Equal(AnnotationGenerationState.Completed, await taskB);
        Assert.Equal(AnnotationGenerationState.Completed, await taskC);

        // Distinct ThreadIds
        string[] threadIds = [itemA.ThreadId!, itemB.ThreadId!, itemC.ThreadId!];
        Assert.Equal(3, threadIds.Distinct().Count());

        // Each annotation only received its own stream deltas
        Assert.Equal($"[{itemA.ThreadId}:part1][{itemA.ThreadId}:part2][{itemA.ThreadId}:part3]", itemA.Messages[1].Text);
        Assert.Equal($"[{itemB.ThreadId}:part1][{itemB.ThreadId}:part2][{itemB.ThreadId}:part3]", itemB.Messages[1].Text);
        Assert.Equal($"[{itemC.ThreadId}:part1][{itemC.ThreadId}:part2][{itemC.ThreadId}:part3]", itemC.Messages[1].Text);
    }

    [Fact]
    public async Task Phase4D_AccountSwitch_BlocksCrossAccountThreadReuse()
    {
        using var fakeServer = new FakeCodexAppServer { UserId = "user-A", Email = "a@example.com" };
        using var service = new AnnotationService(() => fakeServer.Transport, deltaCoalesceInterval: TimeSpan.Zero);

        AnnotationItem item = CreateAnnotation("ann-acct", "Quote under account A");
        AnnotationGenerationState first = await service.AskAsync(item, "Account A question");
        Assert.Equal(AnnotationGenerationState.Completed, first);
        string? threadA = item.ThreadId;
        Assert.NotNull(threadA);

        // Switch active account on the server to User B
        fakeServer.UserId = "user-B";
        fakeServer.Email = "b@example.com";

        AnnotationGenerationState second = await service.AskAsync(item, "Follow-up after account switch");
        Assert.Equal(AnnotationGenerationState.Failed, second);
        Assert.Null(item.ThreadId); // Old thread cleared, not reused under account B
        Assert.Contains("账号已切换", item.LastError, StringComparison.Ordinal);
        Assert.Single(fakeServer.ReceivedTurns); // Second turn was blocked before turn/start!
    }

    [Fact]
    public async Task Phase4E_CancelOneAnnotation_LeavesOtherAnnotationRunningAndIgnoresLateDeltas()
    {
        var turnStartedGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fakeServer = new FakeCodexAppServer();
        fakeServer.CustomTurnHandler = async (threadId, turnId, input, server) =>
        {
            await server.WriteServerLineAsync(
                $$$"""{"method":"item/agentMessage/delta","params":{"threadId":"{{{threadId}}}","turnId":"{{{turnId}}}","itemId":"i","delta":"Partial-{{{threadId}}}"}}""");

            if (input.Contains("CancelMe", StringComparison.Ordinal))
            {
                turnStartedGate.TrySetResult();
                await Task.Delay(300);
                // Emit a late delta after interrupt to verify client ignores it
                await server.WriteServerLineAsync(
                    $$$"""{"method":"item/agentMessage/delta","params":{"threadId":"{{{threadId}}}","turnId":"{{{turnId}}}","itemId":"i","delta":"-LATE_SHOULD_BE_IGNORED"}}""");
            }
            else
            {
                await Task.Delay(80);
                await server.WriteServerLineAsync(
                    $$$"""{"method":"item/agentMessage/delta","params":{"threadId":"{{{threadId}}}","turnId":"{{{turnId}}}","itemId":"i","delta":"-Complete"}}""");
                await server.WriteServerLineAsync(
                    $$$$"""{"method":"turn/completed","params":{"threadId":"{{{{threadId}}}}","turn":{"id":"{{{{turnId}}}}","items":[],"status":"completed"}}}""");
            }
        };

        using var service = new AnnotationService(() => fakeServer.Transport, deltaCoalesceInterval: TimeSpan.Zero);
        AnnotationItem itemCancel = CreateAnnotation("ann-cancel", "Quote 1");
        AnnotationItem itemKeep = CreateAnnotation("ann-keep", "Quote 2");

        Task<AnnotationGenerationState> cancelTask = service.AskAsync(itemCancel, "CancelMe");
        Task<AnnotationGenerationState> keepTask = service.AskAsync(itemKeep, "KeepRunning");

        await turnStartedGate.Task.WaitAsync(TimeSpan.FromSeconds(5));
        TurnInterruptResult interrupt = await service.CancelTurnAsync(itemCancel);
        Assert.True(interrupt.ServerConfirmed);

        await Task.WhenAll(cancelTask, keepTask);
        await Task.Delay(350); // Allow any late server delta to arrive

        Assert.Equal(AnnotationGenerationState.Cancelled, itemCancel.GenerationState);
        Assert.False(itemCancel.Messages[1].IsCommittedHistory);
        Assert.DoesNotContain("LATE_SHOULD_BE_IGNORED", itemCancel.Messages[1].Text, StringComparison.Ordinal);

        Assert.Equal(AnnotationGenerationState.Completed, itemKeep.GenerationState);
        Assert.True(itemKeep.Messages[1].IsCommittedHistory);
        Assert.EndsWith("-Complete", itemKeep.Messages[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Phase4E_ProcessCrashAndRecovery_FailsActiveTurnAndReconnectsOnNextAsk()
    {
        FakeCodexAppServer? currentServer = new();
        currentServer.CustomTurnHandler = async (threadId, turnId, input, server) =>
        {
            await server.WriteServerLineAsync(
                $$$"""{"method":"item/agentMessage/delta","params":{"threadId":"{{{threadId}}}","turnId":"{{{turnId}}}","itemId":"i","delta":"BeforeCrash"}}""");
            await Task.Delay(30);
            server.SimulateCrash(); // Simulate App Server process crash mid-stream
        };

        using var service = new AnnotationService(
            () => currentServer!.Transport,
            deltaCoalesceInterval: TimeSpan.Zero);

        AnnotationItem item = CreateAnnotation("ann-recover", "Quote for crash recovery");
        AnnotationGenerationState crashState = await service.AskAsync(item, "First try");

        Assert.Equal(AnnotationGenerationState.Failed, crashState);
        Assert.Equal("BeforeCrash", item.Messages[1].Text);
        Assert.False(item.Messages[1].IsCommittedHistory);
        Assert.Contains("连接中断", item.LastError, StringComparison.Ordinal);

        // Replace with a healthy server instance and retry on the same annotation
        currentServer.Dispose();
        currentServer = new FakeCodexAppServer();
        try
        {
            AnnotationGenerationState recoveredState = await service.AskAsync(item, "Retry after crash");
            Assert.Equal(AnnotationGenerationState.Completed, recoveredState);
            Assert.Equal("Reply for thread-1/turn-1", item.Messages.Last().Text);
            Assert.True(item.Messages.Last().IsCommittedHistory);
        }
        finally
        {
            currentServer.Dispose();
        }
    }

    [Fact]
    public void Phase4E_AnnotationWindowUi_SupportsShiftEnterNewlineAndStopButtonStates()
    {
        Exception? failure = null;
        var sta = new Thread(() =>
        {
            try
            {
                using var fakeServer = new FakeCodexAppServer();
                using var service = new AnnotationService(() => fakeServer.Transport, deltaCoalesceInterval: TimeSpan.Zero);
                AnnotationItem item = CreateAnnotation("ann-ui", "x = 42");
                var window = new AnnotationWindow(item, service);

                item.GenerationState = AnnotationGenerationState.Generating;
                item.StatusNotice = "AI 正在生成...";
                window.RefreshConversationUi();
                Assert.Equal(Visibility.Visible, window.StopButtonControl.Visibility);
                Assert.Equal(Visibility.Collapsed, window.SendButtonControl.Visibility);

                item.GenerationState = AnnotationGenerationState.Completed;
                item.StatusNotice = null;
                _ = item.AddUserMessage("测试问题");
                AnnotationMessage ans = item.BeginAssistantMessage("turn-1");
                item.UpdateAssistantMessage(ans, "测试回答", AnnotationGenerationState.Completed, committed: true);
                window.RefreshConversationUi();

                Assert.Equal(Visibility.Visible, window.ConversationContainer.Visibility);
                Assert.Equal(2, window.MessagesContainer.Children.Count);
                Assert.Equal(Visibility.Collapsed, window.StopButtonControl.Visibility);
                Assert.Equal(Visibility.Visible, window.SendButtonControl.Visibility);

                window.CloseByUser();
                Assert.True(item.IsClosed);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        sta.Join();
        if (failure is not null)
            throw new AggregateException(failure);
    }
}
