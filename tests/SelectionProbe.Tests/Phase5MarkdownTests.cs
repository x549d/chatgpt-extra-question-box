using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using ContextNotes.SelectionProbe;
using Xunit;

namespace SelectionProbe.Tests;

public sealed class Phase5MarkdownTests
{
    [Fact] public void ParsesHeadingsListsQuotesTablesAndMixedLanguage()
    {
        var blocks = MarkdownView.Parse("# 标题 Heading\n**粗体** *斜体* `code` [链接](https://example.com)\n- 列表\n1. ordered\n> 引用\n| 名称 | 值 |\n| --- | --- |\n| 中文 | English |\n");
        Assert.Contains(blocks, b => b.Kind == "heading" && b.Level == 1);
        Assert.Equal(2, blocks.Count(b => b.Kind == "list")); Assert.Contains(blocks, b => b.Kind == "quote"); Assert.Contains(blocks, b => b.Kind == "table");
    }
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void CodePreservesWhitespaceNewlinesAndIndentation(string nl)
    {
        string code = "    中文  code" + nl + "\tsecond line" + nl;
        var block = Assert.Single(MarkdownView.Parse("```csharp" + nl + code + "```" + nl));
        Assert.Equal("code", block.Kind); Assert.Equal("csharp", block.Language); Assert.Equal(code, block.Text);
    }
    [Fact] public void EveryStreamingPrefixParsesAndUnclosedFencePreservesPartialText()
    {
        const string source = "# 中英\n**bold**\n```python\n    print('<>& 中文')\n```\n| A | B |\n| --- | --- |\n| 1 | 2 |";
        for (int i = 0; i <= source.Length; i++) MarkdownView.Parse(source[..i]);
        Assert.Equal("  unfinished", Assert.Single(MarkdownView.Parse("```js\n  unfinished")).Text);
    }
    [Theory]
    [InlineData("file:///C:/Windows/a.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("https://example.com", true)]
    [InlineData("http://localhost", true)]
    public void OnlyHttpLinksAreActionable(string uri, bool safe) => Assert.Equal(safe, MarkdownView.IsSafeLink(uri));
    [Fact] public void WpfRendersCompleteAndIncompleteMarkdownAndLongCodeWithoutLayoutFaults()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                foreach (string input in new[] { "**粗体** *italic* `inline` [link](https://example.com)", "**未完成 <script>&", "| A | B |\n| --- | --- |\n| 中文 | value |", "```cs\n" + string.Concat(Enumerable.Repeat("    code();\n", 2000)) })
                { var view = MarkdownView.Render(input); view.Measure(new Size(280, double.PositiveInfinity)); Assert.True(double.IsFinite(view.DesiredSize.Height)); }
                var panel = (StackPanel)MarkdownView.Render("**bold** *italic* `inline`");
                var text = Assert.IsType<TextBlock>(panel.Children[0]);
                Assert.Contains(text.Inlines.Cast<Inline>(), i => i is Bold); Assert.Contains(text.Inlines.Cast<Inline>(), i => i is Italic);
            }
            catch (Exception e) { error = e; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error is not null) throw error;
    }
}
