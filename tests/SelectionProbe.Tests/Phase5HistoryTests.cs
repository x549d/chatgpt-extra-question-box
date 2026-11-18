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
    [Theory]
    [InlineData("原文")]
    [InlineData("用户问题")]
    [InlineData("AI answer")]
    [InlineData("新标题")]
    public void SearchFindsQuoteQuestionsAnswersAndTitle(string term)
    {
        using var db = new HistoryDatabase(DbPath); var item = Item(); item.AddUserMessage("用户问题");
        var m = item.BeginAssistantMessage(); item.UpdateAssistantMessage(m, "AI answer", AnnotationGenerationState.Completed, true);
        db.Save(HistoryRecord.Capture(item)); db.Rename(item.AnnotationId, "account-a", "新标题");
        Assert.Single(db.Search("account-a", term)); Assert.Empty(db.Search("account-b", term));
        Assert.Empty(db.Search("account-a", "' OR 1=1 --"));
    }
    [Fact] public void DateAndArchiveFiltersAndTrashRestoreAreScoped()
    {
        using var db = new HistoryDatabase(DbPath); var r = HistoryRecord.Capture(Item()); db.Save(r);
        db.Save(HistoryRecord.Capture(Item("b", "account-b")));
        Assert.Single(db.Search("account-a", since: r.CreatedAt.AddDays(-1), until: r.CreatedAt.AddDays(1)));
        Assert.Empty(db.Search("account-a", since: r.CreatedAt.AddDays(1)));
        db.SetFlags("a", "account-b", true, true); Assert.Single(db.Search("account-a"));
        db.SetFlags("a", "account-a", true, false); Assert.Single(db.Search("account-a", archive: 1)); Assert.Empty(db.Search("account-a", archive: 0));
        db.SetFlags("a", "account-a", false, true); Assert.Empty(db.Search("account-a")); Assert.Single(db.Search("account-a", deleted: true));
        db.SetFlags("a", "account-a", false, false); Assert.Single(db.Search("account-a"));
        Assert.Single(db.Search("account-b"));
    }
    [Fact] public void PurgeRequiresConfirmationAndCascadesOnlyDeletedAccountRecords()
    {
        using var db = new HistoryDatabase(DbPath);
        foreach (string id in new[] { "a", "b" }) { var item = Item(id); item.AddUserMessage("test"); db.Save(HistoryRecord.Capture(item)); }
        db.SetFlags("a", "account-a", false, true);
        Assert.Throws<InvalidOperationException>(() => db.PurgeDeleted("account-a", false));
        db.PurgeDeleted("account-b", true); Assert.Single(db.Search("account-a", deleted: true));
        db.PurgeDeleted("account-a", true); Assert.Single(db.Search("account-a"));
        Assert.Equal("0", db.Query("SELECT count(*) FROM messages WHERE annotation_id='a'")[0][0]);
    }
    [Fact] public void DelayedSnapshotsCannotResurrectPurgedAnnotation()
    {
        using var p = new HistoryPersistence(new HistoryDatabase(DbPath)); var item = Item(); p.Save(item, true);
        p.Suspend(item.AnnotationId); p.Database.SetFlags("a", "account-a", false, true); p.Database.PurgeDeleted("account-a", true);
        p.Save(item); p.Flush(); Assert.Empty(p.Database.Search("account-a"));
    }
    [Fact] public void HistoryMutationsReturnCurrentMetadataInsteadOfStalePayloadTimestamp()
    {
        using var db = new HistoryDatabase(DbPath);
        var oldTime = DateTimeOffset.Parse("2000-01-01T00:00:00+00:00");
        db.Save(HistoryRecord.Capture(Item()) with { CreatedAt = oldTime, UpdatedAt = oldTime });
        db.Rename("a", "account-a", "renamed");
        var renamed = Assert.Single(db.Search("account-a"));
        Assert.Equal("renamed", renamed.Title); Assert.True(renamed.UpdatedAt > oldTime);
        db.SetFlags("a", "account-a", true, false);
        var archived = Assert.Single(db.Search("account-a", archive: 1));
        Assert.True(archived.UpdatedAt >= renamed.UpdatedAt);
        var equivalentStart = DateTimeOffset.Parse("2000-01-01T08:00:00+08:00");
        Assert.Single(db.Search("account-a", since: equivalentStart, until: equivalentStart.AddHours(1)));
    }
    [Fact] public void InstallationBackupPreservesOldSchemaWithoutMigratingSource()
    {
        using var db = new HistoryDatabase(DbPath); db.Save(HistoryRecord.Capture(Item()));
        db.Execute("DROP INDEX annotations_scope_date"); db.Execute("DROP INDEX messages_annotation");
        foreach (string name in new[] { "title", "archived", "deleted" }) db.Execute("ALTER TABLE annotations DROP COLUMN " + name);
        db.Execute("PRAGMA user_version=1");
        string backup = Path.Combine(root, "before-migration.db");
        Assert.Equal(1, HistoryDatabase.BackupFile(DbPath, backup));
        Assert.Equal("1", db.Query("PRAGMA user_version")[0][0]);
        using var restored = new HistoryDatabase(backup); Assert.Single(restored.Search("account-a"));
    }
    [Fact] public void OnlineBackupIncludesCommittedWalAndCanRestoreExistingDestination()
    {
        using var db = new HistoryDatabase(DbPath); var item = Item(); item.AddUserMessage("committed WAL content"); db.Save(HistoryRecord.Capture(item));
        string backup = Path.Combine(root, "consistent.db"); Assert.Equal(2, HistoryDatabase.BackupFile(DbPath, backup));
        using (var snapshot = new HistoryDatabase(backup)) Assert.Equal("committed WAL content", snapshot.Search("account-a")[0].Messages[0].Text);
        db.Save(HistoryRecord.Capture(Item("post-upgrade"))); Assert.Equal(2, db.Search("account-a").Count);
        Assert.Equal(2, HistoryDatabase.BackupFile(backup, DbPath));
        Assert.Single(db.Search("account-a")); Assert.Equal("ok", db.Query("PRAGMA integrity_check")[0][0]);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
