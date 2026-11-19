using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ContextNotes.SelectionProbe;
using Xunit;

namespace SelectionProbe.Tests;

public sealed class Phase3AnnotationTests
{
    private static readonly WindowIdentity TestWindow = new(new nint(0x24680), 8888);
    private static readonly ScreenRectangle DefaultWindowBounds = new(400, 120, 1600, 1000);
    private static readonly ScreenRectangle DefaultViewport = new(700, 200, 1150, 760);

    [Fact]
    public void Phase3A_Snapshot_BindsExactTextAndGeometryFromOverlaySelection()
    {
        const string code = "self.backbone = nn.Sequential(\n    nn.Linear(784, 256),\n    nn.ReLU()  # 中文  English!\n)";
        var anchor = new ScreenRectangle(840, 320, 240, 56);
        var selection = new OverlaySelection(TestWindow, DefaultWindowBounds, DefaultViewport, [anchor], code);

        SelectionSnapshot snapshot = SelectionSnapshot.FromSelection(selection, anchor);
        var item = new AnnotationItem("ann-1", snapshot);

        Assert.Equal("ann-1", item.AnnotationId);
        Assert.Equal("unconfirmed", item.ConversationId);
        Assert.Equal(code, item.SelectedText);
        Assert.Equal(anchor, item.SelectionRect);
        Assert.Equal(anchor, item.CurrentAnchorRect);
        Assert.True(item.ExpandedState);
        Assert.True(item.QuoteExpandedState);
    }

    [Fact]
    public void Phase3A_OverlayHost_OpensPopupFromSnapshotWithoutDuplicateOnSameSelection()
    {
        using var host = new OverlayHost();
        var anchor = new ScreenRectangle(840, 320, 200, 28);
        var snapshot = SelectionSnapshot.FromSelection(
            new OverlaySelection(TestWindow, DefaultWindowBounds, DefaultViewport, [anchor], "nn.Linear(784, 256)"),
            anchor);

        string firstId = "";
        string secondId = "";
        int countAfterReuse = 0;

        host.InvokeForTest(() =>
        {
            AnnotationWindow? first = host.OpenAnnotationFromSnapshot(snapshot);
            Assert.NotNull(first);
            firstId = first.Item.AnnotationId;

            // Opening again with the exact same selection snapshot reuses the existing popup instead of duplicating.
            AnnotationWindow? second = host.OpenAnnotationFromSnapshot(snapshot);
            Assert.NotNull(second);
            secondId = second.Item.AnnotationId;
            countAfterReuse = host.ActiveAnnotationsForTest.Count;
        });

        Assert.Equal("ann-1", firstId);
        Assert.Equal(firstId, secondId);
        Assert.Equal(1, countAfterReuse);
    }

    [Fact]
    public void Phase3B_QuoteControl_PreservesMultilineCodeIndentationSpacesAndCjkVerbatim()
    {
        RunOnSta(() =>
        {
            const string multiline = "def forward(self, x):\n    # 保留缩进与  连续空格\n    return self.backbone(x)\n";
            var anchor = new ScreenRectangle(840, 320, 260, 64);
            var item = new AnnotationItem(
                "ann-quote",
                SelectionSnapshot.FromSelection(new OverlaySelection(TestWindow, DefaultWindowBounds, DefaultViewport, [anchor], multiline), anchor));
            var win = new AnnotationWindow(item);

            Assert.Equal(multiline, win.QuoteControl.Text);
            Assert.Contains("\n    # 保留缩进与  连续空格\n", win.QuoteControl.Text, StringComparison.Ordinal);
            win.CloseByUser();
        });
    }

    [Fact]
    public void Phase3B_QuoteCollapseAndExpand_PreservesOriginalSelectedText()
    {
        RunOnSta(() =>
        {
            string longQuote = string.Join("\n", Enumerable.Range(1, 12).Select(i => $"Line {i}: Python 的装饰器与高阶函数示例 {i * 10}"));
            var anchor = new ScreenRectangle(840, 320, 320, 120);
            var item = new AnnotationItem(
                "ann-long",
                SelectionSnapshot.FromSelection(new OverlaySelection(TestWindow, DefaultWindowBounds, DefaultViewport, [anchor], longQuote), anchor));
            var win = new AnnotationWindow(item);

            Assert.True(item.QuoteExpandedState);
            Assert.Equal(Visibility.Visible, win.QuoteContainer.Visibility);

            win.SetQuoteExpanded(false);
            Assert.False(item.QuoteExpandedState);
            Assert.Equal(Visibility.Collapsed, win.QuoteContainer.Visibility);
            Assert.Equal(longQuote, item.SelectedText);
            Assert.Equal(longQuote, win.QuoteControl.Text);

            win.SetQuoteExpanded(true);
            Assert.True(item.QuoteExpandedState);
            Assert.Equal(Visibility.Visible, win.QuoteContainer.Visibility);
            Assert.Equal(longQuote, win.QuoteControl.Text);
            win.CloseByUser();
        });
    }

