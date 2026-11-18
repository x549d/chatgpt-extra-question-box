using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ContextNotes.SelectionProbe;

internal partial class AnnotationWindow : Window
{
    private const int ExtendedStyle = -20;
    private const int ToolWindow = 0x80;
    private const uint NoActivate = 0x0010;
    private const uint NoSize = 0x0001;
    private const uint NoZOrder = 0x0004;
    private static readonly nint TopmostWindow = new(-1);

    private const double ExpandedWidthDip = 324;
    private const double CollapsedWidthDip = 224;

    private bool cleanedUp;

    public AnnotationWindow(AnnotationItem item, AnnotationService? service = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        Item = item;
        Service = service;
        InitializeComponent();

        // Bind exact verbatim selected text (preserves newlines, indentation, consecutive spaces, CJK/English).
        QuoteTextBlock.Text = item.SelectedText;
        InputBox.Text = item.InputDraft;
        UpdatePlaceholderText();
        ApplyQuoteState(item.QuoteExpandedState);
        ApplyExpandedState(item.ExpandedState);
        RefreshConversationUi();

        InputBox.TextChanged += OnInputTextChanged;
        InputBox.KeyDown += OnInputKeyDown;
        InputBox.GotKeyboardFocus += OnInputGotKeyboardFocus;
        InputBox.LostKeyboardFocus += OnInputLostKeyboardFocus;
        Closed += OnWindowClosed;
    }

    public AnnotationItem Item { get; }
    public AnnotationService? Service { get; set; }

    public TextBox InputControl => InputBox;
    public TextBlock QuoteControl => QuoteTextBlock;
    public Border QuoteContainer => QuoteBorder;
    public StackPanel BodyContainer => BodyPanel;
    public TextBlock StatusControl => StatusText;
    public Border ConversationContainer => ConversationBorder;
    public StackPanel MessagesContainer => MessagesPanel;
    public Button SendButtonControl => SendButton;
    public Button StopButtonControl => StopButton;

    public Action<AnnotationWindow, string>? DiagnosticLogged { get; set; }
    public Action<AnnotationWindow>? ClosedByUser { get; set; }
    public Action<AnnotationWindow, string>? LayoutChanged { get; set; }

    public nint Handle => new WindowInteropHelper(this).EnsureHandle();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        nint hwnd = new WindowInteropHelper(this).Handle;
        int style = GetWindowLongW(hwnd, ExtendedStyle);
        Marshal.SetLastPInvokeError(0);
        // Set WS_EX_TOOLWINDOW so the inline popup never clutters Alt+Tab or taskbar,
        // while omitting WS_EX_NOACTIVATE so InputBox receives real keyboard and IME focus.
        int previous = SetWindowLongW(hwnd, ExtendedStyle, style | ToolWindow);
        int error = Marshal.GetLastWin32Error();
        if (previous == 0 && error != 0)
            throw new Win32Exception(error);

