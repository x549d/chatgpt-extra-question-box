namespace ContextNotes.SelectionProbe;

// Capture immutable snapshots at the caller; debounce disk writes, never keep live WPF objects in the writer.
internal sealed class HistoryPersistence : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, HistoryRecord> pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> suppressed = new(StringComparer.Ordinal);
    private readonly Timer timer;
    public HistoryDatabase Database { get; }
    public string? LastFailure { get; private set; }
    public HistoryPersistence(HistoryDatabase database)
    {
        Database = database;
        timer = new Timer(_ => Flush(), null, 750, 750);
    }
    public void Save(AnnotationItem item, bool immediate = false)
    {
        lock (gate)
        {
            if (suppressed.Contains(item.AnnotationId)) return;
            pending[item.AnnotationId] = HistoryRecord.Capture(item);
            if (immediate) Flush();
        }
    }
    public void Suspend(string id) { lock (gate) { pending.Remove(id); suppressed.Add(id); } }
    public void Resume(string id) { lock (gate) suppressed.Remove(id); }
    public bool Flush()
    {
        lock (gate)
        {
            try
            {
                foreach (var r in pending.Values) Database.Save(r);
                pending.Clear();
                LastFailure = null;
                return true;
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
            {
                // Keep pending snapshots for retry. Neither exception text nor conversation is logged.
                LastFailure = "历史暂未保存，请保持程序运行并检查磁盘空间及数据目录权限。";
                return false;
            }
        }
    }
    public void Dispose()
    {
        timer.Dispose();
        if (Flush())
        {
            try { Database.Backup(Database.FilePath + ".backup.db"); }
            catch (System.IO.IOException) { LastFailure = "历史备份失败，原数据库已保留。"; }
        }
        Database.Dispose();
    }
}