    [Fact]
    public void Phase3B_SubsequentSelections_NeverOverwriteExistingAnnotationQuote()
    {
        using var host = new OverlayHost();
        var anchorA = new ScreenRectangle(840, 320, 200, 28);
        var anchorB = new ScreenRectangle(840, 500, 200, 28);
        var snapshotA = SelectionSnapshot.FromSelection(
            new OverlaySelection(TestWindow, DefaultWindowBounds, DefaultViewport, [anchorA], "Original Selection A"),
            anchorA);

        host.InvokeForTest(() =>
        {
            AnnotationWindow? winA = host.OpenAnnotationFromSnapshot(snapshotA);
            Assert.NotNull(winA);

            // Simulate user selecting different text B in Codex Chat:
            var sampleB = new SelectionSample(
                SelectionSample.Selected,
                2,
                [new SelectedRange("New Selection B", SelectionRegion.Assistant, ["Document", "Group"], [anchorB])],
                "chat",
                DefaultViewport);
            host.UpdateAnnotationsForSampleForTest(TestWindow, DefaultWindowBounds, sampleB);

            Assert.Equal("Original Selection A", winA.Item.SelectedText);
            Assert.Equal("Original Selection A", winA.QuoteControl.Text);
        });
    }

    [Fact]
    public void Phase3C_WindowStyles_AllowKeyboardAndImeActivationUnlikeEntryButton()
    {
        RunOnSta(() =>
        {
            var entryButton = new QuestionOverlay();
            int buttonExStyle = GetWindowLongW(entryButton.Handle, -20);
            Assert.NotEqual(0, buttonExStyle & 0x08000000); // QuestionOverlay has WS_EX_NOACTIVATE

            var anchor = new ScreenRectangle(840, 320, 200, 28);
            var item = new AnnotationItem(
                "ann-focus",
                SelectionSnapshot.FromSelection(new OverlaySelection(TestWindow, DefaultWindowBounds, DefaultViewport, [anchor], "nn.Linear(784, 256)"), anchor));
            var popup = new AnnotationWindow(item);
            int popupExStyle = GetWindowLongW(popup.Handle, -20);

            Assert.NotEqual(0, popupExStyle & 0x80); // WS_EX_TOOLWINDOW set
            Assert.Equal(0, popupExStyle & 0x08000000); // WS_EX_NOACTIVATE unset so keyboard & IME work
            Assert.True(InputMethod.GetIsInputMethodEnabled(popup.InputControl));
            Assert.True(popup.InputControl.Focusable);
            Assert.True(popup.InputControl.IsTabStop);

            entryButton.Close();
            popup.CloseByUser();
        });
    }

    [Fact]
    public void Phase3C_KeyboardInput_UpdatesDraftAndSubmitPreservesInputWithPhase4Notice()
    {
        RunOnSta(() =>
        {
            var anchor = new ScreenRectangle(840, 320, 200, 28);
            var item = new AnnotationItem(
                "ann-input",
                SelectionSnapshot.FromSelection(new OverlaySelection(TestWindow, DefaultWindowBounds, DefaultViewport, [anchor], "nn.Linear(784, 256)"), anchor));
            var popup = new AnnotationWindow(item);

            popup.InputControl.Text = "这个函数是什么意思？";
            Assert.Equal("这个函数是什么意思？", item.InputDraft);
            Assert.Null(item.SubmittedDraft);

            popup.SubmitDraft("enter_key");
            Assert.Equal("这个函数是什么意思？", item.SubmittedDraft);
            Assert.Equal("这个函数是什么意思？", item.InputDraft);
            Assert.Equal(Visibility.Visible, popup.StatusControl.Visibility);
            Assert.Contains("Phase 4", popup.StatusControl.Text, StringComparison.Ordinal);

            popup.CloseByUser();
        });
    }

