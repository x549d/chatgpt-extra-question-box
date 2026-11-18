using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ContextNotes.SelectionProbe;

/// <summary>
/// Coordinates AI question-answering for inline annotations through the local Codex App Server.
/// Keeps UI windows decoupled from JSON-RPC transport details, enforces per-annotation Thread isolation
/// and account-scope isolation, coalesces streaming deltas, and handles turn cancellation and recovery.
/// </summary>
internal sealed class AnnotationService : IDisposable
{
    private readonly Func<IAppServerTransport> transportFactory;
    private readonly Action<string>? diagnosticLogger;
    private readonly TimeSpan deltaCoalesceInterval;
    private readonly TimeSpan turnTimeout;
    private readonly SemaphoreSlim clientLock = new(1, 1);
    private readonly ConcurrentDictionary<string, ActiveTurnSession> activeByAnnotation = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ActiveTurnSession> activeByThread = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> cancelledTurnIds = new(StringComparer.Ordinal);

    private CodexAppServerClient? client;
    private long clientGeneration;
    private string? defaultModel;
    private bool modelsQueried;
    private int disposed;

    public AnnotationService(
        Func<IAppServerTransport>? transportFactory = null,
        Action<string>? diagnosticLogger = null,
        TimeSpan? deltaCoalesceInterval = null,
        TimeSpan? turnTimeout = null)
    {
        this.transportFactory = transportFactory ?? (() => new StdioAppServerTransport());
        this.diagnosticLogger = diagnosticLogger;
        this.deltaCoalesceInterval = deltaCoalesceInterval ?? TimeSpan.FromMilliseconds(30);
        this.turnTimeout = turnTimeout ?? TimeSpan.FromSeconds(180);
    }

    public int? AppServerProcessId => client?.ProcessId;
    public bool IsAppServerConnected => client?.IsConnected == true;

    /// <summary>
    /// Submits a question for the given annotation, streams incremental deltas via <paramref name="onUiUpdate"/>,
    /// and completes when the turn finishes, is cancelled, or fails.
    /// </summary>
    public async Task<AnnotationGenerationState> AskAsync(
        AnnotationItem item,
        string question,
        Action? onUiUpdate = null,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.IsClosed || Volatile.Read(ref disposed) != 0)
            return AnnotationGenerationState.Cancelled;

        string trimmedQuestion = question.Trim();
        if (string.IsNullOrWhiteSpace(trimmedQuestion))
        {
            item.LastError = "请输入你的问题后再发送。";
            item.StatusNotice = item.LastError;
            NotifyUi(item, onUiUpdate);
            return item.GenerationState;
        }

        // Prevent concurrent submissions on the same annotation.
        if (item.IsBusy || activeByAnnotation.ContainsKey(item.AnnotationId))
            return item.GenerationState;

        var session = new ActiveTurnSession(item, onUiUpdate, deltaCoalesceInterval);
        if (!activeByAnnotation.TryAdd(item.AnnotationId, session))
            return item.GenerationState;

        IReadOnlyList<AnnotationMessage> priorCommitted = item.SnapshotMessages()
            .Where(m => m.IsCommittedHistory)
            .ToArray();

        item.SubmittedDraft = trimmedQuestion;
        item.LastError = null;
        item.StatusNotice = "正在连接 Codex App Server...";
        item.StreamingResponseText = "";
        item.GenerationState = AnnotationGenerationState.Connecting;
        _ = item.AddUserMessage(trimmedQuestion);
        AnnotationMessage assistantMsg = item.BeginAssistantMessage();
        session.AssistantMessage = assistantMsg;
        NotifyUi(item, session.UiCallback);

