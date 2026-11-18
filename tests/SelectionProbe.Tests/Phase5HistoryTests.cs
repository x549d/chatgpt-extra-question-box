using System.IO;
using ContextNotes.SelectionProbe;
using Xunit;

namespace SelectionProbe.Tests;

public sealed class Phase5HistoryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "context-notes-tests-" + Guid.NewGuid().ToString("N"));
    private string DbPath => Path.Combine(root, "history.db");
    internal static AnnotationItem Item(string id = "a", string scope = "account-a")
    {
        var rect = new ScreenRectangle(50, 60, 300, 200);
        return new AnnotationItem(id, new SelectionSnapshot(default, rect, rect, rect, [rect], "原文\n  code 中英文", DateTimeOffset.UtcNow)) { AccountScopeId = scope, ThreadId = "old-thread", InputDraft = "未发送草稿", WindowPosition = rect, ExpandedState = false, QuoteExpandedState = false };
    }
    [Fact] public void InitializeAndReopenPreservesThreeAnnotationsAndNineTurns()
    {
        using (var db = new HistoryDatabase(DbPath))
            for (int a = 0; a < 3; a++)
            {
                var item = Item("a" + a);
                for (int t = 0; t < 3; t++) { item.AddUserMessage($"问题 {a}-{t}"); var m = item.BeginAssistantMessage("turn-" + t); item.UpdateAssistantMessage(m, $"回答 {a}-{t}", AnnotationGenerationState.Completed, true); }
                db.Save(HistoryRecord.Capture(item));
            }
        using var reopened = new HistoryDatabase(DbPath);
        var records = reopened.Search("account-a");
        Assert.Equal(3, records.Count);
        foreach (var r in records)
        {
            var item = r.Restore(default, new ScreenRectangle(0, 0, 1000, 1000));
            Assert.Equal(6, item.Messages.Count);
            Assert.Equal("未发送草稿", item.InputDraft);
            Assert.Equal("old-thread", item.ThreadId);
            Assert.True(item.RequiresThreadResume);
            Assert.False(item.ExpandedState); Assert.False(item.QuoteExpandedState);
            Assert.Equal(r.Position, item.WindowPosition);
            Assert.Equal(r.SelectedText, item.SelectedText);
        }
        Assert.Empty(reopened.Search("account-b"));
    }
    [Fact] public void InterruptedGenerationNeverBecomesCommittedHistory()
    {
        using var db = new HistoryDatabase(DbPath);
        var item = Item(); item.GenerationState = AnnotationGenerationState.Generating;
        item.AddUserMessage("保存用户问题"); var m = item.BeginAssistantMessage(); item.UpdateAssistantMessage(m, "部分输出", AnnotationGenerationState.Generating, false);
        db.Save(HistoryRecord.Capture(item));
        var restored = db.Search("account-a")[0].Restore(default, default);
        Assert.Equal(AnnotationGenerationState.Failed, restored.GenerationState);
        Assert.Equal("部分输出", restored.Messages[1].Text);
        Assert.False(restored.Messages[1].IsCommittedHistory);
        Assert.Equal(AnnotationGenerationState.Failed, restored.Messages[1].State);
    }
    [Fact] public void DuplicateMessageIdsRollbackWholeSnapshot()
    {
        using var db = new HistoryDatabase(DbPath);
        var r = HistoryRecord.Capture(Item()); db.Save(r);
        var m = new AnnotationMessage { MessageId = "duplicate", Role = "user", Text = "test" };
        Assert.Throws<IOException>(() => db.Save(r with { Messages = [m, m], Draft = "must rollback" }));
        Assert.Empty(db.Search("account-a")[0].Messages);
        Assert.Equal(r.Draft, db.Search("account-a")[0].Draft);
    }
    [Fact] public void OwnershipCannotChangeAndUpdatesAreUnique()
    {
        using var db = new HistoryDatabase(DbPath); var r = HistoryRecord.Capture(Item()); db.Save(r); db.Save(r with { Draft = "updated" });
        Assert.Single(db.Search("account-a"));
        Assert.Throws<InvalidOperationException>(() => db.Save(r with { AccountScope = "account-b" }));
        Assert.Empty(db.Search("account-b"));
    }
    [Fact] public void BackupRestoresMessagesAndSettings()
    {
        using var db = new HistoryDatabase(DbPath); var item = Item(); item.AddUserMessage("backup"); db.Save(HistoryRecord.Capture(item)); db.SetSetting("model", "catalog-model");
        string backup = Path.Combine(root, "backup.db"); db.Backup(backup);
        using var restored = new HistoryDatabase(backup);
        Assert.Equal("backup", restored.Search("account-a")[0].Messages[0].Text);
        Assert.Equal("catalog-model", restored.GetSetting("model"));
    }
    [Fact] public void NewerDatabaseIsPreservedAndRejected()
    {
        using (var db = new HistoryDatabase(DbPath)) db.Execute("PRAGMA user_version=999");
        Assert.Throws<InvalidDataException>(() => new HistoryDatabase(DbPath));
        Assert.True(File.Exists(DbPath));
    }
    [Fact] public async Task ConcurrentWritesAndReadsKeepAnnotationMessagesSeparate()
    {
        using var db = new HistoryDatabase(DbPath);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() =>
        {
            var item = Item("concurrent-" + i); item.AddUserMessage(i.ToString()); db.Save(HistoryRecord.Capture(item)); db.Search("account-a");
        })));
        Assert.Equal(20, db.Search("account-a").Count);
        Assert.Equal(20, db.Search("account-a").Select(r => r.Messages[0].Text).Distinct().Count());
    }
    [Fact] public void PendingDraftFlushesOnDispose()
    {
        using (var writer = new HistoryPersistence(new HistoryDatabase(DbPath))) writer.Save(Item());
        using var db = new HistoryDatabase(DbPath);
        Assert.Equal("未发送草稿", db.Search("account-a")[0].Draft);
        Assert.True(File.Exists(DbPath + ".backup.db"));
    }
    [Fact] public void VersionOneMigratesWithPreMigrationBackup()
    {
        using (var db = new HistoryDatabase(DbPath))
        {
            db.Save(HistoryRecord.Capture(Item()));
            db.Execute("DROP INDEX annotations_scope_date"); db.Execute("DROP INDEX messages_annotation");
            db.Execute("ALTER TABLE annotations DROP COLUMN title"); db.Execute("ALTER TABLE annotations DROP COLUMN archived"); db.Execute("ALTER TABLE annotations DROP COLUMN deleted");
            db.Execute("PRAGMA user_version=1");
        }
        using var migrated = new HistoryDatabase(DbPath);
        Assert.Single(migrated.Search("account-a")); Assert.True(File.Exists(DbPath + ".pre-v2.db"));
        Assert.Equal("2", migrated.Query("PRAGMA user_version")[0][0]);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
