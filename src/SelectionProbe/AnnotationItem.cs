using System.Text;

namespace ContextNotes.SelectionProbe;

internal enum AnnotationGenerationState
{
    Idle,
    Connecting,
    Generating,
    Completed,
    Cancelled,
    Failed
}

internal sealed class AnnotationMessage
{
    public required string MessageId { get; init; }
    public required string Role { get; init; }
    public string Text { get; set; } = "";
    public string? TurnId { get; set; }
    public AnnotationGenerationState State { get; set; } = AnnotationGenerationState.Completed;
    public bool IsCommittedHistory { get; set; } = true;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Immutable selection snapshot bound to the visible "提问" entry button at the moment the selection is validated.
/// Clicking the button consumes this snapshot directly without re-querying UI Automation or the clipboard.
/// </summary>
internal sealed record SelectionSnapshot(
    WindowIdentity Window,
    ScreenRectangle WindowBounds,
    ScreenRectangle Viewport,
    ScreenRectangle AnchorRectangle,
    ScreenRectangle[] Rectangles,
    string SelectedText,
    DateTimeOffset CapturedAt,
    SelectionTrace? Trace = null)
{
    public static SelectionSnapshot FromSelection(OverlaySelection selection, ScreenRectangle anchor, DateTimeOffset? now = null) =>
        new(
            selection.Window,
            selection.WindowBounds,
            selection.Viewport,
            anchor,
            [.. selection.Rectangles],
            selection.SelectedText,
            now ?? DateTimeOffset.UtcNow,
            selection.Trace);
}

/// <summary>
/// Holds the isolated state of a single inline annotation popup in Codex Chat.
/// Each annotation owns an immutable copy of SelectedText, an independent App Server ThreadId,
/// an account scope fingerprint, and its own multi-turn conversation history.
/// </summary>
internal sealed class AnnotationItem
{
    private readonly object sync = new();

    public AnnotationItem(string annotationId, SelectionSnapshot snapshot, string conversationId = "unconfirmed")
    {
        if (string.IsNullOrWhiteSpace(annotationId))
            throw new ArgumentException("Annotation ID is required.", nameof(annotationId));
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.SelectedText))
            throw new ArgumentException("Selected text snapshot cannot be empty.", nameof(snapshot));

