using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ContextNotes.SelectionProbe;

internal partial class QuestionOverlay : Window
{
    private const int ExtendedStyle = -20;
    private const int ToolWindow = 0x80;
    private const int NoActivateStyle = 0x08000000;
    private const int MouseActivateMessage = 0x0021;
    private const int MouseNoActivate = 3;
    private const uint NoActivate = 0x0010;
    private const uint NoSize = 0x0001;
    private const uint NoZOrder = 0x0004;
    private static readonly nint TopmostWindow = new(-1);

    public QuestionOverlay() => InitializeComponent();

    public Action? Clicked { get; set; }

    public nint Handle => new WindowInteropHelper(this).EnsureHandle();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        nint window = new WindowInteropHelper(this).Handle;
        HwndSource source = HwndSource.FromHwnd(window)
            ?? throw new InvalidOperationException("The overlay HWND has no WPF source.");
        int style = GetWindowLongW(source.Handle, ExtendedStyle);
        Marshal.SetLastPInvokeError(0);
        int previous = SetWindowLongW(source.Handle, ExtendedStyle, style | ToolWindow | NoActivateStyle);
        int error = Marshal.GetLastWin32Error();
        if (previous == 0 && error != 0)
            throw new Win32Exception(error);
        source.AddHook(WindowMessage);
    }

    public bool StageOn(DisplayArea display) =>
        SetWindowPos(Handle, 0, (int)display.WorkArea.X, (int)display.WorkArea.Y, 0, 0, NoActivate | NoSize | NoZOrder);

    public bool Place(ScreenRectangle rectangle) =>
        SetWindowPos(Handle, TopmostWindow, (int)rectangle.X, (int)rectangle.Y, (int)rectangle.Width,
            (int)rectangle.Height, NoActivate);

    private void OnClick(object sender, RoutedEventArgs args) => Clicked?.Invoke();

    private static nint WindowMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == MouseActivateMessage)
        {
            handled = true;
            return MouseNoActivate;
        }
        return 0;
    }

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern int GetWindowLongW(nint window, int index);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern int SetWindowLongW(nint window, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
