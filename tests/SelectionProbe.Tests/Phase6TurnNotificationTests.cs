using System.Text.Json;
using ContextNotes.SelectionProbe;
using Xunit;

namespace SelectionProbe.Tests;

public sealed class Phase6TurnNotificationTests
{
    [Theory]
    [InlineData("thread/tokenUsage/updated")]
    [InlineData("hook/started")]
    [InlineData("future/metadata")]
    public async Task PreviousTurnMetadataCannotBindTheResumedQuestion(string method)
    {
        using var server = new FakeCodexAppServer { ResumeSupported = true };
        server.BeforeTurnStartReply = async (thread, _, fake) =>
            await fake.WriteServerLineAsync(JsonSerializer.Serialize(new { method, @params = new { threadId = thread, turnId = "previous-completed-turn" } }));
        using var service = new AnnotationService(() => server.Transport, turnTimeout: TimeSpan.FromSeconds(1));
        var account = await service.ReadCurrentAccountAsync();
        var item = Phase5HistoryTests.Item();
        item.AccountScopeId = account.ScopeFingerprint;
        item.RequiresThreadResume = true;

        var state = await service.AskAsync(item, "synthetic resumed question").WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(AnnotationGenerationState.Completed, state);
        Assert.Equal("old-thread", item.ThreadId);
        Assert.Contains("old-thread/turn-1", item.StreamingResponseText);
        Assert.True(item.Messages.Last().IsCommittedHistory);
        Assert.Empty(server.InterruptedTurns);
    }

    [Fact]
    public async Task GenuineCompletionBeforeStartReplyStillCompletesTheQuestion()
    {
        using var server = new FakeCodexAppServer();
        server.BeforeTurnStartReply = async (thread, turn, fake) =>
        {
            await fake.WriteServerLineAsync(JsonSerializer.Serialize(new { method = "item/agentMessage/delta", @params = new { threadId = thread, turnId = turn, delta = "early complete" } }));
            await fake.WriteServerLineAsync(JsonSerializer.Serialize(new { method = "turn/completed", @params = new { threadId = thread, turn = new { id = turn, status = "completed" } } }));
        };
        server.CustomTurnHandler = (_, _, _, _) => Task.CompletedTask;
        using var service = new AnnotationService(() => server.Transport, turnTimeout: TimeSpan.FromSeconds(1));
        var bounds = new ScreenRectangle(0, 0, 800, 600);
        var quote = new ScreenRectangle(10, 20, 100, 20);
        var item = new AnnotationItem("early-synthetic", new SelectionSnapshot(default, bounds, bounds, quote, [quote], "synthetic quote", DateTimeOffset.UtcNow));

        var state = await service.AskAsync(item, "synthetic question").WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(AnnotationGenerationState.Completed, state);
        Assert.Equal("early complete", item.StreamingResponseText);
        Assert.True(item.Messages.Last().IsCommittedHistory);
    }
}