        AnnotationId = annotationId;
        ConversationId = string.IsNullOrWhiteSpace(conversationId) ? "unconfirmed" : conversationId;
        Snapshot = snapshot;
        OwnerWindow = snapshot.Window;
        SelectedText = snapshot.SelectedText;
        SelectionRect = snapshot.AnchorRectangle;
        CurrentAnchorRect = snapshot.AnchorRectangle;
        SelectionRectangles = [.. snapshot.Rectangles];
        LastOwnerBounds = snapshot.WindowBounds;
        LastViewport = snapshot.Viewport;
        CreatedAt = snapshot.CapturedAt;
    }

    public string AnnotationId { get; }
    public string ConversationId { get; set; }
    public SelectionSnapshot Snapshot { get; }
    public WindowIdentity OwnerWindow { get; }

    /// <summary>Immutable verbatim selected text captured when the selection was made.</summary>
    public string SelectedText { get; }

    /// <summary>Initial physical screen rectangle of the selection anchor when captured.</summary>
    public ScreenRectangle SelectionRect { get; }

    /// <summary>Current physical screen rectangle of the selection anchor (updated on window move or scroll).</summary>
    public ScreenRectangle CurrentAnchorRect { get; set; }

    public ScreenRectangle[] SelectionRectangles { get; }
    public ScreenRectangle LastOwnerBounds { get; set; }
    public ScreenRectangle LastViewport { get; set; }
    public DateTimeOffset CreatedAt { get; }

    public string InputDraft { get; set; } = "";
    public string? SubmittedDraft { get; set; }
    public string? StatusNotice { get; set; }

    // Phase 4 AI session & multi-turn state (never stores plaintext tokens or emails)
    public string? ThreadId { get; set; }
    public string? SuspendedThreadId { get; set; }
    public bool RequiresThreadResume { get; set; }
    public bool ThreadRecoveryRequired { get; set; }
    public Action<AnnotationItem, bool>? Persist { get; set; }
    public long ThreadClientGeneration { get; set; }
    public bool ThreadAnchoredWithQuote { get; set; }
    public string? AccountScopeId { get; set; }
    public string? AccountSummary { get; set; }
    public string? ModelName { get; set; }
    public string? CurrentTurnId { get; set; }
    public AnnotationGenerationState GenerationState { get; set; } = AnnotationGenerationState.Idle;
    public string? LastError { get; set; }
    public string StreamingResponseText { get; set; } = "";
    public List<AnnotationMessage> Messages { get; } = [];

    public bool IsBusy => GenerationState is AnnotationGenerationState.Connecting or AnnotationGenerationState.Generating;

    public ScreenRectangle WindowPosition { get; set; }
    public string PlacementSide { get; set; } = "right";

    public bool ExpandedState { get; set; } = true;
    public bool QuoteExpandedState { get; set; } = true;

    public bool IsManuallyMoved { get; set; }
    public double ManualOffsetX { get; set; }
    public double ManualOffsetY { get; set; }

    public bool HiddenByViewport { get; set; }
    public bool HiddenByForeground { get; set; }
    public bool IsClosed { get; set; }

    public IReadOnlyList<AnnotationMessage> SnapshotMessages()
    {
        lock (sync)
        {
            return [.. Messages.Select(m => new AnnotationMessage
            {
                MessageId = m.MessageId,
                Role = m.Role,
                Text = m.Text,
                TurnId = m.TurnId,
                State = m.State,
                IsCommittedHistory = m.IsCommittedHistory,
                Timestamp = m.Timestamp
            })];
        }
    }

    public AnnotationMessage AddUserMessage(string question)
    {
        lock (sync)
        {
            var msg = new AnnotationMessage
            {
                MessageId = Guid.NewGuid().ToString("N"),
                Role = "user",
                Text = question,
                State = AnnotationGenerationState.Completed,
                IsCommittedHistory = true,
                Timestamp = DateTimeOffset.UtcNow
            };
            Messages.Add(msg);
            return msg;
        }
    }

    public void RemoveRejectedTurn(AnnotationMessage assistant)
    {
        lock (sync)
        {
            int index = Messages.IndexOf(assistant);
            if (index > 0) Messages.RemoveRange(index - 1, 2);
        }
    }

    public AnnotationMessage BeginAssistantMessage(string? turnId = null)
    {
        lock (sync)
        {
            var msg = new AnnotationMessage
            {
                MessageId = Guid.NewGuid().ToString("N"),
                Role = "assistant",
                Text = "",
                TurnId = turnId,
                State = AnnotationGenerationState.Generating,
                IsCommittedHistory = false,
                Timestamp = DateTimeOffset.UtcNow
            };
            Messages.Add(msg);
            return msg;
        }
    }

    public void UpdateAssistantMessage(AnnotationMessage message, string text, AnnotationGenerationState state, bool committed)
    {
        lock (sync)
        {
            message.Text = text;
            message.State = state;
            message.IsCommittedHistory = committed;
        }
    }

    /// <summary>
    /// Formats the prompt sent to the App Server. The original SelectedText is never mutated.
    /// On the first turn of a thread (or when rehydrating a thread after an App Server restart),
    /// includes the clearly delimited SelectedText and any prior committed Q&A turns.
    /// </summary>
    public string BuildTurnPrompt(string question, bool includeQuoteAndHistory, IReadOnlyList<AnnotationMessage>? priorCommittedMessages = null)
    {
        if (!includeQuoteAndHistory)
            return question;

        var sb = new StringBuilder();
        sb.AppendLine("以下是用户在 Codex Chat 助手回答中划词引用的原文内容（请严格按原文理解，不要修改原文）：");
        sb.AppendLine("<<<QUOTED_SELECTION");
        sb.AppendLine(SelectedText);
        sb.AppendLine("QUOTED_SELECTION");

        if (priorCommittedMessages is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("以下是当前旁注此前已完成的追问记录：");
            foreach (AnnotationMessage msg in priorCommittedMessages)
            {
                if (!msg.IsCommittedHistory || string.IsNullOrWhiteSpace(msg.Text))
                    continue;
                string speaker = msg.Role == "user" ? "用户" : "AI";
                sb.AppendLine($"{speaker}：{msg.Text}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("【用户当前问题】");
        sb.Append(question);
        return sb.ToString();
    }

    public void RecordManualMove(ScreenRectangle newBounds, ScreenRectangle ownerBounds)
    {
        WindowPosition = newBounds;
        PlacementSide = "manual";
        IsManuallyMoved = true;
        if (ownerBounds.IsValid)
        {
            LastOwnerBounds = ownerBounds;
            ManualOffsetX = newBounds.X - ownerBounds.X;
            ManualOffsetY = newBounds.Y - ownerBounds.Y;
        }
    }

    public void ApplyOwnerDelta(ScreenRectangle newOwnerBounds)
    {
        if (!newOwnerBounds.IsValid || !LastOwnerBounds.IsValid)
            return;

        double dx = newOwnerBounds.X - LastOwnerBounds.X;
        double dy = newOwnerBounds.Y - LastOwnerBounds.Y;
        if (dx == 0 && dy == 0 && newOwnerBounds.Width == LastOwnerBounds.Width && newOwnerBounds.Height == LastOwnerBounds.Height)
            return;

        CurrentAnchorRect = new ScreenRectangle(
            CurrentAnchorRect.X + dx,
            CurrentAnchorRect.Y + dy,
            CurrentAnchorRect.Width,
            CurrentAnchorRect.Height);

        if (LastViewport.IsValid)
        {
            var shiftedViewport = new ScreenRectangle(
                LastViewport.X + dx,
                LastViewport.Y + dy,
                LastViewport.Width + (newOwnerBounds.Width - LastOwnerBounds.Width),
                LastViewport.Height + (newOwnerBounds.Height - LastOwnerBounds.Height));
            var clipped = shiftedViewport.Intersect(newOwnerBounds);
            LastViewport = clipped.IsValid ? clipped : newOwnerBounds;
        }
        else
        {
            LastViewport = newOwnerBounds;
        }

        LastOwnerBounds = newOwnerBounds;
    }

    public bool MatchesSelection(string text, ScreenRectangle anchor) =>
        !IsClosed
        && string.Equals(SelectedText, text, StringComparison.Ordinal)
        && (CurrentAnchorRect.Intersects(anchor) || SelectionRect.Intersects(anchor));
}
