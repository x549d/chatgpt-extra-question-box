namespace ContextNotes.SelectionProbe;

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
    DateTimeOffset CapturedAt)
{
    public static SelectionSnapshot FromSelection(OverlaySelection selection, ScreenRectangle anchor, DateTimeOffset? now = null) =>
        new(
            selection.Window,
            selection.WindowBounds,
            selection.Viewport,
            anchor,
            [.. selection.Rectangles],
            selection.SelectedText,
            now ?? DateTimeOffset.UtcNow);
}

/// <summary>
/// Holds the isolated state of a single inline annotation popup in Codex Chat.
/// Each annotation owns an immutable copy of SelectedText and an independent InputDraft.
/// </summary>
internal sealed class AnnotationItem
{
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
    public string ConversationId { get; }
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
