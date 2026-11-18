using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Text;
using System.Windows.Controls;

namespace ContextNotes.SelectionProbe;

internal static class Program
{
    [MTAThread]
    private static int Main(string[] args)
    {
        try
        {
            NativeWindows.UsePhysicalPixels();
            if (args.Length == 3 && args[0] is "--uia-worker" or "--test-worker"
                && long.TryParse(args[1], CultureInfo.InvariantCulture, out long handle)
                && uint.TryParse(args[2], CultureInfo.InvariantCulture, out uint pid))
            {
                return ProbeWorker.Run(new WindowIdentity((nint)handle, pid), args[0] == "--test-worker");
            }
            if (args is ["--self-test"])
                return SelfTestAsync().GetAwaiter().GetResult();
            if (args.Length == 0 || args is ["--companion"])
                return Companion.Run();
            if (args is ["--launch"])
                return Companion.Launch();
            if (args is ["--status"] or ["--stop"])
                return Companion.Status(stopRequested: args[0] == "--stop");
            if (args is ["--watch-codex"])
                return DesktopStartup.Watch();
            if (args is ["--watcher-status"] or ["--stop-watcher"])
                return DesktopStartup.Status(stopRequested: args[0] == "--stop-watcher");
            if (args is ["--inspect"])
                return InspectRunner.Run(TimeSpan.FromSeconds(180));
            if (args is ["--inspect", "--seconds", var inspectSeconds]
                && int.TryParse(inspectSeconds, out int inspectDuration) && inspectDuration is >= 10 and <= 900)
                return InspectRunner.Run(TimeSpan.FromSeconds(inspectDuration));

            int? seconds = null;
            bool fixtures = false;
            bool exerciseTimeout = false;
            bool overlayMode = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--seconds" when i + 1 < args.Length
                        && int.TryParse(args[i + 1], out int duration) && duration is >= 1 and <= 600:
                        seconds = duration;
                        i++;
                        break;
                    case "--overlay":
                        overlayMode = true;
                        break;
                    case "--fixtures":
                        fixtures = true;
                        break;
                    case "--exercise-timeout":
                        exerciseTimeout = true;
                        break;
                    default:
                        return Usage();
                }
            }
            if ((overlayMode && fixtures) || (exerciseTimeout && !fixtures && !overlayMode))
                return Usage();

