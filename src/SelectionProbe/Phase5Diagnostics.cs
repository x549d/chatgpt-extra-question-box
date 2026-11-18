using System.IO;

namespace ContextNotes.SelectionProbe;

// Explicit diagnostic commands use synthetic content; output is metadata only.
internal static class Phase5Diagnostics
{
    public static int CrashFixture(string path)
    {
        using var db = new HistoryDatabase(path);
        var item = Synthetic("crash-fixture", "crash-fixture"); item.AddUserMessage("durable synthetic input");
        db.Save(HistoryRecord.Capture(item));
        db.Execute("BEGIN IMMEDIATE"); db.Execute("UPDATE annotations SET selected_text='uncommitted synthetic update'");
        Console.WriteLine("fixture_ready"); Console.Out.Flush();
        Thread.Sleep(Timeout.Infinite); return 1;
    }
    public static async Task<int> LiveAsync(bool resume)
    {
        using var persistence = new HistoryPersistence(new HistoryDatabase(HistoryDatabase.DefaultPath));
        using var service = new AnnotationService(settingsDatabase: persistence.Database);
        var account = await service.ReadCurrentAccountAsync();
        if (account.Status != AppServerLoginStatus.LoggedIn) { Console.WriteLine("FAIL live_account_unavailable"); return 1; }
        var models = await service.GetModelsAsync();
        Console.WriteLine($"PASS live_catalog count={models.Count} account_scope={account.ScopeFingerprint}");
        string usage = await service.ReadUsageAsync();
        Console.WriteLine($"INFO live_usage_available={!usage.Contains("未提供", StringComparison.Ordinal)}");
        var items = resume
            ? persistence.Database.Search(account.ScopeFingerprint, "Phase 5 synthetic acceptance").Where(r => !r.Deleted && !r.Archived).Take(3).Select(r => r.Restore(default, default)).ToArray()
            : Enumerable.Range(1, 3).Select(n => Synthetic("phase5-live-" + Guid.NewGuid().ToString("N"), "Phase 5 synthetic acceptance " + n)).ToArray();
        if (items.Length != 3) { Console.WriteLine("FAIL expected_three_live_annotations"); return 1; }
        foreach (var item in items)
        {
            item.Persist = persistence.Save;
            int previousCount = item.Messages.Count; string? oldThread = item.ThreadId;
            for (int turn = 1; turn <= (resume ? 1 : 3); turn++)
            {
                var state = await service.AskAsync(item, $"这是软件验收测试。请仅回复数字 {turn}。");
                if (state != AnnotationGenerationState.Completed || item.ThreadRecoveryRequired || item.PersistenceFailure is not null)
                { Console.WriteLine($"FAIL live_turn state={state} recovery_required={item.ThreadRecoveryRequired}"); return 1; }
            }
            if (resume && oldThread != item.ThreadId) { Console.WriteLine("FAIL original_thread_changed"); return 1; }
            if (item.Messages.Count != previousCount + (resume ? 2 : 6)) { Console.WriteLine("FAIL live_message_count"); return 1; }
            persistence.Save(item, true);
            Console.WriteLine($"PASS live_annotation resume={resume} messages={item.Messages.Count} original_thread_preserved={!resume || oldThread == item.ThreadId}");
        }
        Console.WriteLine($"PASS live_isolation unique_threads={items.Select(i => i.ThreadId).Distinct().Count()} other_account_records={persistence.Database.Search("synthetic-other-account").Count}");
        return 0;
    }
    private static AnnotationItem Synthetic(string id, string quote)
    {
        var rectangle = new ScreenRectangle(100, 150, 200, 24);
        return new AnnotationItem(id, new SelectionSnapshot(default, new ScreenRectangle(0, 0, 1000, 800), new ScreenRectangle(0, 0, 1000, 800), rectangle, [rectangle], quote, DateTimeOffset.UtcNow));
    }
}
