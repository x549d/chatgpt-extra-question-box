using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ContextNotes.SelectionProbe;

// Uses the Windows 10/11 inbox SQLite engine; no database server or additional native package.
internal sealed class HistoryDatabase : IDisposable
{
    private nint db;
    private readonly object gate = new();
    public const int SchemaVersion = 2;
    public static string DefaultPath => UserStoragePaths.HistoryPath;
    public string FilePath { get; }

    public HistoryDatabase(string path)
    {
        FilePath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        try
        {
            Check(Native.sqlite3_open_v2(FilePath, out db, 6 | 0x10000, 0));
            Native.sqlite3_busy_timeout(db, 1200);
            Execute("PRAGMA foreign_keys=ON");
            int version = int.Parse(Query("PRAGMA user_version")[0][0]!);
            if (version > SchemaVersion) throw new InvalidDataException("History database is newer than this application; upgrade required.");
            if (Query("PRAGMA quick_check")[0][0] != "ok") throw new InvalidDataException("History integrity check failed. Original files have been preserved.");
            Execute("PRAGMA journal_mode=WAL");
            Execute("PRAGMA synchronous=FULL");
            if (version == SchemaVersion) return;
            if (version is > 0 and < SchemaVersion) Backup(FilePath + ".pre-v" + SchemaVersion + ".db");
            Transaction(() =>
            {
                if (version == 0)
                {
                    Execute("CREATE TABLE annotations(id TEXT PRIMARY KEY, account_scope TEXT NOT NULL, thread_id TEXT, selected_text TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, payload TEXT NOT NULL)");
                    Execute("CREATE TABLE messages(id TEXT PRIMARY KEY, annotation_id TEXT NOT NULL REFERENCES annotations(id) ON DELETE CASCADE, role TEXT NOT NULL, content TEXT NOT NULL, created_at TEXT NOT NULL, turn_id TEXT, completion_status INTEGER NOT NULL, committed INTEGER NOT NULL, sequence INTEGER NOT NULL, UNIQUE(annotation_id,sequence))");
                    Execute("CREATE TABLE settings(key TEXT PRIMARY KEY, value TEXT NOT NULL)");
                }
                if (version < 2)
                {
                    Execute("ALTER TABLE annotations ADD COLUMN title TEXT NOT NULL DEFAULT ''");
                    Execute("ALTER TABLE annotations ADD COLUMN archived INTEGER NOT NULL DEFAULT 0");
                    Execute("ALTER TABLE annotations ADD COLUMN deleted INTEGER NOT NULL DEFAULT 0");
                    Execute("CREATE INDEX annotations_scope_date ON annotations(account_scope,created_at)");
                    Execute("CREATE INDEX messages_annotation ON messages(annotation_id,sequence)");
                }
                Execute("PRAGMA user_version=2");
            });
        }
        catch { Dispose(); throw; }
    }

    public void Save(HistoryRecord record)
    {
        if (record.Messages.Select(m => m.MessageId).Distinct(StringComparer.Ordinal).Count() != record.Messages.Length)
            throw new IOException("Duplicate message IDs in history snapshot.");
        lock (gate) Transaction(() =>
        {
            // Account ownership can be assigned once; it must never migrate between identities.
            var existing = Query("SELECT account_scope FROM annotations WHERE id=?", record.Id);
            if (existing.Count != 0 && existing[0][0] != "" && existing[0][0] != record.AccountScope)
                throw new InvalidOperationException("Annotation account ownership cannot change.");
            Execute("INSERT INTO annotations(id,account_scope,thread_id,selected_text,created_at,updated_at,payload,title,archived,deleted) VALUES(?,?,?,?,?,?,?,?,?,?) ON CONFLICT(id) DO UPDATE SET account_scope=excluded.account_scope,thread_id=excluded.thread_id,updated_at=excluded.updated_at,payload=excluded.payload",
                record.Id, record.AccountScope, record.ThreadId, record.SelectedText, record.CreatedAt.ToString("O"), record.UpdatedAt.ToString("O"), JsonSerializer.Serialize(record with { Messages = [] }), record.Title, record.Archived ? "1" : "0", record.Deleted ? "1" : "0");
            Execute("DELETE FROM messages WHERE annotation_id=? AND sequence>=?", record.Id, record.Messages.Length.ToString());
            for (int i = 0; i < record.Messages.Length; i++)
            {
                var m = record.Messages[i];
                Execute("INSERT INTO messages VALUES(?,?,?,?,?,?,?,?,?) ON CONFLICT(id) DO UPDATE SET annotation_id=CASE WHEN messages.annotation_id=excluded.annotation_id THEN messages.annotation_id END,content=excluded.content,turn_id=excluded.turn_id,completion_status=excluded.completion_status,committed=excluded.committed WHERE messages.annotation_id<>excluded.annotation_id OR messages.content<>excluded.content OR messages.turn_id IS NOT excluded.turn_id OR messages.completion_status<>excluded.completion_status OR messages.committed<>excluded.committed", m.MessageId, record.Id, m.Role, m.Text, m.Timestamp.ToString("O"), m.TurnId, ((int)m.State).ToString(), m.IsCommittedHistory ? "1" : "0", i.ToString());
            }
        });
    }