    [Fact]
    public void Phase3C_KeyboardEditing_SelectAllAndEditStayScopedToAnnotationInputBox()
    {
        RunOnSta(() =>
        {
            var anchor = new ScreenRectangle(840, 320, 200, 28);
            var item = new AnnotationItem(
                "ann-edit",
                SelectionSnapshot.FromSelection(new OverlaySelection(TestWindow, DefaultWindowBounds, DefaultViewport, [anchor], "Immutable Quoted Text"), anchor));
            var popup = new AnnotationWindow(item);

            popup.InputControl.Text = "草稿第一版";
            popup.InputControl.SelectAll();
            Assert.Equal("草稿第一版", popup.InputControl.SelectedText);

            // Replacing selection inside InputBox does not alter the quoted text
            popup.InputControl.SelectedText = "修改后的问题";
            Assert.Equal("修改后的问题", popup.InputControl.Text);
            Assert.Equal("修改后的问题", item.InputDraft);
            Assert.Equal("Immutable Quoted Text", item.SelectedText);
            Assert.Equal("Immutable Quoted Text", popup.QuoteControl.Text);

            popup.CloseByUser();
        });
    }

    [Fact]
    public void Phase3D_MultipleAnnotations_KeepTextDraftExpandAndCloseStateIsolated()
    {
        RunOnSta(() =>
        {
            var anchorA = new ScreenRectangle(840, 300, 220, 28);
            var anchorB = new ScreenRectangle(840, 460, 220, 28);
            var anchorC = new ScreenRectangle(840, 620, 220, 28);

            var winA = new AnnotationWindow(new AnnotationItem(
                "ann-1",
                SelectionSnapshot.FromSelection(new OverlaySelection(TestWindow, DefaultWindowBounds, DefaultViewport, [anchorA], "Quote A"), anchorA)));
            var winB = new AnnotationWindow(new AnnotationItem(
                "ann-2",
                SelectionSnapshot.FromSelection(new OverlaySelection(TestWindow, DefaultWindowBounds, DefaultViewport, [anchorB], "Quote B"), anchorB)));
            var winC = new AnnotationWindow(new AnnotationItem(
                "ann-3",
                SelectionSnapshot.FromSelection(new OverlaySelection(TestWindow, DefaultWindowBounds, DefaultViewport, [anchorC], "Quote C"), anchorC)));

            winA.InputControl.Text = "Question A";
            winB.InputControl.Text = "Question B";
            winC.InputControl.Text = "Question C";

            winB.SetExpanded(false);
            Assert.True(winA.Item.ExpandedState);
            Assert.False(winB.Item.ExpandedState);
            Assert.True(winC.Item.ExpandedState);
            Assert.Equal(Visibility.Visible, winA.BodyContainer.Visibility);
            Assert.Equal(Visibility.Collapsed, winB.BodyContainer.Visibility);
            Assert.Equal(Visibility.Visible, winC.BodyContainer.Visibility);

            winB.CloseByUser();
            Assert.False(winA.Item.IsClosed);
            Assert.True(winB.Item.IsClosed);
            Assert.False(winC.Item.IsClosed);
            Assert.Equal("Question A", winA.Item.InputDraft);
            Assert.Equal("Question C", winC.Item.InputDraft);

            winA.CloseByUser();
            winC.CloseByUser();
        });
    }

    [Fact]
    public void Phase3D_Placement_MultipleAnnotationsNeverShareIdenticalBounds()
    {
        var work = new ScreenRectangle(700, 200, 1150, 760);
        var anchor = new ScreenRectangle(760, 320, 180, 28);
        var occupied = new List<ScreenRectangle>();

        for (int i = 0; i < 4; i++)
        {
            ButtonPlacement? placed = OverlayPlacement.PlaceAnnotation(
                anchor, work, DefaultWindowBounds, 304, 220, 12, 12, occupied);
            Assert.NotNull(placed);
            Assert.DoesNotContain(placed.Value.Bounds, occupied);
            occupied.Add(placed.Value.Bounds);
        }
    }

    [Fact]
    public void Phase3E_Placement_FollowsRightLeftBelowAbovePriorityAndAvoidsComposer()
    {
        var work = new ScreenRectangle(700, 200, 1150, 760);

        var rightAnchor = new ScreenRectangle(760, 320, 180, 28);
        ButtonPlacement? right = OverlayPlacement.PlaceAnnotation(rightAnchor, work, DefaultWindowBounds, 304, 220, 12, 12);
        Assert.NotNull(right);
        Assert.Equal("right", right.Value.Side);
        Assert.False(right.Value.Bounds.Intersects(rightAnchor));

        var leftAnchor = new ScreenRectangle(1620, 320, 180, 28);
        ButtonPlacement? left = OverlayPlacement.PlaceAnnotation(leftAnchor, work, DefaultWindowBounds, 304, 220, 12, 12);
        Assert.NotNull(left);
        Assert.Equal("left", left.Value.Side);
        Assert.False(left.Value.Bounds.Intersects(leftAnchor));

        var belowAnchor = new ScreenRectangle(720, 320, 1100, 28);
        ButtonPlacement? below = OverlayPlacement.PlaceAnnotation(belowAnchor, work, DefaultWindowBounds, 304, 220, 12, 12);
        Assert.NotNull(below);
        Assert.Equal("below", below.Value.Side);
        Assert.False(below.Value.Bounds.Intersects(belowAnchor));

        var aboveAnchor = new ScreenRectangle(720, 820, 1100, 28);
        ButtonPlacement? above = OverlayPlacement.PlaceAnnotation(aboveAnchor, work, DefaultWindowBounds, 304, 220, 12, 12);
        Assert.NotNull(above);
        Assert.Equal("above", above.Value.Side);
        Assert.False(above.Value.Bounds.Intersects(aboveAnchor));
    }

