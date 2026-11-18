using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ContextNotes.SelectionProbe;

internal static class UsageFormatter
{
    public static string Format(JsonElement response)
    {
        var lines = new List<string>();
        if (response.TryGetProperty("rateLimitsByLimitId", out var buckets) && buckets.ValueKind == JsonValueKind.Object)
            foreach (var b in buckets.EnumerateObject()) AddBucket(lines, b.Name, b.Value);
        else if (response.TryGetProperty("rateLimits", out var single)) AddBucket(lines, "Codex", single);
        if (response.TryGetProperty("ordinaryUsageAllowed", out var allowed) && allowed.ValueKind is JsonValueKind.True or JsonValueKind.False)
            lines.Add(allowed.GetBoolean() ? "服务端允许普通用量" : "服务端当前限制普通用量");
        return lines.Count == 0 ? "当前协议未提供可显示的限额信息。" : string.Join("\n", lines);
    }
    private static void AddBucket(List<string> lines, string name, JsonElement bucket)
    {
        if (bucket.ValueKind != JsonValueKind.Object) return;
        foreach (string key in new[] { "primary", "secondary" })
        {
            if (!bucket.TryGetProperty(key, out var window) || window.ValueKind != JsonValueKind.Object || !window.TryGetProperty("usedPercent", out var percent) || percent.ValueKind != JsonValueKind.Number || !percent.TryGetDouble(out double used)) continue;
            string duration = window.TryGetProperty("windowDurationMins", out var mins) && mins.ValueKind == JsonValueKind.Number && mins.TryGetInt64(out long minutes) ? $"{minutes} 分钟窗口" : key;
            string reset = "重置时间未提供";
            if (window.TryGetProperty("resetsAt", out var at) && at.ValueKind == JsonValueKind.Number && at.TryGetInt64(out long seconds))
            { try { reset = "重置于 " + DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().ToString("g"); } catch (ArgumentOutOfRangeException) { } }
            lines.Add($"{name} · {duration} · 已使用 {used:g}% · {reset}");
        }
        if (bucket.TryGetProperty("rateLimitReachedType", out var reached) && reached.ValueKind == JsonValueKind.String) lines.Add("服务端限额状态：" + reached.GetString());
    }
}

internal sealed class ModelWindow : Window
{
    private readonly AnnotationService service;
    private readonly AnnotationItem? annotation;
    private readonly ComboBox models = new() { DisplayMemberPath = "DisplayName", MinWidth = 250 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock usage = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private string scope = "";
    private bool closed;
    public ModelWindow(AnnotationService service, AnnotationItem? annotation = null)
    {
        this.service = service; this.annotation = annotation;
        DarkTheme.Apply(this);
        Title = "Context Notes · 模型与用量"; Width = 500; Height = 360; Background = new SolidColorBrush(Color.FromRgb(35, 36, 40)); Foreground = Brushes.Gainsboro;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(18) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "模型与用量", FontSize = 19, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(models);
        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) }; panel.Children.Add(buttons);
        AddButton(buttons, "设为账号默认", () => Save(false));
        if (annotation is not null) AddButton(buttons, "用于当前旁注", () => Save(true));
        AddButton(buttons, "刷新", async () => await Refresh());
        panel.Children.Add(status); panel.Children.Add(usage);
        Loaded += async (_, _) => await Refresh(); Closed += (_, _) => closed = true;
    }
    private static void AddButton(Panel panel, string label, Action action)
    {
        var button = new Button { Content = label, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 8, 0) };
        button.Click += (_, _) => action(); panel.Children.Add(button);
    }
    private async Task Refresh()
    {
        try
        {
            var account = await service.ReadCurrentAccountAsync();
            var catalog = await service.GetModelsAsync();
            if (closed) return;
            scope = account.ScopeFingerprint;
            models.ItemsSource = catalog.Where(m => !m.Hidden).ToArray();
            string? selected = annotation?.ModelName ?? service.GetPreferredModel(scope);
            models.SelectedItem = catalog.FirstOrDefault(m => m.Model == selected) ?? catalog.FirstOrDefault(m => m.IsDefault && !m.Hidden);
            status.Text = $"{account.DisplaySummary} · {scope}\n当前旁注：{annotation?.ModelName ?? "默认模型"}。模型仅来自 App Server 目录。";
            usage.Text = await service.ReadUsageAsync();
        }
        catch (Exception) { if (!closed) status.Text = "无法读取账号或模型目录，请检查 Codex 登录和连接。"; }
    }
    private async void Save(bool forAnnotation)
    {
        if (models.SelectedItem is not AppServerModelInfo model) return;
        try
        {
            await service.SelectModelAsync(scope, model.Model, forAnnotation ? annotation : null);
            if (!closed) status.Text = forAnnotation ? "当前旁注模型已保存，下次发送时生效。" : "账号默认模型已保存，新旁注下次发送时生效。";
        }
        catch (Exception e) { if (!closed) status.Text = e is InvalidOperationException ? e.Message : "模型设置失败，请刷新目录重试。"; }
    }
}