    public IReadOnlyList<HistoryRecord> Search(string accountScope, string search = "", DateTimeOffset? since = null, DateTimeOffset? until = null, int archive = -1, bool deleted = false, int limit = 200, int offset = 0)
    {
        lock (gate)
        {
            var rows = Query("SELECT payload,title,archived,deleted,updated_at FROM annotations a WHERE account_scope=? AND deleted=? AND (?='-1' OR archived=?) AND (?='' OR created_at>=?) AND (?='' OR created_at<?) AND (?='' OR instr(lower(selected_text),lower(?))>0 OR instr(lower(title),lower(?))>0 OR EXISTS(SELECT 1 FROM messages m WHERE m.annotation_id=a.id AND instr(lower(m.content),lower(?))>0)) ORDER BY updated_at DESC LIMIT ? OFFSET ?",
                accountScope, deleted ? "1" : "0", archive.ToString(), archive.ToString(), since?.ToUniversalTime().ToString("O") ?? "", since?.ToUniversalTime().ToString("O"), until?.ToUniversalTime().ToString("O") ?? "", until?.ToUniversalTime().ToString("O"), search, search, search, search, Math.Clamp(limit, 1, 500).ToString(), Math.Max(offset, 0).ToString());
            return rows.Select(row =>
            {
                var r = JsonSerializer.Deserialize<HistoryRecord>(row[0]!)!;
                var messages = Query("SELECT id,role,content,created_at,turn_id,completion_status,committed FROM messages WHERE annotation_id=? ORDER BY sequence", r.Id)
                    .Select(m => new AnnotationMessage { MessageId = m[0]!, Role = m[1]!, Text = m[2]!, Timestamp = DateTimeOffset.Parse(m[3]!), TurnId = m[4], State = (AnnotationGenerationState)int.Parse(m[5]!), IsCommittedHistory = m[6] == "1" }).ToArray();
                return r with { Title = row[1]!, Archived = row[2] == "1", Deleted = row[3] == "1", UpdatedAt = DateTimeOffset.Parse(row[4]!), Messages = messages };
            }).ToArray();
        }
    }

    public void SetFlags(string id, string accountScope, bool archived, bool deleted) => Execute("UPDATE annotations SET archived=?,deleted=?,updated_at=? WHERE id=? AND account_scope=?", archived ? "1" : "0", deleted ? "1" : "0", DateTimeOffset.UtcNow.ToString("O"), id, accountScope);
    public void Rename(string id, string accountScope, string title) => Execute("UPDATE annotations SET title=?,updated_at=? WHERE id=? AND account_scope=?", title.Trim(), DateTimeOffset.UtcNow.ToString("O"), id, accountScope);
    public string[] AccountScopes() { lock (gate) return Query("SELECT DISTINCT account_scope FROM annotations ORDER BY account_scope").Select(r => r[0]!).ToArray(); }
    public void PurgeDeleted(string accountScope, bool confirmed)
    {
        if (!confirmed) throw new InvalidOperationException("Explicit confirmation required.");
        lock (gate) Transaction(() => Execute("DELETE FROM annotations WHERE account_scope=? AND deleted=1", accountScope));
    }
    public string? GetSetting(string key) { lock (gate) return Query("SELECT value FROM settings WHERE key=?", key).FirstOrDefault()?[0]; }
    public void SetSetting(string key, string value) => Execute("INSERT INTO settings VALUES(?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value", key, value);