    [Fact]
    public void Phase3E_WindowMoveAndManualDrag_UpdateCoordinatesAndClampToWorkArea()
    {
        var anchor = new ScreenRectangle(840, 320, 200, 28);
        var snapshot = SelectionSnapshot.FromSelection(
            new OverlaySelection(TestWindow, DefaultWindowBounds, DefaultViewport, [anchor], "nn.Linear(784, 256)"),
            anchor);
        var item = new AnnotationItem("ann-move", snapshot);

        var movedWindow = new ScreenRectangle(480, 170, 1600, 1000);
        item.ApplyOwnerDelta(movedWindow);
        Assert.Equal(new ScreenRectangle(920, 370, 200, 28), item.CurrentAnchorRect);

        item.RecordManualMove(new ScreenRectangle(1080, 420, 304, 220), movedWindow);
        Assert.True(item.IsManuallyMoved);
        Assert.Equal(600, item.ManualOffsetX);
        Assert.Equal(250, item.ManualOffsetY);

        ScreenRectangle clamped = OverlayPlacement.ClampToArea(
            new ScreenRectangle(-150, 2000, 304, 220),
            new ScreenRectangle(0, 0, 1920, 1040));
        Assert.Equal(new ScreenRectangle(0, 820, 304, 220), clamped);
    }

    [Fact]
    public void Phase3E_ForegroundAndScrollLifecycle_HideAndRestoreWithoutDuplicateWindows()
    {
        using var host = new OverlayHost();
        // Keep test geometry inside 800x600 so headless CI runners (e.g. 1024x768) do not clip the anchor.
        var windowBounds = new ScreenRectangle(40, 40, 720, 520);
        var viewport = new ScreenRectangle(80, 80, 640, 440);
        var anchor = new ScreenRectangle(220, 320, 200, 28);
        const string quote = "Scrollable selection text";
        var snapshot = SelectionSnapshot.FromSelection(
            new OverlaySelection(TestWindow, windowBounds, viewport, [anchor], quote),
            anchor);

        host.InvokeForTest(() =>
        {
            AnnotationWindow? win = host.OpenAnnotationFromSnapshot(snapshot);
            Assert.NotNull(win);
            win.InputControl.Text = "Draft kept across focus & scroll";

            // 1. Selection scrolls outside viewport -> annotation hides with HiddenByViewport
            var outOfView = new SelectionSample("selection_outside_viewport", 1, [], "chat", viewport);
            host.UpdateAnnotationsForSampleForTest(TestWindow, windowBounds, outOfView);
            Assert.True(win.Item.HiddenByViewport);

            // 2. Selection scrolls back into viewport at a new Y coordinate -> restores and updates anchor
            var scrolledAnchor = new ScreenRectangle(220, 200, 200, 28);
            var backInView = new SelectionSample(
                SelectionSample.Selected,
                2,
                [new SelectedRange(quote, SelectionRegion.Assistant, ["Document", "Group"], [scrolledAnchor])],
                "chat",
                viewport);
            host.UpdateAnnotationsForSampleForTest(TestWindow, windowBounds, backInView);
            Assert.False(win.Item.HiddenByViewport);
            Assert.Equal(scrolledAnchor, win.Item.CurrentAnchorRect);

            // 3. Codex loses foreground -> suspends annotation without closing or losing draft
            host.SuspendAnnotationsForTest();
            Assert.True(win.Item.HiddenByForeground);
            Assert.Equal("Draft kept across focus & scroll", win.Item.InputDraft);

            // 4. Codex regains foreground -> restores existing annotation without creating duplicates
            host.UpdateAnnotationsForOwnerForTest(TestWindow, windowBounds);
            Assert.False(win.Item.HiddenByForeground);
            Assert.Single(host.ActiveAnnotationsForTest);
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new AggregateException(failure);
    }

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern int GetWindowLongW(nint window, int index);
}
