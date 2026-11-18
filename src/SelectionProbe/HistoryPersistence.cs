namespace ContextNotes.SelectionProbe;

// Capture immutable snapshots at the caller; debounce disk writes, never keep live WPF objects in the writer.
internal sealed class HistoryPersistence : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, HistoryRecord> pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> suppressed = new(StringComparer.Ordinal);
    private readonly Timer timer;
    private bool disposed;
    public HistoryDatabase Database { get; }
    public string? LastFailure { get; private set; }
    public event Action<string?>? StatusChanged;
    private void SetFailure(string? failure)
    {
        if (LastFailure == failure) return;
        LastFailure = failure; StatusChanged?.Invoke(failure);
    }
    public HistoryPersistence(HistoryDatabase database)
    {
        Database = database;
        timer = new Timer(_ => Flush(), null, 750, 750);
    }
    public void Save(AnnotationItem item, bool immediate = false)
    {
        lock (gate)
        {
            if (disposed || suppressed.Contains(item.AnnotationId)) return;
            pending[item.AnnotationId] = HistoryRecord.Capture(item);
            if (immediate) Flush();
            item.PersistenceFailure = LastFailure;
        }
    }
    public void Suspend(string id) { lock (gate) { pending.Remove(id); suppressed.Add(id); } }
    public void Resume(string id) { lock (gate) suppressed.Remove(id); }
    public bool Flush()
    {
        lock (gate)
        {
            if (disposed) return false;
            try
            {
                foreach (var r in pending.Values) Database.Save(r);
                pending.Clear();
                SetFailure(null);
                return true;
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
            {
                // Keep pending snapshots for retry. Neither exception text nor conversation is logged.
                SetFailure("历史暂未保存，请保持程序运行并检查磁盘空间及数据目录权限。");
                return false;
            }
        }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            timer.Dispose();
            if (Flush())
            {
                try { Database.Backup(Database.FilePath + ".backup.db"); }
                catch (System.IO.IOException) { LastFailure = "历史备份失败，原数据库已保留。"; }
            }
            disposed = true;
            Database.Dispose();
        }
    }
}
