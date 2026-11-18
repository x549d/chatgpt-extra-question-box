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

    private const double ExpandedWidthDip = 304;
    private const double CollapsedWidthDip = 224;

    private bool cleanedUp;

    public AnnotationWindow(AnnotationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Item = item;
        InitializeComponent();

        // Bind exact verbatim selected text (preserves newlines, indentation, consecutive spaces, CJK/English).
        QuoteTextBlock.Text = item.SelectedText;
        InputBox.Text = item.InputDraft;
        PlaceholderText.Visibility = string.IsNullOrEmpty(item.InputDraft) ? Visibility.Visible : Visibility.Collapsed;
        ApplyStatusNotice(item.StatusNotice);
        ApplyQuoteState(item.QuoteExpandedState);
        ApplyExpandedState(item.ExpandedState);

        InputBox.TextChanged += OnInputTextChanged;
        InputBox.KeyDown += OnInputKeyDown;
        InputBox.GotKeyboardFocus += OnInputGotKeyboardFocus;
        InputBox.LostKeyboardFocus += OnInputLostKeyboardFocus;
        Closed += OnWindowClosed;
    }

    public AnnotationItem Item { get; }

    public TextBox InputControl => InputBox;
    public TextBlock QuoteControl => QuoteTextBlock;
    public Border QuoteContainer => QuoteBorder;
    public StackPanel BodyContainer => BodyPanel;
    public TextBlock StatusControl => StatusText;

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
        ApplyQuoteState(expanded);
        UpdateLayout();
        DiagnosticLogged?.Invoke(this,
            $"annotation_quote_collapsed annotation_id={Item.AnnotationId} quote_collapsed={!expanded}");
        LayoutChanged?.Invoke(this, expanded ? "quote_expanded" : "quote_collapsed");
    }

    public void SubmitDraft(string trigger = "button")
    {
        string draft = InputBox.Text;
        Item.InputDraft = draft;
        if (string.IsNullOrWhiteSpace(draft))
        {
            Item.StatusNotice = "请输入你的问题后再提交。";
            ApplyStatusNotice(Item.StatusNotice);
            UpdateLayout();
            return;
        }

        Item.SubmittedDraft = draft;
        Item.StatusNotice = "已保存在当前旁注（AI 实时回答将在 Phase 4 接入）";
        ApplyStatusNotice(Item.StatusNotice);
        UpdateLayout();
        DiagnosticLogged?.Invoke(this,
            $"annotation_question_staged annotation_id={Item.AnnotationId} trigger={trigger} draft_length={draft.Length} selected_text_length={Item.SelectedText.Length}");
        LayoutChanged?.Invoke(this, "question_staged");
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

    private void ApplyStatusNotice(string? notice)
    {
        if (string.IsNullOrWhiteSpace(notice))
        {
            StatusText.Text = "";
            StatusText.Visibility = Visibility.Collapsed;
        }
        else
        {
            StatusText.Text = notice;
            StatusText.Visibility = Visibility.Visible;
        }
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        Item.InputDraft = InputBox.Text;
        PlaceholderText.Visibility = string.IsNullOrEmpty(InputBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        // Do not intercept Enter while an IME composition is active (Key.ImeProcessed).
        if (e.Key == Key.Return && e.ImeProcessedKey == Key.None && Keyboard.Modifiers == ModifierKeys.None)
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

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseByUser();

    public void CloseByUser()
    {
        if (Item.IsClosed)
            return;
        Item.IsClosed = true;
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
        InputBox.TextChanged -= OnInputTextChanged;
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