        try
        {
            CodexAppServerClient activeClient = await EnsureClientAsync(cancellation).ConfigureAwait(false);
            long gen = Interlocked.Read(ref clientGeneration);

            // 1. Check current account login state and enforce account switching isolation.
            AppServerAccountState account = await activeClient.ReadAccountAsync(refreshToken: false, cancellation: cancellation)
                .ConfigureAwait(false);
            if (account.Status == AppServerLoginStatus.ConnectionFailed)
            {
                return FailTurn(session, "无法连接 Codex App Server，请确认 Codex 客户端运行正常。", "connection_failed");
            }
            if (account.Status != AppServerLoginStatus.LoggedIn)
            {
                return FailTurn(session, "当前 Codex 未登录或登录已失效，请在 Codex 客户端检查登录状态。", "not_logged_in");
            }

            if (item.AccountScopeId is null)
            {
                item.AccountScopeId = account.ScopeFingerprint;
                item.AccountSummary = account.DisplaySummary;
            }
            else if (!string.Equals(item.AccountScopeId, account.ScopeFingerprint, StringComparison.Ordinal))
            {
                item.ThreadId = null;
                item.ThreadAnchoredWithQuote = false;
                Log($"event=annotation_account_switched annotation_id={item.AnnotationId} old_scope={item.AccountScopeId} new_scope={account.ScopeFingerprint}");
                return FailTurn(
                    session,
                    "检测到当前 Codex 登录账号已切换，为保护账号上下文隔离，旧账号旁注已停止继续追问。请重新划词创建新旁注。",
                    "account_switched");
            }

            // 2. Query model catalog once per client connection (non-fatal if model/list is unsupported).
            if (!modelsQueried)
            {
                modelsQueried = true;
                try
                {
                    IReadOnlyList<AppServerModelInfo> models = await activeClient.ListModelsAsync(cancellation: cancellation)
                        .ConfigureAwait(false);
                    defaultModel = models.FirstOrDefault(m => m.IsDefault && !m.Hidden)?.Model
                        ?? models.FirstOrDefault(m => !m.Hidden)?.Model;
                }
                catch (Exception error)
                {
                    Log($"event=annotation_model_list_skipped error_type={error.GetType().Name}");
                }
            }

            // 3. Ensure this annotation has its own dedicated App Server Thread on the current connection.
            bool needsNewThread = string.IsNullOrWhiteSpace(item.ThreadId) || item.ThreadClientGeneration != gen;
            if (needsNewThread)
            {
                AppServerThreadInfo threadInfo = await activeClient.StartThreadAsync(
                    model: item.ModelName ?? defaultModel,
                    cancellation: cancellation).ConfigureAwait(false);
                item.ThreadId = threadInfo.ThreadId;
                item.ModelName = string.IsNullOrWhiteSpace(threadInfo.Model) ? (item.ModelName ?? defaultModel) : threadInfo.Model;
                item.ThreadClientGeneration = gen;
                item.ThreadAnchoredWithQuote = false;
                Log($"event=annotation_thread_bound annotation_id={item.AnnotationId} model={item.ModelName ?? "default"}");
            }

            string threadId = item.ThreadId!;
            session.ThreadId = threadId;
            activeByThread[threadId] = session;

            if (session.IsCancelled || item.IsClosed)
            {
                return CompleteAsCancelled(session, serverConfirmed: true);
            }

            // 4. Build prompt: include verbatim SelectedText (+ prior committed history if rehydrating) when thread is not yet anchored.
            bool includeQuoteAndHistory = !item.ThreadAnchoredWithQuote;
            string promptText = item.BuildTurnPrompt(trimmedQuestion, includeQuoteAndHistory, priorCommitted);

            item.GenerationState = AnnotationGenerationState.Generating;
            item.StatusNotice = "AI 正在思考...";
            NotifyUi(item, session.UiCallback);

            AppServerTurnInfo turnInfo = await activeClient.StartTurnAsync(
                threadId,
                promptText,
                model: item.ModelName,
                cancellation: cancellation).ConfigureAwait(false);

            session.BindTurnId(turnInfo.TurnId);
            assistantMsg.TurnId = turnInfo.TurnId;
            item.CurrentTurnId = turnInfo.TurnId;
            item.StatusNotice = "AI 正在生成...";
            NotifyUi(item, session.UiCallback);

            if (session.IsCancelled || item.IsClosed)
            {
                TurnInterruptResult interrupt = await activeClient.InterruptTurnAsync(threadId, turnInfo.TurnId, cancellation: CancellationToken.None)
                    .ConfigureAwait(false);
                return CompleteAsCancelled(session, interrupt.ServerConfirmed);
            }

            // 5. Wait for streaming turn completion, user cancellation, or timeout.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellation, session.CancelToken);
            timeoutCts.CancelAfter(turnTimeout);
            try
            {
                return await session.Completion.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (session.IsCancelled || item.IsClosed)
            {
                TurnInterruptResult interrupt = await activeClient.InterruptTurnAsync(threadId, turnInfo.TurnId, cancellation: CancellationToken.None)
                    .ConfigureAwait(false);
                return CompleteAsCancelled(session, interrupt.ServerConfirmed);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                _ = await activeClient.InterruptTurnAsync(threadId, turnInfo.TurnId, cancellation: CancellationToken.None)
                    .ConfigureAwait(false);
                return FailTurn(session, "AI 响应超时，已保留当前已生成内容，请重试。", "turn_timeout");
            }
        }
        catch (OperationCanceledException)
        {
            return CompleteAsCancelled(session, serverConfirmed: false);
        }
        catch (Exception error)
        {
            Log($"event=annotation_ask_failed annotation_id={item.AnnotationId} error_type={error.GetType().Name}");
            return FailTurn(session, FriendlyErrorMessage(error), error.GetType().Name);
        }
        finally
        {
            if (session.ThreadId is { } tid)
                activeByThread.TryRemove(tid, out _);
            activeByAnnotation.TryRemove(item.AnnotationId, out _);
            session.Dispose();
        }
    }