        if (Item.OwnerWindow.Handle != 0)
            NativeWindows.SetOwner(hwnd, Item.OwnerWindow.Handle);
    }

    public bool StageOn(DisplayArea display) =>
        SetWindowPos(Handle, 0, (int)display.WorkArea.X, (int)display.WorkArea.Y, 0, 0, NoActivate | NoSize | NoZOrder);

    public (int WidthPx, int HeightPx) MeasurePhysicalSize(double scaleX, double scaleY)
    {
        double targetWidthDip = Item.ExpandedState ? ExpandedWidthDip : CollapsedWidthDip;
        Width = targetWidthDip;
        Measure(new Size(targetWidthDip, double.PositiveInfinity));
        double desiredHeightDip = Math.Max(42, DesiredSize.Height);
        return (
            (int)Math.Ceiling(targetWidthDip * scaleX),
            (int)Math.Ceiling(desiredHeightDip * scaleY));
    }

    public bool Place(ScreenRectangle rectangle, bool activate = false)
    {
        uint flags = activate ? 0u : NoActivate;
        bool ok = SetWindowPos(Handle, TopmostWindow, (int)Math.Round(rectangle.X), (int)Math.Round(rectangle.Y),
            (int)Math.Round(rectangle.Width), (int)Math.Round(rectangle.Height), flags);
        if (ok)
        {
            ScreenRectangle actual = NativeWindows.WindowBounds(Handle);
            Item.WindowPosition = actual.IsValid ? actual : rectangle;
        }
        return ok;
    }

    public bool FocusInput()
    {
        if (Item.IsClosed || !Item.ExpandedState)
            return false;

        nint hwnd = Handle;
        _ = NativeWindows.ActivateWindow(hwnd, Item.OwnerWindow.Handle);
        _ = Activate();
        _ = Focus();
        bool focused = InputBox.Focus();
        IInputElement? kbFocused = Keyboard.Focus(InputBox);
        InputBox.CaretIndex = InputBox.Text.Length;

        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (Item.IsClosed || !IsVisible || !Item.ExpandedState)
                return;
            _ = Activate();
            _ = InputBox.Focus();
            _ = Keyboard.Focus(InputBox);
            InputBox.CaretIndex = InputBox.Text.Length;
        }));

        return focused || ReferenceEquals(kbFocused, InputBox) || InputBox.IsKeyboardFocused;
    }

    public void SetExpanded(bool expanded)
    {
        if (Item.ExpandedState == expanded)
            return;
        Item.ExpandedState = expanded;
        Item.Persist?.Invoke(Item, false);
        ApplyExpandedState(expanded);
        UpdateLayout();
        DiagnosticLogged?.Invoke(this,
            $"annotation_collapsed annotation_id={Item.AnnotationId} collapsed={!expanded}");
        LayoutChanged?.Invoke(this, expanded ? "expanded" : "collapsed");
        if (expanded && IsVisible)
            _ = FocusInput();
    }

    public void SetQuoteExpanded(bool expanded)
    {
        if (Item.QuoteExpandedState == expanded)
            return;
        Item.QuoteExpandedState = expanded;
        Item.Persist?.Invoke(Item, false);
        ApplyQuoteState(expanded);
        UpdateLayout();
        DiagnosticLogged?.Invoke(this,
            $"annotation_quote_collapsed annotation_id={Item.AnnotationId} quote_collapsed={!expanded}");
        LayoutChanged?.Invoke(this, expanded ? "quote_expanded" : "quote_collapsed");
    }

    public void SubmitDraft(string trigger = "button")
    {
        if (Item.IsClosed || Item.IsBusy)
            return;

        string draft = InputBox.Text;
        Item.InputDraft = draft;
        if (string.IsNullOrWhiteSpace(draft))
        {
            Item.LastError = "请输入你的问题后再发送。";
            Item.StatusNotice = Item.LastError;
            RefreshConversationUi();
            return;
        }

        Item.SubmittedDraft = draft;
        DiagnosticLogged?.Invoke(this,
            $"annotation_question_submitted annotation_id={Item.AnnotationId} trigger={trigger} draft_length={draft.Length} selected_text_length={Item.SelectedText.Length}");

        if (Service is not null)
        {
            // Clear the input box for the next follow-up turn while keeping SubmittedDraft & Messages populated.
            InputBox.Text = "";
            Item.InputDraft = "";
            _ = Service.AskAsync(Item, draft, RefreshFromBackground);
            return;
        }

        // Offline fallback when no AnnotationService is attached (e.g. standalone window unit tests).
        Item.StatusNotice = "已保存在当前旁注（Phase 4 服务未挂载）";
        ApplyStatusNotice(Item.StatusNotice, isError: false);
        UpdateLayout();
        LayoutChanged?.Invoke(this, "question_staged");
    }

    public void CancelGeneration(string trigger = "stop_button")
    {
        if (Item.IsClosed || Service is null)
            return;
        DiagnosticLogged?.Invoke(this,
            $"annotation_cancel_requested annotation_id={Item.AnnotationId} trigger={trigger}");
        _ = Service.CancelTurnAsync(Item);
    }

    public void RefreshFromBackground()
    {
        if (Item.IsClosed || cleanedUp)
            return;
        if (Dispatcher.CheckAccess())
        {
            RefreshConversationUi();
        }
        else
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                if (!Item.IsClosed && !cleanedUp)
                    RefreshConversationUi();
            }));
        }
    }

    public void RefreshConversationUi()
    {
        if (Item.IsClosed || cleanedUp)
            return;

        IReadOnlyList<AnnotationMessage> messages = Item.SnapshotMessages();
        bool hasConversation = messages.Count > 0 || Item.GenerationState != AnnotationGenerationState.Idle;
        ConversationBorder.Visibility = hasConversation ? Visibility.Visible : Visibility.Collapsed;

        if (!string.IsNullOrWhiteSpace(Item.ModelName))
        {
            ModelBadge.Text = $"· {Item.ModelName}";
            ModelBadge.Visibility = Visibility.Visible;
        }

        PromptLabel.Text = messages.Count > 0 ? "继续追问：" : "关于这段内容，你想问什么？";
        UpdatePlaceholderText();

        bool busy = Item.IsBusy;
        StopButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        SendButton.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        SendButton.IsEnabled = !busy;

        RenderMessages(messages);

        string? status = Item.GenerationState switch
        {
            AnnotationGenerationState.Connecting => Item.StatusNotice ?? "正在连接 Codex App Server...",
            AnnotationGenerationState.Generating => Item.StatusNotice ?? "AI 正在生成...",
            AnnotationGenerationState.Failed => Item.LastError ?? Item.StatusNotice ?? "请求失败，请重试。",
            AnnotationGenerationState.Cancelled => Item.StatusNotice ?? "已停止生成",
            _ => Item.StatusNotice
        };
        bool isError = Item.GenerationState == AnnotationGenerationState.Failed;
        ApplyStatusNotice(status, isError);

        UpdateLayout();
        if (hasConversation)
            ConversationScrollViewer.ScrollToEnd();
        LayoutChanged?.Invoke(this, "conversation_updated");
    }

    private void RenderMessages(IReadOnlyList<AnnotationMessage> messages)
    {
        MessagesPanel.Children.Clear();
        for (int i = 0; i < messages.Count; i++)
        {
            AnnotationMessage msg = messages[i];
            bool isUser = msg.Role == "user";

            var itemBorder = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(isUser ? Color.FromRgb(0x25, 0x28, 0x32) : Color.FromRgb(0x16, 0x17, 0x1A)),
                BorderBrush = new SolidColorBrush(isUser ? Color.FromRgb(0x3B, 0x42, 0x54) : Color.FromRgb(0x2E, 0x32, 0x3B)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 5, 8, 6),
                Margin = new Thickness(0, i == 0 ? 0 : 6, 0, 0)
            };

            var stack = new StackPanel();
            string roleHeader = isUser ? "你：" : "AI：";
            if (!isUser && msg.State == AnnotationGenerationState.Generating)
                roleHeader = "AI（正在生成）：";
            else if (!isUser && msg.State == AnnotationGenerationState.Cancelled)
                roleHeader = "AI（已停止）：";
            else if (!isUser && msg.State == AnnotationGenerationState.Failed)
                roleHeader = "AI（未完成）：";

            var headerBlock = new TextBlock
            {
                Text = roleHeader,
                Foreground = new SolidColorBrush(isUser ? Color.FromRgb(0x93, 0xC5, 0xFD) : Color.FromRgb(0x10, 0xA3, 0x7F)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
                Margin = new Thickness(0, 0, 0, 2)
            };

            string bodyText = string.IsNullOrEmpty(msg.Text) && msg.State == AnnotationGenerationState.Generating
                ? "…"
                : msg.Text;
            var bodyBlock = new TextBlock
            {
                Text = bodyText,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)),
                FontSize = 12,
                FontFamily = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI, Segoe UI"),
                TextWrapping = TextWrapping.Wrap
            };

            stack.Children.Add(headerBlock);
            stack.Children.Add(bodyBlock);
            itemBorder.Child = stack;
            MessagesPanel.Children.Add(itemBorder);
        }
    }

    private void UpdatePlaceholderText()
    {
        PlaceholderText.Text = Item.Messages.Count > 0 ? "输入后续问题……" : "输入问题……";
        PlaceholderText.Visibility = string.IsNullOrEmpty(InputBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyExpandedState(bool expanded)
    {
        Width = expanded ? ExpandedWidthDip : CollapsedWidthDip;
        BodyPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        CollapsedBadge.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        CollapseButton.Content = expanded ? "—" : "▢";
        CollapseButton.ToolTip = expanded ? "折叠旁注" : "展开旁注";
    }

    private void ApplyQuoteState(bool expanded)
    {
        QuoteBorder.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        QuoteToggleButton.Content = expanded ? "收起" : "展开";
    }

    private void ApplyStatusNotice(string? notice, bool isError = false)
    {
        if (string.IsNullOrWhiteSpace(notice))
        {
            StatusText.Text = "";
            StatusText.Visibility = Visibility.Collapsed;
        }
        else
        {
            StatusText.Text = notice;
            StatusText.Foreground = new SolidColorBrush(
                isError ? Color.FromRgb(0xF8, 0x71, 0x71) : Color.FromRgb(0x9C, 0xA3, 0xAF));
            StatusText.Visibility = Visibility.Visible;
        }
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        Item.InputDraft = InputBox.Text;
        Item.Persist?.Invoke(Item, false);
        UpdatePlaceholderText();
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        // Do not intercept Enter while an IME composition is active (Key.ImeProcessed).
        if (e.Key != Key.Return || e.ImeProcessedKey != Key.None)
            return;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            e.Handled = true;
            int caret = InputBox.SelectionStart;
            int selLen = InputBox.SelectionLength;
            string current = InputBox.Text;
            InputBox.Text = current[..caret] + "\n" + current[(caret + selLen)..];
            InputBox.SelectionStart = caret + 1;
            InputBox.SelectionLength = 0;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            SubmitDraft("enter_key");
        }
    }

    private void OnInputGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        InputContainer.BorderBrush = new SolidColorBrush(Color.FromRgb(0x10, 0xA3, 0x7F));
        DiagnosticLogged?.Invoke(this,
            $"focus_acquired annotation_id={Item.AnnotationId} keyboard_focused={InputBox.IsKeyboardFocused}");
    }

    private void OnInputLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        InputContainer.BorderBrush = new SolidColorBrush(Color.FromRgb(0x4E, 0x52, 0x5E));
        DiagnosticLogged?.Invoke(this,
            $"focus_lost annotation_id={Item.AnnotationId}");
    }

    private void OnToggleExpand(object sender, RoutedEventArgs e) => SetExpanded(!Item.ExpandedState);

    private void OnToggleQuote(object sender, RoutedEventArgs e) => SetQuoteExpanded(!Item.QuoteExpandedState);

    private void OnSendClick(object sender, RoutedEventArgs e)
    {
        SubmitDraft("send_button");
        _ = FocusInput();
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        CancelGeneration("stop_button");
        _ = FocusInput();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseByUser();

    public void CloseByUser()
    {
        if (Item.IsClosed)
            return;
        Item.IsClosed = true;
        Service?.NotifyAnnotationClosed(Item);
        ClosedByUser?.Invoke(this);
        Close();
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
            return;
        try
        {
            DragMove();
            ClampAfterManualDrag();
        }
        catch (InvalidOperationException)
        {
            // DragMove can throw if the mouse button was released before the move loop started.
        }
    }

    public void ClampAfterManualDrag()
    {
        nint hwnd = Handle;
        ScreenRectangle current = NativeWindows.WindowBounds(hwnd);
        if (!current.IsValid)
            return;

        nint monitor = NativeWindows.WindowDisplay(hwnd);
        DisplayArea display = NativeWindows.Displays().FirstOrDefault(d => d.Handle == monitor);
        ScreenRectangle area = display.WorkArea.IsValid ? display.WorkArea : current;
        ScreenRectangle clamped = OverlayPlacement.ClampToArea(current, area);
        if (clamped != current)
            _ = Place(clamped, activate: false);

        ScreenRectangle ownerBounds = Item.OwnerWindow.Handle != 0
            ? NativeWindows.WindowBounds(Item.OwnerWindow.Handle)
            : Item.LastOwnerBounds;
        Item.RecordManualMove(clamped, ownerBounds);
        Item.Persist?.Invoke(Item, false);
        DiagnosticLogged?.Invoke(this, FormattableString.Invariant(
            $"position_updated annotation_id={Item.AnnotationId} reason=user_drag px=[{clamped.X:0},{clamped.Y:0},{clamped.Width:0},{clamped.Height:0}]"));
    }

    private void OnWindowClosed(object? sender, EventArgs e) => CleanupResources();

    private void CleanupResources()
    {
        if (cleanedUp)
            return;
        cleanedUp = true;
        Item.IsClosed = true;
        Service?.NotifyAnnotationClosed(Item);
        InputBox.TextChanged -= OnInputTextChanged;
        Item.Persist?.Invoke(Item, true);
        InputBox.KeyDown -= OnInputKeyDown;
        InputBox.GotKeyboardFocus -= OnInputGotKeyboardFocus;
        InputBox.LostKeyboardFocus -= OnInputLostKeyboardFocus;
        Closed -= OnWindowClosed;
    }

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern int GetWindowLongW(nint window, int index);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern int SetWindowLongW(nint window, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
