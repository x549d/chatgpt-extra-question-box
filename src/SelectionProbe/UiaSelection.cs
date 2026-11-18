using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace ContextNotes.SelectionProbe;

internal enum SelectionRegion { Unknown, Composer, User, Assistant, Sidebar }

internal sealed record SelectedRange(string Text, SelectionRegion Region, string[] ControlPath, ScreenRectangle[] Rectangles);
internal sealed record SelectionSample(string State, long SelectionEvents, SelectedRange[] Ranges, string Mode,
    ScreenRectangle Viewport)
{
    public const string Selected = "selected";
}

internal readonly record struct Classification(SelectionRegion Region, string[] Path, ScreenRectangle Viewport);

/// <summary>
/// Reads the Codex desktop text selection through UI Automation. Runs only inside the isolated worker process.
/// Every lookup is bounded: the per-sample path walks the ancestors of the selection and queries direct
/// children; whole-document searches only happen during throttled (re)discovery of cached elements.
/// </summary>
internal sealed partial class UiaSelection(WindowIdentity window) : IDisposable
{
    private const int MaxTextLength = 32_768;
    private const int MaxAncestors = 48;
    private static readonly TimeSpan ModeRediscovery = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ComposerRediscovery = TimeSpan.FromSeconds(3);

    internal static readonly string[] ChatModeNames = ["切换模式，当前模式：ChatGPT", "Switch mode, current mode: ChatGPT"];
    internal static readonly string[] CodexModeNames = ["切换模式，当前模式：Codex", "Switch mode, current mode: Codex"];
    internal static readonly string[] AssistantHeadings = ["ChatGPT 说：", "ChatGPT said:"];
    internal static readonly string[] UserHeadings = ["你说：", "You said:"];
    internal static readonly string[] NeutralHeadings = ["最新一条回复", "Latest reply"];