            using var stopping = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, key) =>
            {
                key.Cancel = true;
                stopping.Cancel();
            };
            Console.CancelKeyPress += cancel;
            try
            {
                using var overlay = overlayMode ? new OverlayHost() : null;
                TimeSpan limit = seconds is { } value ? TimeSpan.FromSeconds(value)
                    : overlayMode ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(120);
                new ProbeMonitor(fixtures, exerciseTimeout, overlay)
                    .RunAsync(limit, stopping.Token).GetAwaiter().GetResult();
            }
            finally
            {
                Console.CancelKeyPress -= cancel;
            }
            return 0;
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Probe stopped with {error.GetType().Name}, HRESULT 0x{error.HResult:X8}.");
            return 1;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("SelectionProbe [--companion | --launch | --status | --stop | --self-test]");
        Console.Error.WriteLine("SelectionProbe [--watch-codex | --watcher-status | --stop-watcher]");
        Console.Error.WriteLine("SelectionProbe --inspect [--seconds 10..900]");
        Console.Error.WriteLine("SelectionProbe [--overlay [--exercise-timeout] | --fixtures] [--seconds 1..600]");
        return 2;
    }

    private static async Task<int> SelfTestAsync()
    {
        const string prose = "P1 中文，标点。 English, test!";
        const string code = "P1 = \"中  文, English!\"\n  P2 = \"空格; test?\"";
        Require(Fixtures.Match(prose) is { Exact: true }, "ordinal Chinese, English and punctuation");
        Require(Fixtures.Match(code) is { Exact: true }, "ordinal spaces, indentation and LF");
        Require(Fixtures.Match(code.Replace("\n", "\r\n")) is { Exact: false }, "CRLF preserved as a difference");
        Require(Fixtures.Match(code + "\n") is { Exact: false }, "terminal LF preserved as a difference");
        Require(Fixtures.Match(code.Replace("中  文", "中 文")) is null, "consecutive spaces are significant");
        Require(Fixtures.Match(prose.Replace('，', ',')) is null, "full-width punctuation is significant");
        Require(Fixtures.Match("arbitrary selection") is null, "arbitrary text is excluded from evidence");

        var rectangle = new ScreenRectangle(-650, -40, 329, 24);
        Require(rectangle.IsValid && rectangle.Contains(-500, -30), "negative physical coordinates");
        Require(!rectangle.Intersects(new ScreenRectangle(0, 0, 1920, 1080)), "separate monitors");
        Require(rectangle.Intersects(new ScreenRectangle(-1920, -100, 1920, 1080)), "visible on a left monitor");
        Require(!new ScreenRectangle(0, 0, double.NaN, 20).IsValid, "invalid rectangle");
        TestOverlayPlacement();
        await TestOverlayWindowAsync();
        await TestPhase3AnnotationsAsync();
        TestChatRules();
        TestDesktopStartup();

        const string programFiles = @"C:\Program Files";
        const string protectedImage = @"C:\Program Files\WindowsApps\OpenAI.Codex_26.1111.2228.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe";
        Require(NativeWindows.IsProtectedCodexImage(protectedImage, programFiles), "protected WindowsApps identity fallback");
        Require(!NativeWindows.IsProtectedCodexImage(protectedImage.Replace("Program Files", "Temp"), programFiles),
            "lookalike image outside WindowsApps is rejected");
        Require(!NativeWindows.IsProtectedCodexImage(protectedImage.Replace("2p2nqsd0c76g0", "other"), programFiles),
            "lookalike publisher is rejected");

        var elapsed = Stopwatch.StartNew();
        Task<WorkerReply> pending;
        int oldPid;
        using (var stalled = new ProbeWorker(default, test: true))
        {
            oldPid = stalled.Id;
            await stalled.RequestAsync(Operation.Ping).WaitAsync(ProbeMonitor.RequestTimeout);
            elapsed.Restart();
            pending = stalled.RequestAsync(Operation.Stall);
            try
            {
                await pending.WaitAsync(ProbeMonitor.RequestTimeout);
                throw new InvalidOperationException("Stalled worker returned unexpectedly.");
            }
            catch (TimeoutException)
            {
                Console.WriteLine($"PASS isolated worker deadline at {elapsed.ElapsedMilliseconds} ms");
            }
        }
        try
        {
            await pending.WaitAsync(ProbeMonitor.RequestTimeout);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException)
        {
            Console.WriteLine("PASS stalled worker pipe closed");
        }

        using var fresh = new ProbeWorker(default, test: true);
        await fresh.RequestAsync(Operation.Ping).WaitAsync(ProbeMonitor.RequestTimeout);
        Require(fresh.Id != oldPid, "fresh worker responds after timeout");
        return 0;
    }

    private static void TestOverlayPlacement()
    {
        var work = new ScreenRectangle(0, 0, 500, 320);
        var first = new ScreenRectangle(40, 80, 100, 24);
        var second = new ScreenRectangle(40, 110, 78, 24);
        var shortCharacter = new ScreenRectangle(40, 80, 3, 24);
        var artifact = new ScreenRectangle(480, 260, 1, 2);
        var smallFragment = new ScreenRectangle(480, 260, 3, 3);
        ScreenRectangle? anchor = OverlayPlacement.GetAnchorRectangle([first, second, artifact, smallFragment], work, 1.5, 1.5);
        Require(anchor == new ScreenRectangle(40, 80, 100, 54), "two-line anchor excludes isolated small fragments");
        Require(OverlayPlacement.GetAnchorRectangle([shortCharacter], work, 2, 2) == shortCharacter,
            "skinny single character retained at high DPI");
        Require(OverlayPlacement.GetAnchorRectangle([artifact], work, 1.5, 1.5) is null, "tiny-only ranges have no anchor");
        Require(OverlayPlacement.GetAnchorRectangle([new ScreenRectangle(20, -30, 120, 10)], work, 1.5, 1.5) is null,
            "offscreen selection has no anchor");
        Require(OverlayPlacement.GetAnchorRectangle([new ScreenRectangle(-10, 40, 40, 24)], work, 1.5, 1.5)
            == new ScreenRectangle(0, 40, 30, 24), "visible portion of clipped range anchors the button");

        Require(OverlayPlacement.Place(first, work, 162, 42, 11, 11)
            is { Side: "right", Bounds.X: 151, Bounds.Y: 80 }, "right side first, top-aligned with the selection");
        Require(OverlayPlacement.Place(new ScreenRectangle(402, 80, 80, 24), work, 162, 42, 11, 11)
            is { Side: "left", Bounds.X: 229 }, "left side fallback");
        Require(OverlayPlacement.Place(new ScreenRectangle(20, 80, 470, 24), work, 162, 42, 11, 11)
            is { Side: "below", Bounds.Y: 115 }, "below fallback");
        Require(OverlayPlacement.Place(new ScreenRectangle(20, 271, 470, 44), work, 162, 42, 11, 11)
            is { Side: "above", Bounds.Y: 218 }, "above fallback");
        Require(OverlayPlacement.Place(work, work, 162, 42, 11, 11) is null, "no placement overlaps a screen-filling selection");

        var left = new DisplayArea(1, new ScreenRectangle(-700, -50, 700, 550), new ScreenRectangle(-700, -50, 700, 510));
        var right = new DisplayArea(2, work, work);
        var spanning = new ScreenRectangle(-360, 0, 640, 305);
        ScreenRectangle[] multiScreen = [new ScreenRectangle(-290, 40, 350, 24), artifact];
        DisplayArea? chosen = OverlayPlacement.ChooseDisplay(multiScreen, spanning, [right, left]);
        Require(chosen == left, "selection area chooses a monitor with negative origin");
        var leftAnchor = OverlayPlacement.GetAnchorRectangle(multiScreen, spanning.Intersect(left.WorkArea), 2, 2);
        Require(leftAnchor is { } box && OverlayPlacement.Place(box, left.WorkArea, 216, 56, 14, 14)
            is { Side: "left", Bounds.Right: <= 0 }, "mixed-scale geometry stays inside the chosen work area");

        var chatViewport = new ScreenRectangle(100, 30, 300, 230);
        var nearComposer = new ScreenRectangle(210, 221, 178, 25);
        Require(OverlayPlacement.Place(nearComposer, chatViewport, 162, 42, 11, 11)
            is { Side: "above", Bounds.Bottom: <= 210, Bounds.Right: <= 400 },
            "conversation viewport beats spare desktop space near the composer");

        var desktopTranscript = new ScreenRectangle(1041, 296, 1229, 899);
        var archivedEditor = new ScreenRectangle(1127, -6668, 1057, 6322);
        var visibleEditor = new ScreenRectangle(1165, 1111, 831, 54);
        Require(!UiaSelection.IsVisibleComposerEdit(archivedEditor, desktopTranscript, offscreen: false)
            && !UiaSelection.IsVisibleComposerEdit(visibleEditor, desktopTranscript, offscreen: true)
            && UiaSelection.IsVisibleComposerEdit(visibleEditor, desktopTranscript, offscreen: false),
            "historical and offscreen editors do not qualify as the visible composer");
        ScreenRectangle aboveComposer = UiaSelection.ClipToComposer(desktopTranscript, visibleEditor, 24);
        var lowerSelection = new ScreenRectangle(1400, 1080, 120, 7);
        Require(aboveComposer.Bottom == 1087 && !aboveComposer.Intersects(visibleEditor)
            && OverlayPlacement.Place(lowerSelection, desktopTranscript, 99, 45, 11, 11) is { } unclipped
            && unclipped.Bounds.Intersects(visibleEditor)
            && OverlayPlacement.Place(lowerSelection, aboveComposer, 99, 45, 11, 11) is { Bounds.Bottom: <= 1087 },
            "visible composer reduces the overlay placement area at the bottom of a long chat");
        foreach (var anchorCase in new[] { first, new ScreenRectangle(402, 80, 80, 24), new ScreenRectangle(20, 80, 470, 24) })
        {
            Require(OverlayPlacement.Place(anchorCase, work, 162, 42, 11, 11) is { } placed
                && !placed.Bounds.Intersects(anchorCase) && placed.Bounds.X >= work.X && placed.Bounds.Right <= work.Right
                && placed.Bounds.Y >= work.Y && placed.Bounds.Bottom <= work.Bottom,
                FormattableString.Invariant($"entry never covers the selection and stays inside the viewport ({anchorCase.X})"));
        }
    }

    private static void TestDesktopStartup()
    {
        var gate = new DesktopStartupGate();
        Require(!gate.ShouldLaunch(0, 0) && gate.ShouldLaunch(110, 0), "desktop startup requires a foreground process");
        gate.Record(110, true, 10);
        Require(!gate.ShouldLaunch(110, 20_000) && gate.ShouldLaunch(111, 20_000),
            "desktop startup waits for a new Codex process after success");
        gate.Record(111, false, 20_000);
        Require(!gate.ShouldLaunch(111, 29_999) && gate.ShouldLaunch(111, 30_000),
            "desktop startup backs off after a failed launch");

        var hosted = Companion.CompanionStartInfo(@"C:\Program Files\dotnet\dotnet.exe", @"C:\Notes Test\SelectionProbe.dll");
        var native = Companion.CompanionStartInfo(@"C:\Notes Test\SelectionProbe.exe", @"C:\Notes Test\SelectionProbe.dll");
        Require(hosted.ArgumentList.SequenceEqual([@"C:\Notes Test\SelectionProbe.dll", "--companion"])
            && native.ArgumentList.SequenceEqual(["--companion"]), "launcher preserves argument boundaries on Windows");

        var messages = new List<string>();
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "SelectionProbe.exe");
        var unavailable = Companion.CompanionStartInfo(missing, "");
        Require(Companion.LaunchChild(unavailable, static () => false, messages.Add) == 1
            && messages.SequenceEqual(["companion=start_failed error_type=Win32Exception"]),
            "launcher reports a missing executable without logging its path");
    }

    private static void TestChatRules()
    {
        Require(UiaSelection.ModeFromName("切换模式，当前模式：ChatGPT") == "chat"
            && UiaSelection.ModeFromName("Switch mode, current mode: ChatGPT") == "chat", "ChatGPT mode switch confirms Chat");
        Require(UiaSelection.ModeFromName("切换模式，当前模式：Codex") == "codex", "Codex mode is recognised and excluded");
        Require(UiaSelection.ModeFromName(null) == "unknown" && UiaSelection.ModeFromName("切换模式") == "unknown",
            "missing or unexpected mode switch is not treated as Chat");
        Require(UiaSelection.RoleFromHeading("ChatGPT 说：") == SelectionRegion.Assistant
            && UiaSelection.RoleFromHeading("ChatGPT said:") == SelectionRegion.Assistant, "assistant role heading");
        Require(UiaSelection.RoleFromHeading("你说：") == SelectionRegion.User
            && UiaSelection.RoleFromHeading("You said:") == SelectionRegion.User, "user role heading");
        Require(UiaSelection.RoleFromHeading("最新一条回复") == SelectionRegion.Unknown, "latest-reply marker is neutral");
        Require(UiaSelection.IsRoleMarker("标题", "sr-only") && UiaSelection.IsRoleMarker("heading", "sr-only"),
            "hidden heading is a role marker");
        Require(!UiaSelection.IsRoleMarker("标题", "text-lg font-bold") && !UiaSelection.IsRoleMarker("文本", "sr-only"),
            "visible Markdown heading named like a role is ignored");

        ((int Start, int End) Range, SelectionRegion Role)[] headings =
        [
            ((30, 32), SelectionRegion.User),
            ((10, 12), SelectionRegion.Assistant),
            ((0, 2), SelectionRegion.User),
            ((60, 62), SelectionRegion.Unknown),
            ((40, 42), SelectionRegion.Assistant)
        ];
        SelectionRegion RoleAt(int start, int end) => UiaSelection.RoleFromMarkers<(int Start, int End)>((start, end), headings,
            static (left, leftEndpoint, right, rightEndpoint) =>
                (leftEndpoint == TextPatternRangeEndpoint.Start ? left.Start : left.End).CompareTo(
                    rightEndpoint == TextPatternRangeEndpoint.Start ? right.Start : right.End));
        Require(RoleAt(12, 30) == SelectionRegion.Assistant && RoleAt(42, 55) == SelectionRegion.Assistant,
            "flattened multi-turn transcript resolves the immediately preceding assistant heading");
        Require(RoleAt(3, 8) == SelectionRegion.User && RoleAt(34, 39) == SelectionRegion.User,
            "interleaved user turns stay excluded");
        Require(RoleAt(20, 34) == SelectionRegion.Unknown && RoleAt(11, 13) == SelectionRegion.Unknown,
            "selections crossing a heading or a message boundary stay excluded");
        Require(RoleAt(63, 68) == SelectionRegion.Unknown, "neutral marker stops a preceding assistant turn");
    }

    private static Task TestPhase3AnnotationsAsync()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sta = new Thread(() =>
        {
            try
            {
                var ownerWindow = new WindowIdentity(new nint(0x12340), 4242);
                var windowBounds = new ScreenRectangle(500, 150, 1600, 1000);
                var viewport = new ScreenRectangle(800, 220, 1100, 760);
                var anchorA = new ScreenRectangle(920, 360, 220, 28);
                const string multilineCode = "self.backbone = nn.Sequential(\n    nn.Linear(784, 256),\n    nn.ReLU()  # keep  two  spaces\n)";

                var selectionA = new OverlaySelection(ownerWindow, windowBounds, viewport, [anchorA], multilineCode);
                var snapshotA = SelectionSnapshot.FromSelection(selectionA, anchorA);
                Require(snapshotA.SelectedText == multilineCode && snapshotA.AnchorRectangle == anchorA,
                    "Phase 3A selection snapshot preserves exact text and anchor");

                // Placement priority: right -> left -> below -> above, plus multi-annotation non-overlap.
                var work = new ScreenRectangle(800, 220, 1100, 760);
                ButtonPlacement? rightPlaced = OverlayPlacement.PlaceAnnotation(
                    new ScreenRectangle(850, 340, 180, 28), work, windowBounds, 304, 220, 12, 12);
                Require(rightPlaced is { Side: "right" } rp && !rp.Bounds.Intersects(new ScreenRectangle(850, 340, 180, 28)),
                    "Phase 3E annotation placement prefers right side without covering selection");
                ScreenRectangle firstPlacedBounds = rightPlaced!.Value.Bounds;

                var nearRightEdge = new ScreenRectangle(1680, 340, 180, 28);
                ButtonPlacement? leftPlaced = OverlayPlacement.PlaceAnnotation(nearRightEdge, work, windowBounds, 304, 220, 12, 12);
                Require(leftPlaced is { Side: "left" } lp && !lp.Bounds.Intersects(nearRightEdge),
                    "Phase 3E annotation placement falls back to left side");

                var wideLine = new ScreenRectangle(820, 340, 1040, 28);
                ButtonPlacement? belowPlaced = OverlayPlacement.PlaceAnnotation(wideLine, work, windowBounds, 304, 220, 12, 12);
                Require(belowPlaced is { Side: "below" } bp && !bp.Bounds.Intersects(wideLine),
                    "Phase 3E annotation placement falls back to below");

                var bottomWideLine = new ScreenRectangle(820, 820, 1040, 28);
                ButtonPlacement? abovePlaced = OverlayPlacement.PlaceAnnotation(bottomWideLine, work, windowBounds, 304, 220, 12, 12);
                Require(abovePlaced is { Side: "above" } ap && !ap.Bounds.Intersects(bottomWideLine),
                    "Phase 3E annotation placement falls back to above near composer");

                ButtonPlacement? secondBesideSameAnchor = OverlayPlacement.PlaceAnnotation(
                    new ScreenRectangle(850, 340, 180, 28), work, windowBounds, 304, 220, 12, 12,
                    [firstPlacedBounds]);
                Require(secondBesideSameAnchor is { } sp && sp.Bounds != firstPlacedBounds,
                    "Phase 3D multiple annotations avoid identical bounds");

                // AnnotationItem and AnnotationWindow tests for 3A, 3B, 3C, 3D, 3E.
                var itemA = new AnnotationItem("ann-1", snapshotA);
                Require(itemA.ConversationId == "unconfirmed" && itemA.ExpandedState && itemA.QuoteExpandedState,
                    "Phase 3D annotation metadata initializes required fields without fabricating conversation_id");

                var windowA = new AnnotationWindow(itemA);
                nint hwndA = windowA.Handle;
                int exStyle = GetWindowLongW(hwndA, -20);
                Require((exStyle & 0x80) != 0 && (exStyle & 0x08000000) == 0,
                    "Phase 3C annotation window uses WS_EX_TOOLWINDOW without WS_EX_NOACTIVATE so it accepts keyboard and IME focus");
                Require(System.Windows.Input.InputMethod.GetIsInputMethodEnabled(windowA.InputControl),
                    "Phase 3C Chinese IME input method is enabled on the annotation TextBox");
                Require(windowA.InputControl.Focusable && windowA.InputControl.IsTabStop,
                    "Phase 3C annotation input control is focusable");
                Require(string.Equals(windowA.QuoteControl.Text, multilineCode, StringComparison.Ordinal),
                    "Phase 3B quote control preserves multiline code, indentation and consecutive spaces verbatim");

                // Typing and draft state in A
                windowA.InputControl.Text = "这个函数是什么意思？";
                Require(itemA.InputDraft == "这个函数是什么意思？",
                    "Phase 3C typing Chinese updates the annotation input draft");

                // Quote collapse/expand in A
                windowA.SetQuoteExpanded(false);
                Require(!itemA.QuoteExpandedState && windowA.QuoteContainer.Visibility == Visibility.Collapsed
                    && windowA.QuoteControl.Text == multilineCode,
                    "Phase 3B collapsing quote hides the quote box without losing the quoted text");
                windowA.SetQuoteExpanded(true);
                Require(itemA.QuoteExpandedState && windowA.QuoteContainer.Visibility == Visibility.Visible,
                    "Phase 3B expanding quote restores the quote box");

                // Submit draft in Phase 3 stages the question and shows Phase 4 notice without faking AI output.
                windowA.SubmitDraft("enter_key");
                Require(itemA.SubmittedDraft == "这个函数是什么意思？"
                    && itemA.InputDraft == "这个函数是什么意思？"
                    && windowA.StatusControl.Visibility == Visibility.Visible
                    && windowA.StatusControl.Text.Contains("Phase 4", StringComparison.Ordinal),
                    "Phase 3C submitting draft preserves user input and displays Phase 4 notice");

                // Second and third independent annotations (Phase 3D)
                var anchorB = new ScreenRectangle(920, 520, 180, 28);
                var snapshotB = SelectionSnapshot.FromSelection(
                    new OverlaySelection(ownerWindow, windowBounds, viewport, [anchorB], "nn.Linear(784, 256)"), anchorB);
                var itemB = new AnnotationItem("ann-2", snapshotB);
                var windowB = new AnnotationWindow(itemB);

                var anchorC = new ScreenRectangle(920, 660, 210, 28);
                var snapshotC = SelectionSnapshot.FromSelection(
                    new OverlaySelection(ownerWindow, windowBounds, viewport, [anchorC], "中文与 English 独立旁注 C"), anchorC);
                var itemC = new AnnotationItem("ann-3", snapshotC);
                var windowC = new AnnotationWindow(itemC);

                windowB.InputControl.Text = "第二个旁注的问题";
                windowC.InputControl.Text = "第三个旁注的问题";
                Require(itemA.SelectedText == multilineCode
                    && itemB.SelectedText == "nn.Linear(784, 256)"
                    && itemC.SelectedText == "中文与 English 独立旁注 C"
                    && itemA.InputDraft == "这个函数是什么意思？"
                    && itemB.InputDraft == "第二个旁注的问题"
                    && itemC.InputDraft == "第三个旁注的问题",
                    "Phase 3D multiple annotations keep SelectedText and InputDraft strictly isolated");

                // Collapsing B does not collapse A or C
                windowB.SetExpanded(false);
                Require(!itemB.ExpandedState && windowB.BodyContainer.Visibility == Visibility.Collapsed
                    && itemA.ExpandedState && windowA.BodyContainer.Visibility == Visibility.Visible
                    && itemC.ExpandedState && windowC.BodyContainer.Visibility == Visibility.Visible,
                    "Phase 3D collapsing one annotation does not affect other annotations");
                windowB.SetExpanded(true);
                Require(itemB.ExpandedState && windowB.BodyContainer.Visibility == Visibility.Visible
                    && itemB.InputDraft == "第二个旁注的问题",
                    "Phase 3D expanding a collapsed annotation restores its body and draft");

                // Window move delta and manual drag clamping (Phase 3E)
                itemA.ApplyOwnerDelta(new ScreenRectangle(560, 190, 1600, 1000));
                Require(itemA.CurrentAnchorRect == new ScreenRectangle(980, 400, 220, 28),
                    "Phase 3E owner window move shifts the tracked selection anchor by the window delta");

                itemB.RecordManualMove(new ScreenRectangle(1100, 480, 304, 220), windowBounds);
                itemB.ApplyOwnerDelta(new ScreenRectangle(550, 200, 1600, 1000));
                Require(itemB.IsManuallyMoved && itemB.ManualOffsetX == 600 && itemB.ManualOffsetY == 330,
                    "Phase 3E manually dragged annotation preserves its offset relative to the Codex window");

                ScreenRectangle clamped = OverlayPlacement.ClampToArea(
                    new ScreenRectangle(-200, 1800, 304, 220), new ScreenRectangle(0, 0, 1920, 1040));
                Require(clamped == new ScreenRectangle(0, 820, 304, 220),
                    "Phase 3E dragged or repositioned annotation is clamped inside the display work area");

                // Closing B does not close A or C
                windowB.CloseByUser();
                Require(itemB.IsClosed && !itemA.IsClosed && !itemC.IsClosed,
                    "Phase 3D closing one annotation leaves other annotations open");

                windowA.CloseByUser();
                windowC.CloseByUser();
                Require(itemA.IsClosed && itemC.IsClosed, "Phase 3E closing annotations releases resources cleanly");
                completed.SetResult();
            }
            catch (Exception error)
            {
                completed.SetException(error);
            }
        }) { IsBackground = true };
        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        return completed.Task;
    }

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern int GetWindowLongW(nint window, int index);

    private static Task TestOverlayWindowAsync()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sta = new Thread(() =>
        {
            try
            {
                var overlay = new QuestionOverlay();
                nint handle = overlay.Handle;
                Require(handle != 0 && !overlay.IsVisible && !overlay.IsActive,
                    "WPF HWND initializes without showing or activating a window");
                Require(SendMessageW(handle, 0x0021, 0, 0) == 3, "overlay handles WM_MOUSEACTIVATE without activation");
                bool clicked = false;
                overlay.Clicked = () => clicked = true;
                var button = (Button)overlay.Content;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(clicked && !button.Focusable && !button.IsTabStop && !overlay.IsActive,
                    "button click routes without keyboard focus");
                overlay.Close();
                completed.SetResult();
            }
            catch (Exception error)
            {
                completed.SetException(error);
            }
        }) { IsBackground = true };
        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        return completed.Task;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);

    private static void Require(bool succeeded, string description)
    {
        if (!succeeded)
            throw new InvalidOperationException(description);
        Console.WriteLine($"PASS {description}");
    }
}
