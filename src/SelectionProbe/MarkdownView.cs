using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ContextNotes.SelectionProbe;

internal sealed record MarkdownBlock(string Kind, string Text, string Language = "", int Level = 0);

// Deliberately restricted Markdown: no HTML, scripts, image fetches, or arbitrary URI schemes.
// Unclosed emphasis is literal, unclosed fences render safely as code during streaming.
internal static class MarkdownView
{
    private static readonly TimeSpan RegexLimit = TimeSpan.FromMilliseconds(100);
    private static bool Matches(string text, string pattern) => Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, RegexLimit);
    public static IReadOnlyList<MarkdownBlock> Parse(string source)
    {
        var lines = new List<(string Text, int Start, int End)>();
        for (int start = 0; start < source.Length;)
        {
            int end = source.IndexOf('\n', start); end = end < 0 ? source.Length : end + 1;
            lines.Add((source[start..end].TrimEnd('\r', '\n'), start, end)); start = end;
        }
        var blocks = new List<MarkdownBlock>();
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i].Text; string trimmed = line.TrimStart();
            if (trimmed.Length == 0) continue;
            if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
            {
                char fence = trimmed[0]; int count = trimmed.TakeWhile(c => c == fence).Count();
                string language = trimmed[count..].Trim(); int start = lines[i].End; int end = source.Length;
                while (++i < lines.Count)
                {
                    string closing = lines[i].Text.Trim();
                    if (closing.Length >= count && closing.All(c => c == fence)) { end = lines[i].Start; break; }
                }
                blocks.Add(new("code", source[start..end], language)); continue;
            }
            if (i + 1 < lines.Count && line.Contains('|') && Matches(lines[i + 1].Text, @"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$"))
            {
                var rows = new List<string> { line }; i++;
                while (i + 1 < lines.Count && lines[i + 1].Text.Contains('|') && !string.IsNullOrWhiteSpace(lines[i + 1].Text)) rows.Add(lines[++i].Text);
                blocks.Add(new("table", string.Join("\n", rows))); continue;
            }
            int heading = trimmed.TakeWhile(c => c == '#').Count();
            if (heading is >= 1 and <= 6 && trimmed.Length > heading && trimmed[heading] == ' ')
            { blocks.Add(new("heading", trimmed[(heading + 1)..], Level: heading)); continue; }
            if (trimmed.StartsWith('>')) { blocks.Add(new("quote", trimmed[1..].TrimStart())); continue; }
            if (Matches(trimmed, @"^(?:[-*+] |\d+[.)]\s)")) { blocks.Add(new("list", trimmed)); continue; }
            blocks.Add(new("paragraph", line));
        }
        return blocks;
    }

    public static FrameworkElement Render(string source)
    {
        var panel = new StackPanel();
        foreach (var block in Parse(source))
        {
            if (block.Kind == "code") { panel.Children.Add(Code(block)); continue; }
            if (block.Kind == "table") { panel.Children.Add(Table(block.Text)); continue; }
            var text = Text(block.Text);
            if (block.Kind == "heading") { text.FontWeight = FontWeights.Bold; text.FontSize = 19 - block.Level; }
            if (block.Kind == "quote")
                panel.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(8, 2, 0, 2), Child = text });
            else panel.Children.Add(text);
        }
        return panel;
    }

    private static FrameworkElement Code(MarkdownBlock block)
    {
        var panel = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(24, 25, 28)), Margin = new Thickness(0, 4, 0, 4) };
        var toolbar = new DockPanel { Margin = new Thickness(4) }; DockPanel.SetDock(toolbar, Dock.Top); panel.Children.Add(toolbar);
        var copy = new Button { Content = "复制代码", Padding = new Thickness(5, 2, 5, 2), HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(copy, Dock.Right); toolbar.Children.Add(copy);
        toolbar.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(block.Language) ? "代码" : block.Language, Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center });
        copy.Click += (_, _) => Copy(block.Text, copy);
        var box = new TextBox
        {
            Text = block.Text, Tag = block.Text, IsReadOnly = true, AcceptsReturn = true, AcceptsTab = true,
            Background = Brushes.Transparent, Foreground = Brushes.Gainsboro, BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11.5,
            TextWrapping = TextWrapping.NoWrap, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 240, Padding = new Thickness(6)
        };
        panel.Children.Add(box);
        return new Expander { Header = "代码 · " + (string.IsNullOrEmpty(block.Language) ? "text" : block.Language), Foreground = Brushes.Gainsboro, IsExpanded = block.Text.Count(c => c == '\n') <= 12, Content = panel };
    }
    internal static bool IsSafeLink(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http";
    internal static void Copy(string value, Button? button = null)
    {
        try { Clipboard.SetText(value); if (button is not null) button.ToolTip = "已复制原始内容"; }
        catch (System.Runtime.InteropServices.COMException) { if (button is not null) button.ToolTip = "剪贴板正忙，请重试"; }
    }
    private static TextBlock Text(string value)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gainsboro, FontSize = 12, Margin = new Thickness(0, 2, 0, 2) };
        // Only complete tokens match. Anything still arriving is shown literally.
        var tokens = Regex.Matches(value, @"(`[^`\n]+`|\*\*[^*\n]+\*\*|\*[^*\n]+\*|\[[^\]\n]+\]\([^\s)]+\))", RegexOptions.CultureInvariant, RegexLimit);
        int start = 0;
        foreach (Match match in tokens)
        {
            if (match.Index > start) text.Inlines.Add(new Run(value[start..match.Index]));
            string token = match.Value;
            if (token.StartsWith('`')) text.Inlines.Add(new Run(token[1..^1]) { FontFamily = new FontFamily("Consolas"), Background = new SolidColorBrush(Color.FromRgb(50, 52, 59)) });
            else if (token.StartsWith("**")) text.Inlines.Add(new Bold(new Run(token[2..^2])));
            else if (token.StartsWith('*')) text.Inlines.Add(new Italic(new Run(token[1..^1])));
            else
            {
                int separator = token.IndexOf("](", StringComparison.Ordinal); string url = token[(separator + 2)..^1];
                if (IsSafeLink(url))
                {
                    var link = new Hyperlink(new Run(token[1..separator])) { Foreground = Brushes.LightSkyBlue, ToolTip = url };
                    link.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception) { link.ToolTip = "无法打开链接"; } };
                    text.Inlines.Add(link);
                }
                else text.Inlines.Add(new Run(token));
            }
            start = match.Index + match.Length;
        }
        if (start < value.Length) text.Inlines.Add(new Run(value[start..]));
        return text;
    }
    private static FrameworkElement Table(string value)
    {
        var grid = new Grid(); var rows = value.Split('\n').Select(r => r.Trim().Trim('|').Split('|')).ToArray();
        int columns = Math.Min(20, rows.Max(r => r.Length));
        for (int c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int r = 0; r < rows.Length; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < Math.Min(columns, rows[r].Length); c++)
            {
                var text = Text(rows[r][c].Trim()); text.MaxWidth = 220; if (r == 0) text.FontWeight = FontWeights.Bold;
                var cell = new Border { Child = text, BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(0.5), Padding = new Thickness(5, 3, 5, 3) };
                Grid.SetRow(cell, r); Grid.SetColumn(cell, c); grid.Children.Add(cell);
            }
        }
        return new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
}