    private static readonly Condition DocumentCondition =
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document);
    private static readonly Condition EditCondition =
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
    private static readonly Condition ModeButtonCondition = new AndCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
        new OrCondition([.. ChatModeNames.Concat(CodexModeNames)
            .Select(name => new PropertyCondition(AutomationElement.NameProperty, name))]));
    private static readonly Condition RoleHeadingCondition = new AndCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text),
        new OrCondition([.. AssistantHeadings.Concat(UserHeadings).Concat(NeutralHeadings)
            .Select(name => new PropertyCondition(AutomationElement.NameProperty, name))]));

    private static readonly CacheRequest Ancestry = CreateAncestryRequest();

    private AutomationElement? root;
    private AutomationElement? document;
    private AutomationElement? modeButton;
    private AutomationElement? composer;
    private long nextModeDiscovery;
    private long nextComposerDiscovery;
    private long selectionEvents;
    private AutomationEventHandler? eventHandler;

    public SelectionSample Read()
    {
        if (NativeWindows.Foreground() != window)
            return Empty("not_foreground");

        try
        {
            if (!TryGetDocument(out TextPattern? textPattern))
                return Empty("chat_mode_unconfirmed");

            string mode = CurrentMode();
            if (mode != "chat")
                return Empty(mode == "codex" ? "mode_codex" : "chat_mode_unconfirmed", mode);

            TextPatternRange[]? ranges = textPattern.GetSelection();
            if (ranges is null || ranges.Length == 0)
                return Empty("selection_empty", mode);
            if (ranges.Length > 16)
                return Empty("too_many_ranges", mode);

            var selected = new List<SelectedRange>(ranges.Length);
            ScreenRectangle viewport = default;
            foreach (TextPatternRange range in ranges)
            {
                if (range.CompareEndpoints(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End) == 0)
                    continue;

                Classification found = Classify(textPattern, range);
                if (found.Region != SelectionRegion.Assistant)
                    return Empty($"role_{found.Region.ToString().ToLowerInvariant()}", mode, default,
                        new SelectedRange("", found.Region, found.Path, []));

                viewport = ClampToComposer(found.Viewport);
                ScreenRectangle limits = viewport;
                var rectangles = range.GetBoundingRectangles()
                    .Select(rect => new ScreenRectangle(rect.X, rect.Y, rect.Width, rect.Height))
                    .Where(rect => rect.IsValid && rect.Intersects(limits) && NativeWindows.IsOnDisplay(rect))
                    .ToArray();
                if (rectangles.Length == 0)
                    return Empty("selection_outside_viewport", mode, viewport,
                        new SelectedRange("", found.Region, found.Path, []));

                string text = range.GetText(MaxTextLength + 1);
                if (text.Length == 0)
                    continue;
                if (text.Length > MaxTextLength)
                    return Empty("selection_too_long", mode, viewport);
                selected.Add(new SelectedRange(text, found.Region, found.Path, rectangles));
            }

            if (NativeWindows.Foreground() != window)
                return Empty("not_foreground");
            return selected.Count == 0
                ? Empty("selection_empty", mode, viewport)
                : new SelectionSample(SelectionSample.Selected, Interlocked.Read(ref selectionEvents), [.. selected],
                    mode, viewport);
        }
        catch (Exception error) when (IsUiaFailure(error))
        {
            document = null;
            modeButton = null;
            composer = null;
            return Empty("uia_provider_unavailable");
        }
    }

    private bool TryGetDocument([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TextPattern? textPattern)
    {
        textPattern = null;
        root ??= AutomationElement.FromHandle(window.Handle);
        document ??= root.FindFirst(TreeScope.Descendants, DocumentCondition);
        document ??= FindDocumentFrom(AutomationElement.FocusedElement);
        if (document is null && NativeWindows.CursorInWindow(window) is { } point)
            document = FindDocumentFrom(AutomationElement.FromPoint(point));
        if (document is null)
            return false;
        var current = document.Current;
        if (current.ControlType != ControlType.Document || current.FrameworkId != "Chrome"
            || current.AutomationId != "RootWebArea" || !document.TryGetCurrentPattern(TextPattern.Pattern, out object pattern))
        {
            document = null;
            return false;
        }
        textPattern = (TextPattern)pattern;
        return true;
    }

    /// <summary>Returns chat, codex or unknown from the cached mode switch. Never scans on every sample.</summary>
    private string CurrentMode()
    {
        string? name = null;
        if (modeButton is not null)
        {
            try
            {
                name = modeButton.Current.Name;
            }
            catch (ElementNotAvailableException)
            {
                modeButton = null;
            }
        }
        if (modeButton is null && Environment.TickCount64 >= nextModeDiscovery)
        {
            nextModeDiscovery = Environment.TickCount64 + (long)ModeRediscovery.TotalMilliseconds;
            modeButton = document!.FindFirst(TreeScope.Descendants, ModeButtonCondition);
            name = modeButton?.Current.Name;
        }
        return ModeFromName(name);
    }

    internal static string ModeFromName(string? name) =>
        name is null ? "unknown" : ChatModeNames.Contains(name) ? "chat" : CodexModeNames.Contains(name) ? "codex" : "unknown";

    /// <summary>
    /// Walks from the selection to the document. The first ancestor with a direct child role heading is the
    /// message turn. Both selection endpoints must stay inside that turn. Edit ancestors mark the composer.
    /// </summary>
    internal Classification Classify(TextPattern text, TextPatternRange selection)
    {
        var path = new List<string>();
        AutomationElement? element = selection.GetEnclosingElement()?.GetUpdatedCache(Ancestry);
        AutomationElement? turn = null;
        SelectionRegion role = SelectionRegion.Unknown;
        ScreenRectangle viewport = default;
        for (int depth = 0; element is not null && depth < MaxAncestors; depth++)
        {
            var info = element.Cached;
            if (IsDocument(info))
                break;
            path.Add(info.ControlType.ProgrammaticName.Replace("ControlType.", "", StringComparison.Ordinal));
            if (IsSidebar(info))
                return new(SelectionRegion.Sidebar, [.. path], default);
            if (info.ControlType == ControlType.Edit)
                return new(SelectionRegion.Composer, [.. path], default);

            if (turn is null)
            {
                SelectionRegion? found = RoleOfChildren(element);
                if (found is null)
                    return new(SelectionRegion.Unknown, [.. path], default);
                if (found != SelectionRegion.Unknown)
                {
                    turn = element;
                    role = found.Value;
                }
            }
            else if (turn is not null && !viewport.IsValid
                && element.GetCachedPropertyValue(AutomationElement.IsScrollPatternAvailableProperty) is true)
            {
                viewport = ToScreen(info.BoundingRectangle);
            }
            element = TreeWalker.RawViewWalker.GetParent(element, Ancestry);
        }

        if (turn is null)
            return new(SelectionRegion.Unknown, [.. path], default);

        TextPatternRange bounds = text.RangeFromChild(turn);
        if (selection.CompareEndpoints(TextPatternRangeEndpoint.Start, bounds, TextPatternRangeEndpoint.Start) < 0
            || selection.CompareEndpoints(TextPatternRangeEndpoint.End, bounds, TextPatternRangeEndpoint.End) > 0)
            return new(SelectionRegion.Unknown, [.. path], default);

        ScreenRectangle windowBounds = NativeWindows.WindowBounds(window.Handle);
        viewport = viewport.IsValid ? viewport.Intersect(windowBounds) : windowBounds;
        return new(role, [.. path], viewport);
    }

    /// <summary>Unknown when there is no role heading, null when headings disagree.</summary>
    private static SelectionRegion? RoleOfChildren(AutomationElement element)
    {
        SelectionRegion role = SelectionRegion.Unknown;
        foreach (AutomationElement heading in element.FindAll(TreeScope.Children, RoleHeadingCondition))
        {
            var info = heading.Current;
            if (!IsRoleMarker(info.LocalizedControlType, info.ClassName, ToScreen(info.BoundingRectangle)))
                continue;
            SelectionRegion candidate = RoleFromHeading(info.Name);
            if (candidate == SelectionRegion.Unknown)
                continue;
            if (role != SelectionRegion.Unknown && role != candidate)
                return null;
            role = candidate;
        }
        return role;
    }

    internal static SelectionRegion RoleFromHeading(string name) =>
        AssistantHeadings.Contains(name) ? SelectionRegion.Assistant
        : UserHeadings.Contains(name) ? SelectionRegion.User
        : SelectionRegion.Unknown;

    /// <summary>The desktop exposes roles as visually hidden headings; visible Markdown headings are ignored.</summary>
    internal static bool IsRoleMarker(string localizedType, string classes, ScreenRectangle bounds) =>
        localizedType is "标题" or "heading"
        && (HasClass(classes, "sr-only") || (bounds.Width is > 0 and <= 4 && bounds.Height is > 0 and <= 4));

    private static bool IsDocument(AutomationElement.AutomationElementInformation info) =>
        info.ControlType == ControlType.Document && info.AutomationId == "RootWebArea";

    internal static bool IsSidebar(AutomationElement.AutomationElementInformation info) =>
        info.AutomationId == "app-shell-sidebar"
        || info.LocalizedControlType is "导航" or "navigation"
        || HasClass(info.ClassName, "app-shell-left-panel") || HasClass(info.ClassName, "group/sidebar-rail");

    /// <summary>The composer floats above the transcript; keep the button out of its area.</summary>
    private ScreenRectangle ClampToComposer(ScreenRectangle viewport)
    {
        if (!viewport.IsValid)
            return viewport;
        ScreenRectangle editor = ComposerBounds();
        if (!editor.IsValid || editor.Right <= viewport.X || editor.X >= viewport.Right
            || editor.Y <= viewport.Y || editor.Y >= viewport.Bottom)
            return viewport;
        double padding = 16 * NativeWindows.Geometry(window, []).Dpi / 96.0;
        double bottom = Math.Max(viewport.Y, editor.Y - padding);
        return new ScreenRectangle(viewport.X, viewport.Y, viewport.Width, bottom - viewport.Y);
    }

    private ScreenRectangle ComposerBounds()
    {
        if (composer is not null)
        {
            try
            {
                ScreenRectangle bounds = ToScreen(composer.Current.BoundingRectangle);
                if (bounds.IsValid)
                    return bounds;
            }
            catch (ElementNotAvailableException)
            {
            }
            composer = null;
        }
        if (Environment.TickCount64 < nextComposerDiscovery)
            return default;
        nextComposerDiscovery = Environment.TickCount64 + (long)ComposerRediscovery.TotalMilliseconds;
        AutomationElement? best = null;
        ScreenRectangle bestBounds = default;
        foreach (AutomationElement edit in document!.FindAll(TreeScope.Descendants, EditCondition))
        {
            var info = edit.Current;
            ScreenRectangle bounds = ToScreen(info.BoundingRectangle);
            if (!bounds.IsValid)
                continue;
            bool prose = HasClass(info.ClassName, "ProseMirror");
            if (best is null || prose || bounds.Bottom > bestBounds.Bottom)
            {
                best = edit;
                bestBounds = bounds;
                if (prose)
                    break;
            }
        }
        composer = best;
        return bestBounds;
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
        catch (Exception error) when (IsUiaFailure(error))
        {
            return false;
        }
    }

    private static CacheRequest CreateAncestryRequest()
    {
        var request = new CacheRequest { TreeScope = TreeScope.Element, AutomationElementMode = AutomationElementMode.Full };
        request.Add(AutomationElement.ControlTypeProperty);
        request.Add(AutomationElement.LocalizedControlTypeProperty);
        request.Add(AutomationElement.AutomationIdProperty);
        request.Add(AutomationElement.ClassNameProperty);
        request.Add(AutomationElement.FrameworkIdProperty);
        request.Add(AutomationElement.BoundingRectangleProperty);
        request.Add(AutomationElement.IsScrollPatternAvailableProperty);
        request.Add(AutomationElement.NameProperty);
        return request;
    }

    internal static bool IsUiaFailure(Exception error) =>
        error is ElementNotAvailableException or COMException or InvalidOperationException
            or Win32Exception or UnauthorizedAccessException or ArgumentException;

    internal static bool HasClass(string? classes, string token, bool prefix = false) =>
        !string.IsNullOrEmpty(classes) && classes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(part => prefix
            ? part.StartsWith(token, StringComparison.Ordinal) : string.Equals(part, token, StringComparison.Ordinal));

    internal static ScreenRectangle ToScreen(System.Windows.Rect rectangle) =>
        rectangle.IsEmpty ? default : new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);

    private SelectionSample Empty(string state, string mode = "unknown", ScreenRectangle viewport = default,
        params SelectedRange[] ranges) => new(state, Interlocked.Read(ref selectionEvents), ranges, mode, viewport);

    public void Dispose()
    {
        if (root is not null && eventHandler is not null)
            Automation.RemoveAutomationEventHandler(TextPattern.TextSelectionChangedEvent, root, eventHandler);
    }
}
