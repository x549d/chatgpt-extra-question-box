using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ContextNotes.SelectionProbe;

internal enum AppServerLoginStatus
{
    Unknown,
    LoggedIn,
    LoggedOut,
    ConnectionFailed
}

internal sealed record AppServerAccountState(
    AppServerLoginStatus Status,
    string AccountType,
    string PlanType,
    string ScopeFingerprint,
    bool RequiresOpenaiAuth,
    string DisplaySummary);

internal sealed record AppServerModelInfo(
    string Id,
    string Model,
    string DisplayName,
    string Description,
    bool IsDefault,
    bool Hidden);

internal sealed record AppServerThreadInfo(
    string ThreadId,
    string Model,
    string ModelProvider,
    bool Ephemeral);

internal sealed record AppServerTurnInfo(
    string TurnId,
    string Status);

internal sealed record TurnInterruptResult(
    bool ServerConfirmed,
    string? ErrorMessage = null);

internal sealed class AppServerRpcException(long code, string message)
    : InvalidOperationException($"App Server RPC error {code}: {message}")
{
    public long Code { get; } = code;
}

internal interface IAppServerTransport : IDisposable
{
    bool IsConnected { get; }
    int? ProcessId { get; }
    event Action<string>? Disconnected;
    Task WriteLineAsync(string jsonLine, CancellationToken cancellation);
    Task<string?> ReadLineAsync(CancellationToken cancellation);
}

/// <summary>
/// Stream-backed transport used both by the stdio child process and by segmented-stream unit tests.
/// Handles arbitrary byte/character chunking and splits strictly on newline boundaries.
/// </summary>
internal sealed class StreamAppServerTransport : IAppServerTransport
{
    private readonly StreamReader reader;
    private readonly StreamWriter writer;
    private readonly Action? onDispose;
    private int disposed;

    public StreamAppServerTransport(Stream inputToRead, Stream outputToWrite, int? processId = null, Action? onDispose = null)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        reader = new StreamReader(inputToRead, utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: false);
        writer = new StreamWriter(outputToWrite, utf8, bufferSize: 4096, leaveOpen: false)
        {
            NewLine = "\n",
            AutoFlush = true
        };
        ProcessId = processId;
        this.onDispose = onDispose;
    }

    public bool IsConnected => Volatile.Read(ref disposed) == 0;
    public int? ProcessId { get; }
    public event Action<string>? Disconnected;

    public async Task WriteLineAsync(string jsonLine, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        await writer.WriteLineAsync(jsonLine.AsMemory(), cancellation).ConfigureAwait(false);
        await writer.FlushAsync(cancellation).ConfigureAwait(false);
    }

    public async Task<string?> ReadLineAsync(CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        string? line = await reader.ReadLineAsync(cancellation).ConfigureAwait(false);
        if (line is null)
            Disconnected?.Invoke("eof");
        return line;
    }

    public void NotifyDisconnected(string reason) => Disconnected?.Invoke(reason);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        try { writer.Dispose(); } catch (Exception) { }
        try { reader.Dispose(); } catch (Exception) { }
        onDispose?.Invoke();
    }
}

/// <summary>
/// Spawns `codex.exe app-server` over stdio and binds it to a Windows Job Object with KILL_ON_JOB_CLOSE
/// so no orphan `codex.exe` process can ever survive if the companion exits or crashes.
/// </summary>
internal sealed class StdioAppServerTransport : IAppServerTransport
{
    private readonly Process process;
    private readonly StreamAppServerTransport inner;
    private readonly nint jobHandle;
    private int disposed;

