using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace ContextNotes.SelectionProbe;

/// <summary>
/// Read-only structural diagnostics for real Codex Chat research. Output contains control types, automation ids,
/// class tokens, rectangles and lengths. It never prints selected text or message content: element names are only
/// printed for known role labels or for container controls whose names come from accessibility labels.
/// </summary>
internal sealed partial class UiaSelection
{
    private static readonly Regex ToggleName = new(@"^(Chat|Work|聊天|工作|ChatGPT|Codex)$|模式|mode",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Condition ToggleCondition = new OrCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.RadioButton),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));
    private static readonly Condition TextCondition =
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text);
    private string? lastSignature;
    private bool environmentReported;

    public string[] Inspect()
    {
        var lines = new List<string>();
        if (NativeWindows.Foreground() != window)
            return [];
        try
        {
            if (!TryGetDocument(out TextPattern? text))
                return Changed("no_document", ["inspect document=unavailable"]);

            if (!environmentReported)
            {
                environmentReported = true;
                var doc = document!.Current;
                lines.Add($"inspect document framework={doc.FrameworkId} automation_id={doc.AutomationId}");
                AddToggles(lines);
            }

            string mode = CurrentMode();
            TextPatternRange[] ranges = text.GetSelection() ?? [];
            var signature = new StringBuilder(mode).Append('|').Append(ranges.Length);
            var rectangles = new List<ScreenRectangle[]>();
            foreach (TextPatternRange range in ranges)
            {
                ScreenRectangle[] rects = [.. range.GetBoundingRectangles().Select(ToScreen)];
                rectangles.Add(rects);
                foreach (ScreenRectangle rect in rects)
                    signature.Append('|').Append(Coordinates(rect));
            }
            if (signature.ToString() == lastSignature)
                return [.. lines];
            lastSignature = signature.ToString();

            lines.Add($"inspect ---- selection_changed mode={mode} mode_button={SafeModeName()} ranges={ranges.Length}");
            for (int i = 0; i < ranges.Length; i++)
            {
                TextPatternRange range = ranges[i];
                int length = range.GetText(MaxTextLength + 1).Length;
                lines.Add($"inspect range={i} utf16={length} rects={string.Join(' ', rectangles[i].Select(Coordinates))}");
                if (length == 0)
                    continue;
                Classification found = Classify(text, range);
                lines.Add($"inspect range={i} classify={found.Region} viewport={Coordinates(found.Viewport)} " +
                    $"clamped={Coordinates(ClampToComposer(found.Viewport))} path={string.Join('/', found.Path)}");
                AddAncestors(lines, range);
            }
            AddEdits(lines, text, ranges);
            return [.. lines];
        }
        catch (Exception error) when (IsUiaFailure(error))
        {
            document = null;
            modeButton = null;
            composer = null;
            lines.Add($"inspect error={error.GetType().Name} hresult=0x{error.HResult:X8}");
            return [.. lines];
        }
    }

    private string[] Changed(string signature, string[] lines)
    {
        if (signature == lastSignature)
            return [];
        lastSignature = signature;
        return lines;
    }

    private string SafeModeName() => modeButton?.Current.Name is { } name
        && (ChatModeNames.Contains(name) || CodexModeNames.Contains(name)) ? name : "none";

    private void AddToggles(List<string> lines)
    {
        int count = 0;
        foreach (AutomationElement element in document!.FindAll(TreeScope.Descendants, ToggleCondition))
        {
            var info = element.Current;
            if (info.Name.Length is 0 or > 40 || !ToggleName.IsMatch(info.Name))
                continue;
            string state = element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object selection)
                ? $" selected={((SelectionItemPattern)selection).Current.IsSelected}"
                : element.TryGetCurrentPattern(TogglePattern.Pattern, out object toggle)
                    ? $" toggle={((TogglePattern)toggle).Current.ToggleState}" : "";
            lines.Add($"inspect toggle type={Short(info.ControlType)} name='{info.Name}' aid={info.AutomationId} " +
                $"rect={Coordinates(ToScreen(info.BoundingRectangle))} offscreen={info.IsOffscreen}{state} class={Classes(info.ClassName)}");
            if (++count >= 24)
                break;
        }
        if (count == 0)
            lines.Add("inspect toggle none");
    }

    private void AddAncestors(List<string> lines, TextPatternRange range)
    {
        AutomationElement? element = range.GetEnclosingElement()?.GetUpdatedCache(Ancestry);
        for (int depth = 0; element is not null && depth < MaxAncestors; depth++)
        {
            var info = element.Cached;
            bool scroll = element.GetCachedPropertyValue(AutomationElement.IsScrollPatternAvailableProperty) is true;
            lines.Add(FormattableString.Invariant(
                $"inspect   anc={depth} type={Short(info.ControlType)} loc={info.LocalizedControlType} aid={info.AutomationId} ") +
                $"fw={info.FrameworkId} scroll={scroll} rect={Coordinates(ToScreen(info.BoundingRectangle))} " +
                $"name={SafeName(info.ControlType, info.LocalizedControlType, info.Name)} class={Classes(info.ClassName)}");
            if (depth < 14)
                AddChildMarkers(lines, element);
            if (info.ControlType == ControlType.Document && info.AutomationId == "RootWebArea")
                break;
            element = TreeWalker.RawViewWalker.GetParent(element, Ancestry);
        }
    }

    private static void AddChildMarkers(List<string> lines, AutomationElement element)
    {
        int count = 0;
        foreach (AutomationElement child in element.FindAll(TreeScope.Children, TextCondition))
        {
            var info = child.Current;
            if (info.LocalizedControlType is not ("标题" or "heading"))
                continue;
            ScreenRectangle bounds = ToScreen(info.BoundingRectangle);
            lines.Add($"inspect     child_heading name={SafeHeading(info.Name)} sr_only={HasClass(info.ClassName, "sr-only")} " +
                $"rect={Coordinates(bounds)} marker={IsRoleMarker(info.LocalizedControlType, info.ClassName, bounds)}");
            if (++count >= 6)
                break;
        }
    }

    private void AddEdits(List<string> lines, TextPattern text, TextPatternRange[] ranges)
    {
        int count = 0;
        foreach (AutomationElement edit in document!.FindAll(TreeScope.Descendants, EditCondition))
        {
            var info = edit.Current;
            string contains = "n/a";
            if (ranges.Length > 0 && edit.TryGetCurrentPattern(TextPattern.Pattern, out _))
            {
                TextPatternRange bounds = text.RangeFromChild(edit);
                contains = (ranges[0].CompareEndpoints(TextPatternRangeEndpoint.Start, bounds, TextPatternRangeEndpoint.Start) >= 0
                    && ranges[0].CompareEndpoints(TextPatternRangeEndpoint.End, bounds, TextPatternRangeEndpoint.End) <= 0).ToString();
            }
            lines.Add($"inspect edit rect={Coordinates(ToScreen(info.BoundingRectangle))} prose_mirror={HasClass(info.ClassName, "ProseMirror")} " +
                $"contains_selection={contains} class={Classes(info.ClassName)}");
            if (++count >= 8)
                break;
        }
    }

    private static string SafeHeading(string name) =>
        AssistantHeadings.Contains(name) || UserHeadings.Contains(name) || NeutralHeadings.Contains(name)
            ? $"'{name}'" : $"len:{name.Length}";

    private static string SafeName(ControlType type, string localized, string name)
    {
        if (name.Length == 0)
            return "''";
        if (AssistantHeadings.Contains(name) || UserHeadings.Contains(name) || NeutralHeadings.Contains(name))
            return $"'{name}'";
        bool labelled = (type == ControlType.Group || type == ControlType.Pane || type == ControlType.Window
            || type == ControlType.Document) && localized is not ("标题" or "heading" or "段落" or "paragraph");
        return labelled && name.Length <= 40 ? $"'{name}'" : $"len:{name.Length}";
    }

    private static string Classes(string? classes)
    {
        if (string.IsNullOrWhiteSpace(classes))
            return "-";
        string joined = string.Join('.', classes.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return joined.Length <= 320 ? joined : joined[..320] + "…";
    }

    private static string Short(ControlType type) => type.ProgrammaticName.Replace("ControlType.", "", StringComparison.Ordinal);

    internal static string Coordinates(ScreenRectangle rectangle) => rectangle.IsValid
        ? string.Create(CultureInfo.InvariantCulture, $"[{rectangle.X:0},{rectangle.Y:0},{rectangle.Width:0},{rectangle.Height:0}]")
        : "[]";
}