    public void Backup(string destination)
    {
        lock (gate)
        {
            if (string.Equals(Path.GetFullPath(destination), FilePath, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Backup must use a different path.");
            nint target = 0;
            try
            {
                Check(Native.sqlite3_open_v2(destination, out target, 6, 0));
                nint backup = Native.sqlite3_backup_init(target, "main", db, "main");
                if (backup == 0) throw new IOException("Cannot start SQLite backup.");
                int result;
                try { result = Native.sqlite3_backup_step(backup, -1); }
                finally { Check(Native.sqlite3_backup_finish(backup)); }
                if (result != 101) Check(result);
            }
            finally { if (target != 0) Native.sqlite3_close_v2(target); }
        }
    }

    // This raw operation deliberately bypasses constructor migrations. SQLite's backup
    // transaction also supports rollback into an existing DB without copying stale WAL files.
    public static int BackupFile(string source, string destination)
    {
        source = Path.GetFullPath(source); destination = Path.GetFullPath(destination);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Backup must use a different path.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        nint input = 0, output = 0;
        try
        {
            Check(Native.sqlite3_open_v2(source, out input, 1 | 0x10000, 0));
            Native.sqlite3_busy_timeout(input, 1200);
            if (ReadScalar(input, "PRAGMA quick_check") != "ok") throw new InvalidDataException("Backup source integrity check failed; original files preserved.");
            int version = int.Parse(ReadScalar(input, "PRAGMA user_version"));
            Check(Native.sqlite3_open_v2(destination, out output, 6 | 0x10000, 0));
            Native.sqlite3_busy_timeout(output, 1200);
            nint backup = Native.sqlite3_backup_init(output, "main", input, "main");
            if (backup == 0) throw new IOException("Cannot start SQLite backup.");
            int result = 0;
            try
            {
                long deadline = Environment.TickCount64 + 5000;
                do
                {
                    result = Native.sqlite3_backup_step(backup, -1);
                    if (result is not (5 or 6)) break;
                    Thread.Sleep(50);
                } while (Environment.TickCount64 < deadline);
            }
            finally { Check(Native.sqlite3_backup_finish(backup)); }
            if (result != 101) Check(result);
            if (ReadScalar(output, "PRAGMA quick_check") != "ok" || int.Parse(ReadScalar(output, "PRAGMA user_version")) != version)
                throw new InvalidDataException("Backup verification failed; source preserved.");
            return version;
        }
        finally
        {
            if (output != 0) Native.sqlite3_close_v2(output);
            if (input != 0) Native.sqlite3_close_v2(input);
        }
    }
    private static string ReadScalar(nint connection, string sql)
    {
        Check(Native.sqlite3_prepare_v2(connection, sql, -1, out var statement, 0));
        try
        {
            int code = Native.sqlite3_step(statement); CheckDone(code);
            return Marshal.PtrToStringUTF8(Native.sqlite3_column_text(statement, 0)) ?? "";
        }
        finally { Native.sqlite3_finalize(statement); }
    }

    internal void Transaction(Action action)
    {
        lock (gate)
        {
            Execute("BEGIN IMMEDIATE");
            try { action(); Execute("COMMIT"); }
            catch { Execute("ROLLBACK"); throw; }
        }
    }
    internal void Execute(string sql, params string?[] values) { lock (gate) { using var s = Prepare(sql, values); CheckDone(Native.sqlite3_step(s.Handle)); } }
    internal List<string?[]> Query(string sql, params string?[] values)
    {
        lock (gate)
        {
            using var s = Prepare(sql, values);
            var result = new List<string?[]>();
            int code;
            while ((code = Native.sqlite3_step(s.Handle)) == 100)
            {
                var row = new string?[Native.sqlite3_column_count(s.Handle)];
                for (int i = 0; i < row.Length; i++) row[i] = Marshal.PtrToStringUTF8(Native.sqlite3_column_text(s.Handle, i));
                result.Add(row);
            }
            CheckDone(code);
            return result;
        }
    }
    private Statement Prepare(string sql, string?[] values)
    {
        if (db == 0) throw new ObjectDisposedException(nameof(HistoryDatabase));
        Check(Native.sqlite3_prepare_v2(db, sql, -1, out nint handle, 0));
        var statement = new Statement(handle);
        try
        {
            for (int i = 0; i < values.Length; i++)
                Check(values[i] is null ? Native.sqlite3_bind_null(handle, i + 1) : Native.sqlite3_bind_text(handle, i + 1, values[i]!, -1, new nint(-1)));
            return statement;
        }
        catch { statement.Dispose(); throw; }
    }
    private static void Check(int code) { if (code != 0) throw new IOException($"SQLite operation failed (code {code}). History files preserved."); }
    private static void CheckDone(int code) { if (code != 101 && code != 100) Check(code); }
    public void Dispose() { lock (gate) { if (db != 0) { Native.sqlite3_close_v2(db); db = 0; } } }
    private sealed record Statement(nint Handle) : IDisposable { public void Dispose() => Native.sqlite3_finalize(Handle); }
    private static class Native
    {
        private const string Library = "winsqlite3.dll";
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out nint db, int flags, nint vfs);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_close_v2(nint db);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_busy_timeout(nint db, int milliseconds);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_prepare_v2(nint db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int length, out nint statement, nint tail);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_bind_text(nint statement, int index, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int length, nint destructor);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_bind_null(nint statement, int index);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_step(nint statement);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_finalize(nint statement);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_column_count(nint statement);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint sqlite3_column_text(nint statement, int index);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint sqlite3_backup_init(nint destination, [MarshalAs(UnmanagedType.LPUTF8Str)] string destinationName, nint source, [MarshalAs(UnmanagedType.LPUTF8Str)] string sourceName);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_backup_step(nint backup, int pages);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_backup_finish(nint backup);
    }
}

internal sealed record HistoryRecord(
    string Id, string AccountScope, string? ThreadId, string SelectedText, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    string Title, string Draft, string? SubmittedDraft, string? Model, bool Anchored, ScreenRectangle Position,
    bool Expanded, bool QuoteExpanded, AnnotationGenerationState State, bool Archived, bool Deleted, AnnotationMessage[] Messages)
{
    public static HistoryRecord Capture(AnnotationItem item) => new(item.AnnotationId, item.AccountScopeId ?? "", item.ThreadId ?? item.SuspendedThreadId,
        item.SelectedText, item.CreatedAt, DateTimeOffset.UtcNow, item.SelectedText.ReplaceLineEndings(" ")[..Math.Min(60, item.SelectedText.ReplaceLineEndings(" ").Length)],
        item.InputDraft, item.SubmittedDraft, item.ModelName, item.ThreadAnchoredWithQuote, item.WindowPosition,
        item.ExpandedState, item.QuoteExpandedState, item.GenerationState, false, false, [.. item.SnapshotMessages()]);

    public AnnotationItem Restore(WindowIdentity owner, ScreenRectangle ownerBounds)
    {
        var anchor = new ScreenRectangle(ownerBounds.X + 20, ownerBounds.Y + 100, 100, 20);
        var item = new AnnotationItem(Id, new SelectionSnapshot(owner, ownerBounds, ownerBounds, anchor, [anchor], SelectedText, CreatedAt))
        {
            AccountScopeId = AccountScope == "" ? null : AccountScope, ThreadId = ThreadId, ThreadClientGeneration = -1,
            RequiresThreadResume = ThreadId is not null, ThreadIsDurable = true, ThreadAnchoredWithQuote = Anchored,
            InputDraft = Draft, SubmittedDraft = SubmittedDraft, ModelName = Model, WindowPosition = Position,
            ExpandedState = Expanded, QuoteExpandedState = QuoteExpanded, IsManuallyMoved = true,
            GenerationState = State is AnnotationGenerationState.Connecting or AnnotationGenerationState.Generating ? AnnotationGenerationState.Failed : State,
            StatusNotice = State is AnnotationGenerationState.Connecting or AnnotationGenerationState.Generating ? "上次生成因退出中断，保留部分内容；不会自动重发。" : "已恢复本地历史；发送前将校验账号与原 Thread。"
        };
        foreach (var message in Messages)
        {
            if (message.State is AnnotationGenerationState.Connecting or AnnotationGenerationState.Generating)
            { message.State = AnnotationGenerationState.Failed; message.IsCommittedHistory = false; }
            item.Messages.Add(message);
        }
        return item;
    }
}
