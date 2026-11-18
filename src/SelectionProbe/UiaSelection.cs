using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace ContextNotes.SelectionProbe;

internal enum SelectionRegion { Unknown, Editable, User, Assistant }

internal sealed record SelectedRange(string Text, SelectionRegion Region, string[] ControlPath, ScreenRectangle[] Rectangles);
internal sealed record SelectionSample(string State, long SelectionEvents, SelectedRange[] Ranges)
{
    public const string Selected = "selected";
}

internal sealed class UiaSelection(WindowIdentity window) : IDisposable
{
    private const int MaxTextLength = 32_768;
    private static readonly Condition DocumentCondition =
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document);
    private static readonly Condition CodexModeCondition = new AndCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
        new OrCondition(
            new PropertyCondition(AutomationElement.NameProperty, "切换模式，当前模式：Codex"),
            new PropertyCondition(AutomationElement.NameProperty, "Switch mode, current mode: Codex")));
    private static readonly Condition RoleHeadingCondition = new AndCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text),
        new OrCondition(
            new PropertyCondition(AutomationElement.NameProperty, "你说："),
            new PropertyCondition(AutomationElement.NameProperty, "ChatGPT 说："),
            new PropertyCondition(AutomationElement.NameProperty, "最新一条回复"),
            new PropertyCondition(AutomationElement.NameProperty, "You said:"),
            new PropertyCondition(AutomationElement.NameProperty, "ChatGPT said:"),
            new PropertyCondition(AutomationElement.NameProperty, "Latest reply")));

    private AutomationElement? root;
    private AutomationElement? document;
    private AutomationElement? modeIndicator;
    private long selectionEvents;
    private AutomationEventHandler? eventHandler;

    public SelectionSample Read()
    {
        if (NativeWindows.Foreground() != window)
            return Empty("inactive");

        try
        {
            root ??= AutomationElement.FromHandle(window.Handle);
            document ??= root.FindFirst(TreeScope.Descendants, DocumentCondition);
            document ??= FindDocumentFrom(AutomationElement.FocusedElement);
            if (document is null && NativeWindows.CursorInWindow(window) is { } point)
                document = FindDocumentFrom(AutomationElement.FromPoint(point));
            if (document is null)
                return Empty("no-document");
            if (!document.TryGetCurrentPattern(TextPattern.Pattern, out object pattern))
            {
                document = null;
                return Empty("no-text-pattern");
            }
            if (!IsCodexMode(document))
                return Empty("codex-mode-unconfirmed");

            var textPattern = (TextPattern)pattern;
            TextPatternRange[]? ranges = textPattern.GetSelection();
            if (ranges is null || ranges.Length == 0)
                return Empty("no-selection");
            if (ranges.Length > 16)
                return Empty("too-many-ranges");

            var selected = new List<SelectedRange>(ranges.Length);
            var visibleWindow = NativeWindows.Geometry(window, []).Bounds;
            foreach (TextPatternRange range in ranges)
            {
                if (range.CompareEndpoints(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End) == 0)
                    continue;

                var (region, path) = Classify(textPattern, range);
                if (region == SelectionRegion.Editable)
                    return Empty("editable-selection", new SelectedRange("", region, path, []));

                var rectangles = range.GetBoundingRectangles()
                    .Select(rect => new ScreenRectangle(rect.X, rect.Y, rect.Width, rect.Height))
                    .Where(rect => rect.IsValid && rect.Intersects(visibleWindow) && NativeWindows.IsOnDisplay(rect))
                    .ToArray();
                if (rectangles.Length == 0)
                    return Empty("selection-outside-view");

                string text = range.GetText(MaxTextLength + 1);
                if (text.Length == 0)
                    continue;
                if (text.Length > MaxTextLength)
                    return Empty("selection-too-long");
                selected.Add(new SelectedRange(text, region, path, rectangles));
            }

            if (NativeWindows.Foreground() != window)
                return Empty("inactive");
            return selected.Count == 0
                ? Empty("no-selection")
                : new SelectionSample(SelectionSample.Selected, Interlocked.Read(ref selectionEvents), [.. selected]);
        }
        catch (Exception error) when (error is ElementNotAvailableException or COMException or InvalidOperationException
            or Win32Exception or UnauthorizedAccessException)
        {
            document = null;
            modeIndicator = null;
            return Empty("uia-provider-unavailable");
        }
    }

    private bool IsCodexMode(AutomationElement currentDocument)
    {
        if (modeIndicator?.Current.Name is "切换模式，当前模式：Codex" or "Switch mode, current mode: Codex")
            return true;
        modeIndicator = currentDocument.FindFirst(TreeScope.Descendants, CodexModeCondition);
        return modeIndicator is not null;
    }

    private AutomationElement? FindDocumentFrom(AutomationElement? element)
    {
        AutomationElement? candidate = null;
        for (int depth = 0; element is not null && depth < 32; depth++)
        {
            if (element.Equals(root))
                return candidate;
            ControlType type = element.Current.ControlType;
            if (type == ControlType.Window)
                break;
            if (type == ControlType.Document)
                candidate ??= element;
            element = TreeWalker.RawViewWalker.GetParent(element);
        }
        return null;
    }

    public bool Subscribe()
    {
        if (root is null || NativeWindows.Foreground() != window)
            return false;
        AutomationEventHandler handler = (_, _) => Interlocked.Increment(ref selectionEvents);
        try
        {
            Automation.AddAutomationEventHandler(TextPattern.TextSelectionChangedEvent, root, TreeScope.Subtree, handler);
            eventHandler = handler;
            return true;
        }
        catch (Exception error) when (error is ElementNotAvailableException or COMException or InvalidOperationException
            or Win32Exception or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private (SelectionRegion Region, string[] Path) Classify(TextPattern text, TextPatternRange selection)
    {
        var path = new List<string>();
        AutomationElement? transcript = null;
        AutomationElement? element = selection.GetEnclosingElement();
        for (int depth = 0; element is not null && depth < 24; depth++)
        {
            ControlType type = element.Current.ControlType;
            path.Add(type.ProgrammaticName.Replace("ControlType.", "", StringComparison.Ordinal));
            if (type == ControlType.Edit)
                return (SelectionRegion.Editable, [.. path]);
            if (element.Equals(document) || element.Equals(root))
                break;

            if (transcript is null && type == ControlType.Group && element.Current.Name is "对话" or "Conversation")
                transcript = element;
            element = TreeWalker.RawViewWalker.GetParent(element);
        }
        SelectionRegion region = transcript is null ? SelectionRegion.Unknown : RoleInTranscript(text, selection, transcript);
        return (region, [.. path]);
    }

    private static SelectionRegion RoleInTranscript(TextPattern text, TextPatternRange selection, AutomationElement transcript)
    {
        const TextPatternRangeEndpoint start = TextPatternRangeEndpoint.Start;
        const TextPatternRangeEndpoint end = TextPatternRangeEndpoint.End;
        try
        {
            // The desktop exposes the role as a visually hidden heading within the transcript.
            // Ignore visible headings with the same names, including headings in Markdown replies.
            AutomationElementCollection headings = transcript.FindAll(TreeScope.Descendants, RoleHeadingCondition);
            if (headings.Count > 128)
                return SelectionRegion.Unknown;

            TextPatternRange? preceding = null;
            SelectionRegion region = SelectionRegion.Unknown;
            foreach (AutomationElement heading in headings)
            {
                var properties = heading.Current;
                var bounds = properties.BoundingRectangle;
                if (properties.LocalizedControlType is not ("标题" or "heading")
                    || bounds.Width is <= 0 or > 4 || bounds.Height is <= 0 or > 4)
                    continue;

                TextPatternRange marker = text.RangeFromChild(heading);
                if (marker.CompareEndpoints(start, selection, end) >= 0)
                    continue;
                if (marker.CompareEndpoints(end, selection, start) > 0)
                    return SelectionRegion.Unknown;

                SelectionRegion candidate = properties.Name switch
                {
                    "ChatGPT 说：" or "ChatGPT said:" => SelectionRegion.Assistant,
                    "你说：" or "You said:" => SelectionRegion.User,
                    _ => SelectionRegion.Unknown
                };
                int position = preceding is null ? 1 : marker.CompareEndpoints(end, preceding, end);
                if (position == 0 && candidate != region)
                    return SelectionRegion.Unknown;
                if (position > 0)
                {
                    preceding = marker;
                    region = candidate;
                }
            }
            return region;
        }
        catch (Exception error) when (error is ElementNotAvailableException or COMException or InvalidOperationException
            or Win32Exception or UnauthorizedAccessException or ArgumentException)
        {
            return SelectionRegion.Unknown;
        }
    }

    private SelectionSample Empty(string state, params SelectedRange[] ranges) =>
        new(state, Interlocked.Read(ref selectionEvents), ranges);

    public void Dispose()
    {
        if (root is not null && eventHandler is not null)
            Automation.RemoveAutomationEventHandler(TextPattern.TextSelectionChangedEvent, root, eventHandler);
    }
}