    public StdioAppServerTransport(string? executablePath = null, string? workingDirectory = null)
    {
        string resolvedExe = executablePath ?? ResolveCodexExecutable()
            ?? throw new FileNotFoundException("Could not locate codex.exe for App Server.");
        string resolvedCwd = workingDirectory ?? EnsureEmptyWorkspaceDirectory();

        var start = new ProcessStartInfo(resolvedExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = resolvedCwd,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        start.ArgumentList.Add("app-server");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("allow_managed_hooks_only=true");

        PopulateUserEnvironment(start);

        process = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!process.Start())
            throw new InvalidOperationException("Failed to start codex app-server process.");

        jobHandle = TryAttachKillOnCloseJob(process.Handle);
        inner = new StreamAppServerTransport(
            process.StandardOutput.BaseStream,
            process.StandardInput.BaseStream,
            process.Id);

        process.Exited += (_, _) => inner.NotifyDisconnected("process_exited");
        _ = DrainStandardErrorAsync(process.StandardError);
    }

    public bool IsConnected => Volatile.Read(ref disposed) == 0 && !process.HasExited && inner.IsConnected;
    public int? ProcessId => inner.ProcessId;

    public event Action<string>? Disconnected
    {
        add => inner.Disconnected += value;
        remove => inner.Disconnected -= value;
    }

    public Task WriteLineAsync(string jsonLine, CancellationToken cancellation) =>
        inner.WriteLineAsync(jsonLine, cancellation);

    public Task<string?> ReadLineAsync(CancellationToken cancellation) =>
        inner.ReadLineAsync(cancellation);

