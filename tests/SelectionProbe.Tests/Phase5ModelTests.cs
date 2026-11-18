using System.IO;
using System.Text.Json;
using ContextNotes.SelectionProbe;
using Xunit;

namespace SelectionProbe.Tests;

public sealed class Phase5ModelTests
{
    [Fact] public async Task DefaultSelectionPersistsPerAccountAndIndividualModelReachesTurnProtocol()
    {
        string root = Path.Combine(Path.GetTempPath(), "context-model-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var db = new HistoryDatabase(Path.Combine(root, "history.db"));
            using var fake = new FakeCodexAppServer();
            using var service = new AnnotationService(() => fake.Transport, settingsDatabase: db);
            var account = await service.ReadCurrentAccountAsync();
            var model = Assert.Single(await service.GetModelsAsync());
            await service.SelectModelAsync(account.ScopeFingerprint, model.Model);
            Assert.Equal(model.Model, db.GetSetting("default-model:" + account.ScopeFingerprint));
            Assert.Null(db.GetSetting("default-model:other-account"));
            var item = Phase5HistoryTests.Item(); item.AccountScopeId = account.ScopeFingerprint; item.ThreadId = null;
            await service.SelectModelAsync(account.ScopeFingerprint, model.Model, item);
            Assert.Equal(AnnotationGenerationState.Completed, await service.AskAsync(item, "test selected model"));
            Assert.Equal(model.Model, Assert.Single(fake.RequestedModels));
            using var reopened = new HistoryDatabase(db.FilePath);
            Assert.Equal(model.Model, reopened.GetSetting("default-model:" + account.ScopeFingerprint));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact] public async Task InvalidModelAndChangedAccountAreRejected()
    {
        using var fake = new FakeCodexAppServer(); using var service = new AnnotationService(() => fake.Transport);
        var account = await service.ReadCurrentAccountAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SelectModelAsync(account.ScopeFingerprint, "not-in-catalog"));
        fake.UserId = "changed-account";
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SelectModelAsync(account.ScopeFingerprint, fake.DefaultModel));
        Assert.Empty(fake.ReceivedTurns);
    }
    [Fact] public async Task UnavailableSavedModelDoesNotSilentlyFallback()
    {
        using var fake = new FakeCodexAppServer(); using var service = new AnnotationService(() => fake.Transport);
        var item = Phase5HistoryTests.Item(); item.AccountScopeId = null; item.ModelName = "unavailable"; item.ThreadId = null;
        Assert.Equal(AnnotationGenerationState.Failed, await service.AskAsync(item, "test"));
        Assert.Contains("模型当前不可用", item.LastError); Assert.Empty(fake.ReceivedTurns);
    }
    [Fact] public async Task RestoredThreadResumesWithoutSendingUntilUserAsks()
    {
        using var fake = new FakeCodexAppServer { ResumeSupported = true }; using var service = new AnnotationService(() => fake.Transport);
        var account = await service.ReadCurrentAccountAsync();
        var original = Phase5HistoryTests.Item(); original.AccountScopeId = account.ScopeFingerprint;
        var restored = HistoryRecord.Capture(original).Restore(default, default);
        Assert.Empty(fake.ReceivedTurns);
        Assert.Equal(AnnotationGenerationState.Completed, await service.AskAsync(restored, "continue old thread"));
        Assert.Equal("old-thread", Assert.Single(fake.ResumedThreads));
        Assert.Equal("old-thread", Assert.Single(fake.ReceivedTurns).ThreadId);
    }
    [Fact] public async Task MissingThreadRequiresExplicitRecoveryAndNeverCreatesFakeContinuation()
    {
        using var fake = new FakeCodexAppServer(); using var service = new AnnotationService(() => fake.Transport);
        var account = await service.ReadCurrentAccountAsync(); var item = Phase5HistoryTests.Item(); item.AccountScopeId = account.ScopeFingerprint;
        var restored = HistoryRecord.Capture(item).Restore(default, default);
        Assert.Equal(AnnotationGenerationState.Failed, await service.AskAsync(restored, "continue"));
        Assert.True(restored.ThreadRecoveryRequired); Assert.Equal("old-thread", restored.ThreadId); Assert.Empty(fake.ReceivedTurns);
        await service.AskAsync(restored, "retry without confirmation"); Assert.Empty(fake.ReceivedTurns);
    }
    [Theory]
    [InlineData("{}", "未提供")]
    [InlineData("{\"rateLimits\":{\"primary\":{\"usedPercent\":42,\"windowDurationMins\":300,\"resetsAt\":null}}}", "42%")]
    [InlineData("{\"rateLimitsByLimitId\":{\"catalog-bucket\":{\"secondary\":{\"usedPercent\":80}}}}", "catalog-bucket")]
    public void UsageReflectsOnlyProvidedData(string json, string expected)
    {
        using var document = JsonDocument.Parse(json); string text = UsageFormatter.Format(document.RootElement);
        Assert.Contains(expected, text); Assert.DoesNotContain("费用", text);
    }
    [Fact] public async Task UnsupportedUsageIsExplicit()
    {
        using var fake = new FakeCodexAppServer(); using var service = new AnnotationService(() => fake.Transport);
        Assert.Contains("未提供", await service.ReadUsageAsync());
    }
}
