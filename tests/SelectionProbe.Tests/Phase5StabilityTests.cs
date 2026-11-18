using System.Diagnostics;
using System.IO;
using System.Windows;
using ContextNotes.SelectionProbe;
using Xunit;
using Xunit.Abstractions;

namespace SelectionProbe.Tests;

public sealed class Phase5StabilityTests(ITestOutputHelper output) : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "context-stability-" + Guid.NewGuid().ToString("N"));
    private string DbPath => Path.Combine(root, "history.db");
    [Fact] public void TwentyAnnotationsThousandMessagesWriteSearchBackupAndReopen()
    {
        using var process = Process.GetCurrentProcess(); var cpu = process.TotalProcessorTime; long memory = process.PrivateMemorySize64;
        var elapsed = Stopwatch.StartNew();
        using (var db = new HistoryDatabase(DbPath))
        {
            for (int n = 0; n < 20; n++)
            {
                var item = Phase5HistoryTests.Item("load-" + n);
                for (int m = 0; m < 50; m++) item.AddUserMessage("中文 English " + new string('x', 2000) + m);
                var record = HistoryRecord.Capture(item); db.Save(record); db.Save(record with { Draft = "incremental" });
            }
            Assert.Equal(1000, db.Search("account-a").Sum(r => r.Messages.Length));
            Assert.Equal(20, db.Search("account-a", "English").Count);
            db.Backup(Path.Combine(root, "backup.db"));
        }
        using var reopened = new HistoryDatabase(DbPath);
        Assert.Equal("ok", reopened.Query("PRAGMA integrity_check")[0][0]);
        process.Refresh();
        output.WriteLine($"20 annotations / 1000 messages, write+update+search+backup+reopen: {elapsed.ElapsedMilliseconds}ms; process CPU delta={(process.TotalProcessorTime - cpu).TotalMilliseconds:F0}ms; private memory delta={process.PrivateMemorySize64 - memory} bytes.");
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(30));
    }
    [Fact] public void DatabaseBusyRetainsPendingSnapshotAndRetriesAfterUnlock()
    {
        using var p = new HistoryPersistence(new HistoryDatabase(DbPath)); using var locking = new HistoryDatabase(DbPath);
        locking.Execute("BEGIN IMMEDIATE");
        try { p.Save(Phase5HistoryTests.Item(), true); Assert.NotNull(p.LastFailure); }
        finally { locking.Execute("ROLLBACK"); }
        Assert.True(p.Flush()); Assert.Null(p.LastFailure); Assert.Single(p.Database.Search("account-a"));
    }
    [Fact] public async Task KilledProcessPreservesCommittedWalAndRollsBackOpenTransaction()
    {
        Directory.CreateDirectory(root);
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(typeof(AnnotationItem).Assembly.Location); info.ArgumentList.Add("--phase5-crash-fixture"); info.ArgumentList.Add(DbPath);
        using var child = Process.Start(info)!;
        try { Assert.Equal("fixture_ready", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15))); }
        finally { if (!child.HasExited) child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
        using var db = new HistoryDatabase(DbPath);
        Assert.Equal("crash-fixture", db.Search("")[0].SelectedText);
        Assert.Equal("durable synthetic input", db.Search("")[0].Messages[0].Text);
        Assert.Equal("ok", db.Query("PRAGMA integrity_check")[0][0]);
    }
    [Fact] public void SimulatedSqliteFullRollsBackAndRecoversAfterSpaceAvailable()
    {
        using var db = new HistoryDatabase(DbPath); db.Execute("PRAGMA max_page_count=15");
        var item = Phase5HistoryTests.Item(); item.AddUserMessage(new string('x', 2_000_000));
        Assert.Throws<IOException>(() => db.Save(HistoryRecord.Capture(item)));
        Assert.Empty(db.Search("account-a")); Assert.Equal("ok", db.Query("PRAGMA integrity_check")[0][0]);
        db.Execute("PRAGMA max_page_count=10000"); db.Save(HistoryRecord.Capture(item)); Assert.Single(db.Search("account-a"));
    }
    [Fact] public void SnapshotWithCrossAnnotationMessageIdCannotMoveOrOverwriteMessage()
    {
        using var db = new HistoryDatabase(DbPath); var first = Phase5HistoryTests.Item("first"); first.AddUserMessage("original");
        var r = HistoryRecord.Capture(first); db.Save(r);
        Assert.Throws<IOException>(() => db.Save(HistoryRecord.Capture(Phase5HistoryTests.Item("second")) with { Messages = r.Messages }));
        Assert.Single(db.Search("account-a")); Assert.Equal("original", db.Search("account-a")[0].Messages[0].Text);
    }
    [Fact] public void WritesAfterPersistenceDisposeAreIgnoredWithoutTimerFaults()
    {
        var p = new HistoryPersistence(new HistoryDatabase(DbPath)); p.Save(Phase5HistoryTests.Item(), true); p.Dispose();
        Parallel.For(0, 100, _ => p.Save(Phase5HistoryTests.Item())); Assert.False(p.Flush()); p.Dispose();
    }
    [Fact] public async Task StorageFailureBlocksNetworkTurnAndPreservesUserQuestion()
    {
        using var fake = new FakeCodexAppServer(); using var service = new AnnotationService(() => fake.Transport);
        var item = Phase5HistoryTests.Item(); item.AccountScopeId = null; item.Persist = (i, _) => i.PersistenceFailure = "磁盘空间不足，历史未保存";
        Assert.Equal(AnnotationGenerationState.Failed, await service.AskAsync(item, "must survive"));
        Assert.Empty(fake.ReceivedTurns); Assert.Equal("must survive", item.Messages[0].Text); Assert.False(item.Messages[1].IsCommittedHistory);
    }
    [Fact] public async Task UnconfirmedTurnStatusCannotBecomeCompletedHistory()
    {
        using var fake = new FakeCodexAppServer(); using var service = new AnnotationService(() => fake.Transport);
        fake.CustomTurnHandler = async (thread, turn, _, server) => await server.WriteServerLineAsync(System.Text.Json.JsonSerializer.Serialize(new { method = "turn/completed", @params = new { threadId = thread, turn = new { id = turn, status = "unknown" } } }));
        var item = Phase5HistoryTests.Item(); item.AccountScopeId = null; item.ThreadId = null;
        Assert.Equal(AnnotationGenerationState.Failed, await service.AskAsync(item, "test"));
        Assert.False(item.Messages.Last().IsCommittedHistory);
    }
    [Fact] public async Task RepeatedConcurrentGenerationAndCancellationKeepTerminalStatesStable()
    {
        for (int repeat = 0; repeat < 5; repeat++)
        {
            using var fake = new FakeCodexAppServer(); using var service = new AnnotationService(() => fake.Transport, deltaCoalesceInterval: TimeSpan.FromMilliseconds(1));
            var items = Enumerable.Range(0, 10).Select(i => { var item = Phase5HistoryTests.Item("race-" + i); item.AccountScopeId = null; item.ThreadId = null; return item; }).ToArray();
            var tasks = items.Select(i => service.AskAsync(i, "small response")).ToArray();
            await Task.WhenAll(items.Where((_, n) => n % 2 == 0).Select(service.CancelTurnAsync));
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
            foreach (var item in items)
            {
                Assert.False(item.IsBusy); Assert.Equal(2, item.Messages.Count);
                Assert.Equal(item.GenerationState, item.Messages[1].State);
                Assert.Equal(item.GenerationState == AnnotationGenerationState.Completed, item.Messages[1].IsCommittedHistory);
            }
        }
    }
    [Theory]
    [InlineData(1)] [InlineData(5)] [InlineData(10)]
    public void WpfAnnotationsReuseCompletedMessageViewsAndCloseCleanly(int count)
    {
        Exception? error = null; var timer = Stopwatch.StartNew();
        var thread = new Thread(() =>
        {
            try
            {
                var windows = new List<AnnotationWindow>();
                for (int i = 0; i < count; i++)
                {
                    var item = Phase5HistoryTests.Item("ui-" + i); item.AddUserMessage("question");
                    var m = item.BeginAssistantMessage(); item.UpdateAssistantMessage(m, "# Markdown\n```cs\n    code();\n```", AnnotationGenerationState.Completed, true);
                    var window = new AnnotationWindow(item); windows.Add(window);
                    var original = window.MessagesContainer.Children[0];
                    for (int tick = 0; tick < 50; tick++) window.RefreshConversationUi();
                    Assert.Same(original, window.MessagesContainer.Children[0]);
                    window.Measure(new Size(324, double.PositiveInfinity));
                }
                foreach (var window in windows) window.CloseByUser();
                Assert.All(windows, w => Assert.True(w.Item.IsClosed));
            }
            catch (Exception e) { error = e; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (error is not null) throw error;
        output.WriteLine($"{count} WPF annotations × 50 refreshes and cleanup: {timer.ElapsedMilliseconds}ms");
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
