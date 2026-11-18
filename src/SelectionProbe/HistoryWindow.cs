using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ContextNotes.SelectionProbe;

internal sealed class HistoryWindow : Window
{
    private readonly HistoryPersistence history;
    private readonly AnnotationService service;
    private readonly Action<HistoryRecord> restore;
    private readonly Action<string> closeAnnotation;
    private readonly TextBox search = new() { ToolTip = "搜索原文、问题、回答或标题" };
    private readonly TextBox title = new() { ToolTip = "修改选中旁注的标题" };
    private readonly ComboBox accounts = new() { MinWidth = 150 };
    private readonly ComboBox archive = new() { ItemsSource = new[] { "全部归档状态", "未归档", "已归档" }, SelectedIndex = 0 };
    private readonly CheckBox deleted = new() { Content = "回收站", VerticalAlignment = VerticalAlignment.Center };
    private readonly DatePicker from = new() { ToolTip = "创建日期（起）", Width = 120 };
    private readonly DatePicker to = new() { ToolTip = "创建日期（止，含当天）", Width = 120 };
    private readonly ListBox list = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock notice = new() { TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private string currentScope = "";
    private int queryVersion;
    private int page;
    private bool closed;

    public HistoryWindow(HistoryPersistence history, AnnotationService service, Action<HistoryRecord> restore, Action<string> closeAnnotation)
    {
        this.history = history; this.service = service; this.restore = restore;
        this.closeAnnotation = closeAnnotation;
        Title = "Context Notes · 旁注历史"; Width = 760; Height = 540; MinWidth = 620; MinHeight = 440;
        Background = new SolidColorBrush(Color.FromRgb(35, 36, 40)); Foreground = Brushes.Gainsboro;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel { Margin = new Thickness(16) }; Content = root;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "旁注历史", FontSize = 20, Margin = new Thickness(0, 0, 0, 10) });
        top.Children.Add(search);
        var filters = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        foreach (var c in new FrameworkElement[] { accounts, archive, from, to, deleted }) { c.Margin = new Thickness(0, 0, 8, 0); filters.Children.Add(c); }
        top.Children.Add(filters);
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        bottom.Children.Add(title);
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) }; bottom.Children.Add(actions);
        AddButton(actions, "打开旁注", RestoreSelected);
        AddButton(actions, "保存标题", async () => { if (Selected is { } r) { string text = title.Text; await Mutate(() => history.Database.Rename(r.Id, r.AccountScope, text)); } });
        AddButton(actions, "归档 / 取消", () => ChangeFlags(archiveAction: true));
        AddButton(actions, "删除 / 恢复", () => ChangeFlags(archiveAction: false));
        AddButton(actions, "清空回收站", Purge);
        AddButton(actions, "备份数据库", Backup);
        AddButton(actions, "上一页", async () => { page = Math.Max(0, page - 1); await Reload(); });
        AddButton(actions, "下一页", async () => { if (list.Items.Count == 50) page++; await Reload(); });
        bottom.Children.Add(notice);
        root.Children.Add(list);
        list.SelectionChanged += (_, _) => title.Text = Selected?.Title ?? "";
        list.MouseDoubleClick += (_, _) => RestoreSelected();
        search.TextChanged += (_, _) => Schedule(); accounts.SelectionChanged += (_, _) => Schedule(); archive.SelectionChanged += (_, _) => Schedule();
        deleted.Click += (_, _) => Schedule(); from.SelectedDateChanged += (_, _) => Schedule(); to.SelectedDateChanged += (_, _) => Schedule();
        debounce.Tick += async (_, _) => { debounce.Stop(); await Reload(); };
        Loaded += async (_, _) => await Initialize();
        Closed += (_, _) => { closed = true; queryVersion++; debounce.Stop(); };
    }
    private HistoryRecord? Selected => (list.SelectedItem as ListBoxItem)?.Tag as HistoryRecord;
    private static void AddButton(Panel panel, string text, Action click)
    {
        var button = new Button { Content = text, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 6, 6) };
        button.Click += (_, _) => click(); panel.Children.Add(button);
    }
    private void Schedule() { page = 0; debounce.Stop(); debounce.Start(); }
    private async Task Initialize()
    {
        try
        {
            var account = await service.ReadCurrentAccountAsync();
            if (closed) return;
            currentScope = account.Status == AppServerLoginStatus.LoggedIn ? account.ScopeFingerprint : "";
            var scopes = await Task.Run(() => history.Database.AccountScopes());
            if (closed) return;
            accounts.ItemsSource = scopes.Append(currentScope).Distinct().ToArray(); accounts.SelectedItem = currentScope;
            notice.Text = $"当前账号：{account.DisplaySummary} · {currentScope}。每页 50 条；旧账号历史可浏览，继续追问须匹配当前账号。";
            await Reload();
        }
        catch (Exception) { if (!closed) notice.Text = "无法读取当前账号，请检查 Codex 登录后重新打开历史。"; }
    }
    private async Task Reload()
    {
        if (closed || accounts.SelectedItem is not string scope) return;
        int version = ++queryVersion;
        string term = search.Text; int filter = archive.SelectedIndex - 1; bool trash = deleted.IsChecked == true;
        DateTimeOffset? start = from.SelectedDate is { } f ? new DateTimeOffset(f) : null;
        DateTimeOffset? end = to.SelectedDate is { } t ? new DateTimeOffset(t.AddDays(1)) : null;
        int offset = page * 50;
        try
        {
            var records = await Task.Run(() => { history.Flush(); return history.Database.Search(scope, term, start, end, filter, trash, 50, offset); });
            if (closed || version != queryVersion) return;
            list.Items.Clear();
            foreach (var r in records)
            {
                var panel = new StackPanel { Margin = new Thickness(4) };
                panel.Children.Add(new TextBlock { Text = r.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                panel.Children.Add(new TextBlock { Text = r.SelectedText.ReplaceLineEndings(" "), MaxHeight = 34, TextWrapping = TextWrapping.Wrap });
                panel.Children.Add(new TextBlock { Text = $"创建 {r.CreatedAt.LocalDateTime:g} · 更新 {r.UpdatedAt.LocalDateTime:g} · {r.Messages.Length} 消息 · {r.AccountScope} · {r.Model ?? "默认模型"} · {(r.Archived ? "已归档" : "未归档")}", FontSize = 10, Opacity = 0.7 });
                list.Items.Add(new ListBoxItem { Tag = r, Content = panel });
            }
            if (history.LastFailure is { } failure) notice.Text = failure;
        }
        catch (Exception) { if (!closed) notice.Text = "无法读取历史，原数据库已保留。"; }
    }
    private async void RestoreSelected()
    {
        if (Selected is not { } r || r.Deleted) return;
        try
        {
            var account = await service.ReadCurrentAccountAsync();
            if (closed) return;
            if (account.Status != AppServerLoginStatus.LoggedIn || r.AccountScope != account.ScopeFingerprint)
            { notice.Text = "账号不匹配，请在 Codex 切换至该旁注所属账号后恢复。"; return; }
            restore(r);
        }
        catch (Exception) { notice.Text = "无法校验账号，未打开旁注。"; }
    }
    private async void ChangeFlags(bool archiveAction)
    {
        if (Selected is not { } r) return;
        if (!archiveAction && !r.Deleted && MessageBox.Show(this, "将此旁注移入回收站？可从回收站恢复。", "删除旁注", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        closeAnnotation(r.Id);
        await Mutate(() => history.Database.SetFlags(r.Id, r.AccountScope, archiveAction ? !r.Archived : r.Archived, archiveAction ? r.Deleted : !r.Deleted));
    }
    private async void Purge()
    {
        if (accounts.SelectedItem is not string scope) return;
        if (MessageBox.Show(this, "永久删除此账号回收站中的所有旁注？操作不可撤销。既有备份和 App Server 历史仍单独保留。", "永久删除", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await Mutate(() => history.Database.PurgeDeleted(scope, true));
    }
    private async Task Mutate(Action action)
    {
        try { await Task.Run(() => { history.Flush(); action(); }); await Reload(); }
        catch (Exception) { if (!closed) notice.Text = "修改历史失败，原数据已保留。"; }
    }
    private async void Backup()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "SQLite backup|*.db", FileName = "context-notes-backup.db", InitialDirectory = Path.GetDirectoryName(history.Database.FilePath) };
        if (dialog.ShowDialog(this) != true) return;
        try { await Task.Run(() => { history.Flush(); history.Database.Backup(dialog.FileName); }); notice.Text = "数据库备份完成。备份含对话内容，请妥善保存。"; }
        catch (Exception) { notice.Text = "备份失败，原数据库未修改。"; }
    }
}