    public static string? ResolveCodexExecutable()
    {
        string localAppData = GetRealUserLocalAppData();
        string codexBinRoot = Path.Combine(localAppData, "OpenAI", "Codex", "bin");
        if (Directory.Exists(codexBinRoot))
        {
            string? candidate = Directory.EnumerateFiles(codexBinRoot, "codex.exe", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .Select(info => info.FullName)
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(candidate))
                return candidate;
        }

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string full = Path.Combine(dir.Trim(), "codex.exe");
                    if (File.Exists(full))
                        return full;
                }
                catch (ArgumentException)
                {
                }
            }
        }
        return null;
    }

    public static string EnsureEmptyWorkspaceDirectory()
    {
        string baseDir = Path.Combine(Diagnostics.LogDirectory, "..", "empty-workspace");
        string full = Path.GetFullPath(baseDir);
        Directory.CreateDirectory(full);
        return full;
    }

    private static string GetRealUserProfile()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile) && Directory.Exists(Path.Combine(profile, ".codex")))
            return profile;
        string userName = Environment.UserName;
        if (!string.IsNullOrWhiteSpace(userName))
        {
            string candidate = Path.Combine(@"C:\Users", userName);
            if (Directory.Exists(candidate))
                return candidate;
        }
        return profile;
    }

    private static string GetRealUserLocalAppData()
    {
        string profile = GetRealUserProfile();
        if (!string.IsNullOrWhiteSpace(profile))
        {
            string candidate = Path.Combine(profile, "AppData", "Local");
            if (Directory.Exists(candidate))
                return candidate;
        }
        return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    private static void PopulateUserEnvironment(ProcessStartInfo start)
    {
        string profile = GetRealUserProfile();
        if (string.IsNullOrWhiteSpace(profile))
            return;

        start.Environment["USERPROFILE"] = profile;
        start.Environment["HOME"] = profile;
        string roaming = Path.Combine(profile, "AppData", "Roaming");
        string local = Path.Combine(profile, "AppData", "Local");
        string codexHome = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(profile, ".codex");
        if (Directory.Exists(roaming))
            start.Environment["APPDATA"] = roaming;
        if (Directory.Exists(local))
            start.Environment["LOCALAPPDATA"] = local;
        if (Directory.Exists(codexHome))
            start.Environment["CODEX_HOME"] = codexHome;
    }

    private static async Task DrainStandardErrorAsync(StreamReader stderr)
    {
        try
        {
            while (await stderr.ReadLineAsync().ConfigureAwait(false) is not null)
            {
                // Drain stderr so the child process never blocks on a full stderr pipe buffer.
                // Never log raw stderr lines to avoid leaking paths or diagnostic payloads.
            }
        }
        catch (Exception)
        {
        }
    }

    private static nint TryAttachKillOnCloseJob(nint processHandle)
    {
        nint job = CreateJobObjectW(0, null);
        if (job == 0)
            return 0;

        var info = new JobObjectExtendedLimitInformation();
        info.BasicLimitInformation.LimitFlags = 0x00002000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        int size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, buffer, false);
            if (!SetInformationJobObject(job, 9, buffer, (uint)size)
                || !AssignProcessToJobObject(job, processHandle))
            {
                CloseHandle(job);
                return 0;
            }
            return job;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        inner.Dispose();
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
        }
        finally
        {
            process.Dispose();
            if (jobHandle != 0)
                CloseHandle(jobHandle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateJobObjectW(nint securityAttributes, string? name);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool SetInformationJobObject(nint job, int infoClass, nint info, uint infoLength);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool AssignProcessToJobObject(nint job, nint process);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern bool CloseHandle(nint handle);
}

/// <summary>
/// Asynchronous JSON-RPC 2.0 client for the local Codex App Server.
/// Reuses the user's existing Codex login without reading tokens, cookies, or browser state.
/// </summary>
internal sealed class CodexAppServerClient : IDisposable
{
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(15);
    private const string DefaultDeveloperInstructions =
        "你是 Codex Chat 内置的划词追问助手（Context Notes）。请仅根据用户引用的原文和当前旁注对话历史，直接用清晰、准确的纯文本回答用户的问题。不要调用任何 Shell、文件修改、MCP 或外部工具。";

    private readonly IAppServerTransport transport;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly SemaphoreSlim initLock = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pendingRequests = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task readLoopTask;
    private readonly Action<string>? diagnosticLogger;
    private long nextRequestId;
    private bool initialized;
    private int disposed;

    public CodexAppServerClient(IAppServerTransport transport, Action<string>? diagnosticLogger = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        this.transport = transport;
        this.diagnosticLogger = diagnosticLogger;
        transport.Disconnected += OnTransportDisconnected;
        readLoopTask = Task.Run(ReadLoopAsync);
    }

    public bool IsConnected => Volatile.Read(ref disposed) == 0 && transport.IsConnected;
    public bool IsInitialized => initialized && IsConnected;
    public int? ProcessId => transport.ProcessId;

    public event Action<string, JsonElement>? NotificationReceived;
    public event Action<string>? Disconnected;

    public async Task InitializeAsync(TimeSpan? timeout = null, CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (initialized)
            return;

        await initLock.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (initialized)
                return;

            var sw = Stopwatch.StartNew();
            var parameters = new JsonObject
            {
                ["clientInfo"] = new JsonObject
                {
                    ["name"] = "context-notes-native",
                    ["title"] = "ChatGPT Context Notes Native",
                    ["version"] = "0.4.0"
                }
            };

            JsonElement result = await SendRequestCoreAsync(
                "initialize",
                parameters,
                timeout ?? DefaultRequestTimeout,
                cancellation).ConfigureAwait(false);

            string platform = result.TryGetProperty("platformOs", out JsonElement os) ? os.GetString() ?? "unknown" : "unknown";
            await SendNotificationAsync("initialized", null, cancellation).ConfigureAwait(false);
            initialized = true;
            Log($"event=app_server_initialized pid={ProcessId ?? 0} platform={platform} latency_ms={sw.ElapsedMilliseconds}");
        }
        finally
        {
            initLock.Release();
        }
    }

    public async Task<AppServerAccountState> ReadAccountAsync(
        bool refreshToken = false,
        TimeSpan? timeout = null,
        CancellationToken cancellation = default)
    {
        try
        {
            await InitializeAsync(timeout, cancellation).ConfigureAwait(false);
            var parameters = new JsonObject { ["refreshToken"] = refreshToken };
            JsonElement response = await SendRequestAsync("account/read", parameters, timeout, cancellation).ConfigureAwait(false);
            return ParseAccountResponse(response);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Log($"event=app_server_account_read_failed error_type={error.GetType().Name}");
            return new AppServerAccountState(
                AppServerLoginStatus.ConnectionFailed,
                "none",
                "unknown",
                "unauthenticated",
                RequiresOpenaiAuth: true,
                "无法连接 Codex App Server");
        }
    }

    internal static AppServerAccountState ParseAccountResponse(JsonElement response)
    {
        bool requiresAuth = !response.TryGetProperty("requiresOpenaiAuth", out JsonElement reqProp)
            || reqProp.ValueKind != JsonValueKind.False;
        string? userId = response.TryGetProperty("userId", out JsonElement uidProp) && uidProp.ValueKind == JsonValueKind.String
            ? uidProp.GetString()
            : null;

        if (!response.TryGetProperty("account", out JsonElement account) || account.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            AppServerLoginStatus status = requiresAuth ? AppServerLoginStatus.LoggedOut : AppServerLoginStatus.LoggedIn;
            return new AppServerAccountState(
                status,
                "none",
                "none",
                status == AppServerLoginStatus.LoggedIn ? "acct-anonymous" : "unauthenticated",
                requiresAuth,
                status == AppServerLoginStatus.LoggedIn ? "已就绪（无需账号认证）" : "未登录 Codex 账号");
        }

        string type = account.TryGetProperty("type", out JsonElement typeProp) && typeProp.ValueKind == JsonValueKind.String
            ? typeProp.GetString() ?? "unknown"
            : "unknown";
        string plan = account.TryGetProperty("planType", out JsonElement planProp) && planProp.ValueKind == JsonValueKind.String
            ? planProp.GetString() ?? "unknown"
            : "unknown";
        string? email = account.TryGetProperty("email", out JsonElement emailProp) && emailProp.ValueKind == JsonValueKind.String
            ? emailProp.GetString()
            : null;
        string? workspace = account.TryGetProperty("workspaceName", out JsonElement wsProp) && wsProp.ValueKind == JsonValueKind.String
            ? wsProp.GetString()
            : null;

        string fingerprint = ComputeAccountScopeFingerprint(type, userId, email, workspace);
        string summary = type == "chatgpt" && !string.IsNullOrWhiteSpace(plan) && plan != "unknown"
            ? $"已登录（ChatGPT { char.ToUpperInvariant(plan[0]) + plan[1..] }）"
            : $"已登录（{type}）";

        return new AppServerAccountState(
            AppServerLoginStatus.LoggedIn,
            type,
            plan,
            fingerprint,
            requiresAuth,
            summary);
    }

    /// <summary>
    /// Computes an irreversible SHA-256 scope identifier for the active account so threads and histories
    /// are isolated across account switches without storing or logging emails, user IDs, or tokens.
    /// </summary>
    public static string ComputeAccountScopeFingerprint(string accountType, string? userId, string? email, string? workspaceName)
    {
        string raw = $"{accountType.Trim().ToLowerInvariant()}|{userId?.Trim() ?? ""}|{email?.Trim().ToLowerInvariant() ?? ""}|{workspaceName?.Trim() ?? ""}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return "acct-" + Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
    }

    public async Task<IReadOnlyList<AppServerModelInfo>> ListModelsAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellation = default)
    {
        await InitializeAsync(timeout, cancellation).ConfigureAwait(false);
        var models = new List<AppServerModelInfo>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            JsonElement response = await SendRequestAsync("model/list", new JsonObject { ["cursor"] = cursor, ["limit"] = 100 }, timeout, cancellation).ConfigureAwait(false);
            models.AddRange(ParseModelListResponse(response));
            cursor = response.TryGetProperty("nextCursor", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
            if (cursor is not null && (!cursors.Add(cursor) || cursors.Count > 100)) throw new InvalidDataException("Invalid model catalog pagination.");
        } while (cursor is not null);
        return models.DistinctBy(m => m.Model).ToArray();
    }

    internal static IReadOnlyList<AppServerModelInfo> ParseModelListResponse(JsonElement response)
    {
        if (!response.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<AppServerModelInfo>();
        foreach (JsonElement item in data.EnumerateArray())
        {
            string id = item.TryGetProperty("id", out JsonElement idProp) ? idProp.GetString() ?? "" : "";
            string model = item.TryGetProperty("model", out JsonElement modelProp) ? modelProp.GetString() ?? id : id;
            string displayName = item.TryGetProperty("displayName", out JsonElement dnProp) ? dnProp.GetString() ?? model : model;
            string description = item.TryGetProperty("description", out JsonElement descProp) ? descProp.GetString() ?? "" : "";
            bool isDefault = item.TryGetProperty("isDefault", out JsonElement defProp) && defProp.ValueKind == JsonValueKind.True;
            bool hidden = item.TryGetProperty("hidden", out JsonElement hidProp) && hidProp.ValueKind == JsonValueKind.True;
            if (!string.IsNullOrWhiteSpace(model))
                list.Add(new AppServerModelInfo(id, model, displayName, description, isDefault, hidden));
        }
        return list;
    }

    public async Task<AppServerThreadInfo> StartThreadAsync(
        string? model = null,
        string? workingDirectory = null,
        string? developerInstructions = null,
        TimeSpan? timeout = null,
        CancellationToken cancellation = default)
    {
        await InitializeAsync(timeout, cancellation).ConfigureAwait(false);
        string cwd = workingDirectory ?? StdioAppServerTransport.EnsureEmptyWorkspaceDirectory();
        var parameters = new JsonObject
        {
            ["cwd"] = cwd,
            ["ephemeral"] = false,
            ["approvalPolicy"] = "never",
            ["sandbox"] = "read-only",
            ["developerInstructions"] = developerInstructions ?? DefaultDeveloperInstructions
        };
        if (!string.IsNullOrWhiteSpace(model))
            parameters["model"] = model;

        JsonElement response = await SendRequestAsync("thread/start", parameters, timeout, cancellation).ConfigureAwait(false);
        JsonElement thread = response.GetProperty("thread");
        string threadId = thread.GetProperty("id").GetString()
            ?? throw new InvalidDataException("thread/start returned an empty thread id.");
        string resolvedModel = response.TryGetProperty("model", out JsonElement modelProp) && modelProp.ValueKind == JsonValueKind.String
            ? modelProp.GetString() ?? ""
            : "";
        string provider = response.TryGetProperty("modelProvider", out JsonElement provProp) && provProp.ValueKind == JsonValueKind.String
            ? provProp.GetString() ?? ""
            : "";
        bool ephemeral = !thread.TryGetProperty("ephemeral", out JsonElement ephProp) || ephProp.ValueKind == JsonValueKind.True;

        Log($"event=app_server_thread_started model={resolvedModel} ephemeral={ephemeral}");
        return new AppServerThreadInfo(threadId, resolvedModel, provider, ephemeral);
    }

    public async Task ResumeThreadAsync(string threadId, CancellationToken cancellation = default)
    {
        var response = await SendRequestAsync("thread/resume", new JsonObject
        {
            ["threadId"] = threadId,
            ["excludeTurns"] = true,
            ["cwd"] = StdioAppServerTransport.EnsureEmptyWorkspaceDirectory(),
            ["approvalPolicy"] = "never",
            ["sandbox"] = "read-only",
            ["developerInstructions"] = DefaultDeveloperInstructions
        }, cancellation: cancellation).ConfigureAwait(false);
        if (response.GetProperty("thread").GetProperty("id").GetString() != threadId)
            throw new InvalidDataException("Server resumed a different thread.");
    }

    public async Task<AppServerTurnInfo> StartTurnAsync(
        string threadId,
        string inputText,
        string? model = null,
        TimeSpan? timeout = null,
        CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            throw new ArgumentException("Thread ID is required.", nameof(threadId));
        if (string.IsNullOrWhiteSpace(inputText))
            throw new ArgumentException("Input text is required.", nameof(inputText));

        await InitializeAsync(timeout, cancellation).ConfigureAwait(false);
        var parameters = new JsonObject
        {
            ["threadId"] = threadId,
            ["approvalPolicy"] = "never",
            ["sandboxPolicy"] = new JsonObject
            {
                ["type"] = "readOnly",
                ["networkAccess"] = false
            },
            ["input"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = inputText
                }
            }
        };
        if (!string.IsNullOrWhiteSpace(model))
            parameters["model"] = model;

        JsonElement response = await SendRequestAsync("turn/start", parameters, timeout, cancellation).ConfigureAwait(false);
        JsonElement turn = response.GetProperty("turn");
        string turnId = turn.GetProperty("id").GetString()
            ?? throw new InvalidDataException("turn/start returned an empty turn id.");
        string status = turn.TryGetProperty("status", out JsonElement stProp) && stProp.ValueKind == JsonValueKind.String
            ? stProp.GetString() ?? "inProgress"
            : "inProgress";

        Log($"event=app_server_turn_started status={status} input_length={inputText.Length}");
        return new AppServerTurnInfo(turnId, status);
    }

    public async Task<TurnInterruptResult> InterruptTurnAsync(
        string threadId,
        string turnId,
        TimeSpan? timeout = null,
        CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(threadId) || string.IsNullOrWhiteSpace(turnId))
            return new TurnInterruptResult(false, "Missing threadId or turnId.");

        try
        {
            var parameters = new JsonObject
            {
                ["threadId"] = threadId,
                ["turnId"] = turnId
            };
            _ = await SendRequestAsync("turn/interrupt", parameters, timeout ?? TimeSpan.FromSeconds(5), cancellation)
                .ConfigureAwait(false);
            Log("event=app_server_turn_interrupted server_confirmed=true");
            return new TurnInterruptResult(true);
        }
        catch (Exception error)
        {
            Log($"event=app_server_turn_interrupted server_confirmed=false error_type={error.GetType().Name}");
            return new TurnInterruptResult(false, error.Message);
        }
    }

    public async Task<JsonElement> SendRequestAsync(
        string method,
        JsonNode? parameters,
        TimeSpan? timeout = null,
        CancellationToken cancellation = default)
    {
        await InitializeAsync(timeout, cancellation).ConfigureAwait(false);
        return await SendRequestCoreAsync(method, parameters, timeout ?? DefaultRequestTimeout, cancellation).ConfigureAwait(false);
    }

    private async Task<JsonElement> SendRequestCoreAsync(
        string method,
        JsonNode? parameters,
        TimeSpan timeout,
        CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!transport.IsConnected)
            throw new IOException("App Server transport is disconnected.");

        long id = Interlocked.Increment(ref nextRequestId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingRequests[id] = tcs;

        var payload = new JsonObject
        {
            ["id"] = id,
            ["method"] = method
        };
        if (parameters is not null)
            payload["params"] = parameters;

        string json = payload.ToJsonString();
        try
        {
            await WriteRawLineAsync(json, cancellation).ConfigureAwait(false);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
            timeoutCts.CancelAfter(timeout);
            return await tcs.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested && !lifetime.IsCancellationRequested)
        {
            throw new TimeoutException($"App Server request '{method}' (id={id}) timed out after {timeout.TotalMilliseconds:0} ms.");
        }
        finally
        {
            pendingRequests.TryRemove(id, out _);
        }
    }

    private async Task SendNotificationAsync(string method, JsonNode? parameters, CancellationToken cancellation)
    {
        var payload = new JsonObject { ["method"] = method };
        if (parameters is not null)
            payload["params"] = parameters;
        await WriteRawLineAsync(payload.ToJsonString(), cancellation).ConfigureAwait(false);
    }

    private async Task WriteRawLineAsync(string jsonLine, CancellationToken cancellation)
    {
        await writeLock.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            await transport.WriteLineAsync(jsonLine, cancellation).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                string? line = await transport.ReadLineAsync(lifetime.Token).ConfigureAwait(false);
                if (line is null)
                    break;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                ProcessIncomingLine(line);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            Log($"event=app_server_read_fault error_type={error.GetType().Name}");
        }
        finally
        {
            FailAllPendingRequests(new IOException("App Server connection closed."));
        }
    }

    private void ProcessIncomingLine(string line)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException error)
        {
            Log($"event=app_server_json_parse_error error_type={error.GetType().Name}");
            return;
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return;

            bool hasId = TryGetRequestId(root, out long id);
            bool hasMethod = root.TryGetProperty("method", out JsonElement methodProp) && methodProp.ValueKind == JsonValueKind.String;

            if (hasId && !hasMethod)
            {
                if (!pendingRequests.TryRemove(id, out TaskCompletionSource<JsonElement>? tcs))
                    return;

                if (root.TryGetProperty("error", out JsonElement errorProp) && errorProp.ValueKind == JsonValueKind.Object)
                {
                    long code = errorProp.TryGetProperty("code", out JsonElement codeProp) && codeProp.TryGetInt64(out long c) ? c : -1;
                    string message = errorProp.TryGetProperty("message", out JsonElement msgProp) ? msgProp.GetString() ?? "RPC error" : "RPC error";
                    tcs.TrySetException(new AppServerRpcException(code, message));
                }
                else if (root.TryGetProperty("result", out JsonElement resultProp))
                {
                    tcs.TrySetResult(resultProp.Clone());
                }
                else
                {
                    tcs.TrySetResult(default);
                }
                return;
            }

            if (hasId && hasMethod)
            {
                // Server-initiated request (e.g. unexpected tool approval). Reject immediately so the turn never hangs.
                _ = RejectServerRequestAsync(id);
                return;
            }

            if (hasMethod)
            {
                string method = methodProp.GetString()!;
                JsonElement parameters = root.TryGetProperty("params", out JsonElement paramsProp)
                    ? paramsProp.Clone()
                    : default;
                try
                {
                    NotificationReceived?.Invoke(method, parameters);
                }
                catch (Exception error)
                {
                    Log($"event=app_server_notification_handler_fault method={method} error_type={error.GetType().Name}");
                }
            }
        }
    }

    private async Task RejectServerRequestAsync(long requestId)
    {
        try
        {
            var errorReply = new JsonObject
            {
                ["id"] = requestId,
                ["error"] = new JsonObject
                {
                    ["code"] = -32601,
                    ["message"] = "Context Notes is read-only and does not execute tool approvals."
                }
            };
            await WriteRawLineAsync(errorReply.ToJsonString(), lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private static bool TryGetRequestId(JsonElement root, out long id)
    {
        id = 0;
        if (!root.TryGetProperty("id", out JsonElement idProp))
            return false;
        if (idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt64(out id))
            return true;
        if (idProp.ValueKind == JsonValueKind.String && long.TryParse(idProp.GetString(), out id))
            return true;
        return false;
    }

    private void OnTransportDisconnected(string reason)
    {
        initialized = false;
        FailAllPendingRequests(new IOException($"App Server disconnected: {reason}"));
        Log($"event=app_server_disconnected reason={reason}");
        Disconnected?.Invoke(reason);
    }

    private void FailAllPendingRequests(Exception error)
    {
        foreach (KeyValuePair<long, TaskCompletionSource<JsonElement>> pair in pendingRequests)
        {
            if (pendingRequests.TryRemove(pair.Key, out TaskCompletionSource<JsonElement>? tcs))
                tcs.TrySetException(error);
        }
    }

    private void Log(string message) => diagnosticLogger?.Invoke(message);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        lifetime.Cancel();
        transport.Disconnected -= OnTransportDisconnected;
        transport.Dispose();
        FailAllPendingRequests(new ObjectDisposedException(nameof(CodexAppServerClient)));
        try { readLoopTask.Wait(TimeSpan.FromMilliseconds(500)); } catch (Exception) { }
        writeLock.Dispose();
        initLock.Dispose();
        lifetime.Dispose();
    }
}
