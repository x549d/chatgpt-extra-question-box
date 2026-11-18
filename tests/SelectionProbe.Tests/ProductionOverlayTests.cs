using ContextNotes.SelectionProbe;
using Xunit;

namespace SelectionProbe.Tests;

public sealed class ProductionOverlayTests
{
    private static readonly WindowIdentity Owner = new(42, 123);
    private static readonly ScreenRectangle Window = new(80, 40, 1200, 960);
    private static readonly ScreenRectangle Viewport = new(220, 160, 820, 700);
    private static readonly ScreenRectangle Line = new(280, 246, 382, 27);

    [Theory]
    [InlineData("WinSta0", "Default", true)]
    [InlineData("winsta0", "default", true)]
    [InlineData("NonInteractive", "Default", false)]
    [InlineData("WinSta0", "OtherDesktop", false)]
    [InlineData(null, null, false)]
    public void OnlyInteractiveDefaultDesktopCanOwnProductionSingleton(string? station, string? desktop, bool allowed)
    {
        Assert.Equal(allowed, new DesktopContext(station, desktop, 0).CanRunOverlay);
    }

    [Fact]
    public void ProductionSelectionBuildsTheSameSnapshotUsedByAnnotation()
    {
        const string text = "  print(\"中文\")\n    return  value";
        var secondLine = Line with { Y = 280 };
        ScreenRectangle[] rectangles = [Line, secondLine];
        var sample = new SelectionSample(SelectionSample.Selected, 3,
            [new SelectedRange(text, SelectionRegion.Assistant, [], rectangles)], "chat", Viewport);
        string reason = ProbeMonitor.EvaluateSelection(sample, rectangles, boundsMatch: true);
        var trace = new SelectionTrace(sample.Mode, 2, text.Length, rectangles.Length, "assistant", reason);

        Assert.Equal("eligible", reason);
        var selection = Assert.IsType<OverlaySelection>(ProbeMonitor.CreateOverlaySelection(Owner, Window, sample, rectangles, trace));
        var anchor = Assert.IsType<ScreenRectangle>(OverlayPlacement.GetAnchorRectangle(rectangles, Viewport, 1.5, 1.5));
        var placement = Assert.IsType<ButtonPlacement>(OverlayPlacement.Place(anchor, Viewport, 99, 45, 11, 11));
        Assert.False(placement.Bounds.Intersects(anchor));
        Assert.Equal(placement.Bounds, placement.Bounds.Intersect(Viewport));

        SelectionSnapshot snapshot = SelectionSnapshot.FromSelection(selection, anchor);
        Assert.Equal(Owner, snapshot.Window);
        Assert.Equal(Viewport, snapshot.Viewport);
        Assert.Equal(text, snapshot.SelectedText);
        Assert.Equal(anchor, snapshot.AnchorRectangle);
        Assert.Equal(trace, snapshot.Trace);
    }

    [Theory]
    [InlineData("chat", "User", "text", true, true, true, "role_user")]
    [InlineData("chat", "Composer", "text", true, true, true, "role_composer")]
    [InlineData("codex", "Assistant", "text", true, true, true, "chat_mode_unconfirmed")]
    [InlineData("chat", "Assistant", " ", true, true, true, "selection_empty_text")]
    [InlineData("chat", "Assistant", "text", false, true, true, "window_moved")]
    [InlineData("chat", "Assistant", "text", true, false, true, "invalid_viewport")]
    [InlineData("chat", "Assistant", "text", true, true, false, "invalid_rectangle")]
    public void ProductionIneligibilityHasASpecificReasonAndNoPublishableSelection(
        string mode, string role, string text, bool boundsMatch, bool viewportValid, bool rectangleValid, string expected)
    {
        ScreenRectangle[] rectangles = rectangleValid ? [Line] : [new ScreenRectangle(2, 2, 1, 2)];
        var sample = new SelectionSample(SelectionSample.Selected, 0,
            [new SelectedRange(text, Enum.Parse<SelectionRegion>(role), [], rectangles)], mode, viewportValid ? Viewport : default);
        string reason = ProbeMonitor.EvaluateSelection(sample, rectangles, boundsMatch);
        Assert.Equal(expected, reason);
        var trace = new SelectionTrace(mode, 3, text.Length, rectangles.Length, role.ToString().ToLowerInvariant(), reason);
        Assert.Null(ProbeMonitor.CreateOverlaySelection(Owner, Window, sample, rectangles, trace));
    }

    [Fact]
    public void ProductionFailureDiagnosticsUseATypeWithoutAnExceptionMessage()
    {
        var failure = new InvalidOperationException("private diagnostic detail");
        string line = SelectionTrace.Empty("monitor_restart").Format("probe_monitor_failed", false, failure.GetType());
        Assert.Contains($"pid={Environment.ProcessId}", line);
        Assert.Contains("mode=unknown selection_id=0 text_length=0 rectangle_count=0 role=none", line);
        Assert.Contains("eligibility_reason=monitor_restart overlay_visible=false error_type=InvalidOperationException", line);
        Assert.DoesNotContain(failure.Message, line);
    }
}
