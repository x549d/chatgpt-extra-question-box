using System.IO;
using ContextNotes.SelectionProbe;
using Xunit;

namespace SelectionProbe.Tests;

public sealed class Phase6RecoveryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "context-phase6-" + Guid.NewGuid().ToString("N"));

    [Fact] public void ColdForegroundCanStartWithoutAnExistingCompanion()
    {
        var gate = new DesktopStartupGate();
        Assert.True(gate.ShouldLaunch(110, 0, companionReady: false));
    }
    [Fact] public void HealthyCompanionDoesNotCauseRepeatedLauncherProcesses()
    {
        var gate = new DesktopStartupGate(); gate.Record(110, true, 0);
        Assert.False(gate.ShouldLaunch(110, 60_000, companionReady: true));
    }
    [Fact] public void SameClientCompanionCrashIsRetriedAfterTheBackoff()
    {
        var gate = new DesktopStartupGate(); gate.Record(110, true, 100);
        Assert.False(gate.ShouldLaunch(110, 10_099, companionReady: false));
        Assert.True(gate.ShouldLaunch(110, 10_100, companionReady: false));
        gate.Record(110, true, 10_100);
        Assert.False(gate.ShouldLaunch(110, 10_101, companionReady: true));
    }
    [Fact] public void FailedRecoveryCannotBusyLoop()
    {
        var gate = new DesktopStartupGate(); gate.Record(110, false, 20_000);
        Assert.False(gate.ShouldLaunch(110, 29_999, companionReady: false));
        Assert.True(gate.ShouldLaunch(110, 30_000, companionReady: false));
    }
    [Fact] public void ANewClientDoesNotWaitForThePreviousClientBackoff()
    {
        var gate = new DesktopStartupGate(); gate.Record(110, false, 20_000);
        Assert.True(gate.ShouldLaunch(111, 20_001, companionReady: false));
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void NoForegroundCannotTriggerBackgroundRecovery(bool ready)
    {
        Assert.False(new DesktopStartupGate().ShouldLaunch(0, 60_000, companionReady: ready));
    }
    [Theory] [InlineData(1)] [InlineData(2)]
    public void ProcessReopenPreservesThreadAndDraftButDoesNotReplayInterruptedGeneration(int value)
    {
        var state = (AnnotationGenerationState)value;
        string path = Path.Combine(root, "history.db");
        var item = Phase5HistoryTests.Item(); item.AddUserMessage("durable question");
        var partial = item.BeginAssistantMessage();
        item.UpdateAssistantMessage(partial, "incomplete answer", state, false);
        item.GenerationState = state;
        using (var database = new HistoryDatabase(path)) database.Save(HistoryRecord.Capture(item));
        using var reopened = new HistoryDatabase(path);
        var restored = Assert.Single(reopened.Search("account-a")).Restore(default, default);
        Assert.Equal("old-thread", restored.ThreadId); Assert.Equal("account-a", restored.AccountScopeId);
        Assert.Equal("未发送草稿", restored.InputDraft);
        Assert.Equal(AnnotationGenerationState.Failed, restored.GenerationState); Assert.False(restored.IsBusy);
        Assert.Equal(2, restored.Messages.Count); Assert.False(restored.Messages.Last().IsCommittedHistory);
        Assert.Equal(AnnotationGenerationState.Failed, restored.Messages.Last().State);
    }
    [Fact] public void InvalidBackupSourceCannotOverwriteTheLastGoodRecoveryDatabase()
    {
        Directory.CreateDirectory(root);
        string invalid = Path.Combine(root, "invalid.db"), valid = Path.Combine(root, "last-good.db");
        File.WriteAllText(invalid, "invalid synthetic database");
        using (var database = new HistoryDatabase(valid)) database.Save(HistoryRecord.Capture(Phase5HistoryTests.Item()));
        byte[] before = File.ReadAllBytes(valid);
        Assert.ThrowsAny<Exception>(() => HistoryDatabase.BackupFile(invalid, valid));
        Assert.Equal(before, File.ReadAllBytes(valid));
        using var recovered = new HistoryDatabase(valid);
        Assert.Single(recovered.Search("account-a")); Assert.Equal("ok", recovered.Query("PRAGMA integrity_check")[0][0]);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