    public async Task<TurnInterruptResult> CancelTurnAsync(AnnotationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!activeByAnnotation.TryGetValue(item.AnnotationId, out ActiveTurnSession? session))
            return new TurnInterruptResult(false, "No active turn.");

        session.MarkCancelled();
        string? threadId = session.ThreadId ?? item.ThreadId;
        string? turnId = session.TurnId ?? item.CurrentTurnId;
        if (!string.IsNullOrWhiteSpace(turnId))
            cancelledTurnIds[turnId] = 1;

        TurnInterruptResult result = new(false);
        if (client is { IsConnected: true } activeClient
            && !string.IsNullOrWhiteSpace(threadId)
            && !string.IsNullOrWhiteSpace(turnId))
        {
            result = await activeClient.InterruptTurnAsync(threadId, turnId).ConfigureAwait(false);
        }

        CompleteAsCancelled(session, result.ServerConfirmed);
        return result;
    }

    public void NotifyAnnotationClosed(AnnotationItem item)
    {
        if (item is null)
            return;
        item.IsClosed = true;
        if (activeByAnnotation.TryGetValue(item.AnnotationId, out ActiveTurnSession? session))
        {
            // Detach UI callback immediately so no background notification touches the closed window.
            session.DetachUi();
            _ = Task.Run(() => CancelTurnAsync(item));
        }
    }

    private async Task<CodexAppServerClient> EnsureClientAsync(CancellationToken cancellation)
    {
        if (client is { IsConnected: true } existing)
        {
            await existing.InitializeAsync(cancellation: cancellation).ConfigureAwait(false);
            return existing;
        }

        await clientLock.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (client is { IsConnected: true } ready)
            {
                await ready.InitializeAsync(cancellation: cancellation).ConfigureAwait(false);
                return ready;
            }

            DisposeClientUnlocked();
            IAppServerTransport transport = transportFactory();
            var created = new CodexAppServerClient(transport, diagnosticLogger);
            created.NotificationReceived += OnClientNotification;
            created.Disconnected += OnClientDisconnected;
            await created.InitializeAsync(cancellation: cancellation).ConfigureAwait(false);

            client = created;
            modelsQueried = false;
            Interlocked.Increment(ref clientGeneration);
            return created;
        }
        finally
        {
            clientLock.Release();
        }
    }

    private void OnClientNotification(string method, JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
            return;

        string? threadId = parameters.TryGetProperty("threadId", out JsonElement thProp) && thProp.ValueKind == JsonValueKind.String
            ? thProp.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(threadId) || !activeByThread.TryGetValue(threadId, out ActiveTurnSession? session))
            return;

        string? turnId = parameters.TryGetProperty("turnId", out JsonElement tuProp) && tuProp.ValueKind == JsonValueKind.String
            ? tuProp.GetString()
            : parameters.TryGetProperty("turn", out JsonElement turnObj)
                && turnObj.ValueKind == JsonValueKind.Object
                && turnObj.TryGetProperty("id", out JsonElement tidProp)
                ? tidProp.GetString()
                : null;

        if (!string.IsNullOrWhiteSpace(turnId))
        {
            if (cancelledTurnIds.ContainsKey(turnId))
                return;
            if (!session.MatchesOrBindTurnId(turnId))
                return;
        }

        if (session.IsCancelled || session.Item.IsClosed)
            return;

        switch (method)
        {
            case "item/agentMessage/delta":
                if (parameters.TryGetProperty("delta", out JsonElement deltaProp) && deltaProp.ValueKind == JsonValueKind.String)
                {
                    string delta = deltaProp.GetString() ?? "";
                    if (delta.Length > 0)
                        session.AppendDelta(delta);
                }
                break;

            case "item/completed":
                if (parameters.TryGetProperty("item", out JsonElement itemObj)
                    && itemObj.ValueKind == JsonValueKind.Object
                    && itemObj.TryGetProperty("type", out JsonElement itemType)
                    && itemType.GetString() == "agentMessage"
                    && itemObj.TryGetProperty("text", out JsonElement fullTextProp)
                    && fullTextProp.ValueKind == JsonValueKind.String)
                {
                    string fullText = fullTextProp.GetString() ?? "";
                    session.EnsureFullText(fullText);
                }
                break;

            case "error":
                bool willRetry = parameters.TryGetProperty("willRetry", out JsonElement retryProp)
                    && retryProp.ValueKind == JsonValueKind.True;
                string errMsg = parameters.TryGetProperty("error", out JsonElement errObj)
                    && errObj.ValueKind == JsonValueKind.Object
                    && errObj.TryGetProperty("message", out JsonElement msgProp)
                    ? msgProp.GetString() ?? "AI 生成出错"
                    : "AI 生成出错";
                if (!willRetry)
                    session.RecordError(errMsg);
                break;

            case "turn/completed":
                HandleTurnCompleted(session, parameters);
                break;
        }
    }

    private void HandleTurnCompleted(ActiveTurnSession session, JsonElement parameters)
    {
        string status = "completed";
        string? errorMsg = null;
        if (parameters.TryGetProperty("turn", out JsonElement turnObj) && turnObj.ValueKind == JsonValueKind.Object)
        {
            if (turnObj.TryGetProperty("status", out JsonElement stProp) && stProp.ValueKind == JsonValueKind.String)
                status = stProp.GetString() ?? "completed";
            if (turnObj.TryGetProperty("error", out JsonElement errObj)
                && errObj.ValueKind == JsonValueKind.Object
                && errObj.TryGetProperty("message", out JsonElement msgProp))
            {
                errorMsg = msgProp.GetString();
            }
        }

        if (status == "interrupted" || session.IsCancelled)
        {
            CompleteAsCancelled(session, serverConfirmed: true);
            return;
        }

        if (status == "failed")
        {
            FailTurn(session, errorMsg ?? session.LastRecordedError ?? "AI 请求未成功完成，请重试。", "turn_failed");
            return;
        }

        string finalText = session.FlushBuffer();
        AnnotationItem item = session.Item;
        if (session.AssistantMessage is { } msg)
            item.UpdateAssistantMessage(msg, finalText, AnnotationGenerationState.Completed, committed: true);

        item.StreamingResponseText = finalText;
        item.ThreadAnchoredWithQuote = true;
        item.CurrentTurnId = null;
        item.LastError = null;
        item.StatusNotice = null;
        item.GenerationState = AnnotationGenerationState.Completed;
        NotifyUi(item, session.UiCallback);

        Log($"event=annotation_turn_completed annotation_id={item.AnnotationId} response_length={finalText.Length}");
        session.Completion.TrySetResult(AnnotationGenerationState.Completed);
    }

    private AnnotationGenerationState CompleteAsCancelled(ActiveTurnSession session, bool serverConfirmed)
    {
        string partial = session.FlushBuffer();
        AnnotationItem item = session.Item;
        if (session.TurnId is { } tid)
            cancelledTurnIds[tid] = 1;

        if (session.AssistantMessage is { } msg)
        {
            string display = string.IsNullOrEmpty(partial) ? "（已取消生成）" : partial;
            item.UpdateAssistantMessage(msg, display, AnnotationGenerationState.Cancelled, committed: false);
        }

        item.StreamingResponseText = partial;
        item.CurrentTurnId = null;
        item.LastError = null;
        item.StatusNotice = serverConfirmed
            ? "已停止生成（服务端已确认中断）"
            : "已停止显示生成内容";
        item.GenerationState = AnnotationGenerationState.Cancelled;
        NotifyUi(item, session.UiCallback);

        Log($"event=annotation_turn_cancelled annotation_id={item.AnnotationId} server_confirmed={serverConfirmed} partial_length={partial.Length}");
        session.Completion.TrySetResult(AnnotationGenerationState.Cancelled);
        return AnnotationGenerationState.Cancelled;
    }

    private AnnotationGenerationState FailTurn(ActiveTurnSession session, string userMessage, string reasonCode)
    {
        string partial = session.FlushBuffer();
        AnnotationItem item = session.Item;
        if (session.AssistantMessage is { } msg)
        {
            string display = string.IsNullOrEmpty(partial) ? $"（未完成：{userMessage}）" : partial;
            item.UpdateAssistantMessage(msg, display, AnnotationGenerationState.Failed, committed: false);
        }

        item.StreamingResponseText = partial;
        item.CurrentTurnId = null;
        item.LastError = userMessage;
        item.StatusNotice = userMessage;
        item.GenerationState = AnnotationGenerationState.Failed;
        NotifyUi(item, session.UiCallback);

        Log($"event=annotation_turn_failed annotation_id={item.AnnotationId} reason={reasonCode} partial_length={partial.Length}");
        session.Completion.TrySetResult(AnnotationGenerationState.Failed);
        return AnnotationGenerationState.Failed;
    }

    private void OnClientDisconnected(string reason)
    {
        Log($"event=annotation_service_client_disconnected reason={reason}");
        foreach (KeyValuePair<string, ActiveTurnSession> pair in activeByAnnotation)
        {
            ActiveTurnSession session = pair.Value;
            if (!session.Completion.Task.IsCompleted)
            {
                FailTurn(session, "Codex App Server 连接中断，已保留当前已生成内容，请重试。", "app_server_disconnected");
            }
        }
    }

    private static string FriendlyErrorMessage(Exception error) => error switch
    {
        FileNotFoundException => "未找到本机 Codex App Server 可执行文件（codex.exe）。",
        TimeoutException => "请求 Codex App Server 超时，请稍后重试。",
        AppServerRpcException rpc when rpc.Message.Contains("auth", StringComparison.OrdinalIgnoreCase)
            || rpc.Message.Contains("login", StringComparison.OrdinalIgnoreCase)
            || rpc.Message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
            => "当前 Codex 登录状态不可用，请在 Codex 客户端检查账号状态。",
        AppServerRpcException rpc => $"AI 服务返回错误：{rpc.Message}",
        IOException => "与 Codex App Server 的通信连接已断开，请重试。",
        _ => "发送问题时出现异常，请稍后重试。"
    };

    private static void NotifyUi(AnnotationItem item, Action? callback)
    {
        if (item.IsClosed || callback is null)
            return;
        try
        {
            callback();
        }
        catch (Exception)
        {
        }
    }

    private void DisposeClientUnlocked()
    {
        if (client is null)
            return;
        client.NotificationReceived -= OnClientNotification;
        client.Disconnected -= OnClientDisconnected;
        client.Dispose();
        client = null;
    }

    private void Log(string message) => diagnosticLogger?.Invoke(message);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        foreach (KeyValuePair<string, ActiveTurnSession> pair in activeByAnnotation)
        {
            pair.Value.DetachUi();
            pair.Value.MarkCancelled();
        }
        activeByAnnotation.Clear();
        activeByThread.Clear();

        clientLock.Wait();
        try
        {
            DisposeClientUnlocked();
        }
        finally
        {
            clientLock.Release();
            clientLock.Dispose();
        }
    }

    private sealed class ActiveTurnSession : IDisposable
    {
        private readonly object bufferLock = new();
        private readonly StringBuilder buffer = new();
        private readonly CancellationTokenSource cancelCts = new();
        private readonly TimeSpan coalesceWindow;
        private Timer? coalesceTimer;
        private Action? uiCallback;
        private string? turnId;
        private bool firstDeltaFlushed;
        private bool flushScheduled;
        private int cancelled;

        public ActiveTurnSession(AnnotationItem item, Action? uiCallback, TimeSpan coalesceWindow)
        {
            Item = item;
            this.uiCallback = uiCallback;
            this.coalesceWindow = coalesceWindow;
        }

        public AnnotationItem Item { get; }
        public AnnotationMessage? AssistantMessage { get; set; }
        public string? ThreadId { get; set; }
        public string? TurnId => Volatile.Read(ref turnId);
        public string? LastRecordedError { get; private set; }
        public bool IsCancelled => Volatile.Read(ref cancelled) != 0;
        public CancellationToken CancelToken => cancelCts.Token;
        public Action? UiCallback => Volatile.Read(ref uiCallback);
        public TaskCompletionSource<AnnotationGenerationState> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void BindTurnId(string id)
        {
            lock (bufferLock)
                turnId ??= id;
        }

        public bool MatchesOrBindTurnId(string incomingTurnId)
        {
            lock (bufferLock)
            {
                if (turnId is null)
                {
                    turnId = incomingTurnId;
                    return true;
                }
                return string.Equals(turnId, incomingTurnId, StringComparison.Ordinal);
            }
        }

        public void AppendDelta(string delta)
        {
            if (IsCancelled || Item.IsClosed)
                return;

            bool flushNow = false;
            string snapshot = "";
            lock (bufferLock)
            {
                buffer.Append(delta);
                if (!firstDeltaFlushed || coalesceWindow <= TimeSpan.Zero)
                {
                    firstDeltaFlushed = true;
                    flushNow = true;
                    snapshot = buffer.ToString();
                }
                else if (!flushScheduled)
                {
                    flushScheduled = true;
                    coalesceTimer ??= new Timer(OnCoalesceTimerTick, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    coalesceTimer.Change(coalesceWindow, Timeout.InfiniteTimeSpan);
                }
            }

            if (flushNow)
                ApplyStreamingSnapshot(snapshot);
        }

        public void EnsureFullText(string fullText)
        {
            if (IsCancelled || Item.IsClosed || string.IsNullOrEmpty(fullText))
                return;

            lock (bufferLock)
            {
                if (buffer.Length < fullText.Length)
                {
                    buffer.Clear();
                    buffer.Append(fullText);
                }
            }
        }

        public string FlushBuffer()
        {
            string snapshot;
            lock (bufferLock)
            {
                flushScheduled = false;
                coalesceTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                snapshot = buffer.ToString();
            }
            ApplyStreamingSnapshot(snapshot);
            return snapshot;
        }

        public void RecordError(string message) => LastRecordedError = message;

        public void MarkCancelled()
        {
            if (Interlocked.Exchange(ref cancelled, 1) == 0)
            {
                try { cancelCts.Cancel(); } catch (ObjectDisposedException) { }
            }
        }

        public void DetachUi() => Interlocked.Exchange(ref uiCallback, null);

        private void OnCoalesceTimerTick(object? state)
        {
            if (IsCancelled || Item.IsClosed)
                return;

            string snapshot;
            lock (bufferLock)
            {
                flushScheduled = false;
                snapshot = buffer.ToString();
            }
            ApplyStreamingSnapshot(snapshot);
        }

        private void ApplyStreamingSnapshot(string snapshot)
        {
            if (Item.IsClosed)
                return;
            if (AssistantMessage is { } msg)
                Item.UpdateAssistantMessage(msg, snapshot, AnnotationGenerationState.Generating, committed: false);
            Item.StreamingResponseText = snapshot;
            Item.StatusNotice = "AI 正在生成...";
            NotifyUi(Item, UiCallback);
        }

        public void Dispose()
        {
            DetachUi();
            coalesceTimer?.Dispose();
            cancelCts.Dispose();
        }
    }
}
