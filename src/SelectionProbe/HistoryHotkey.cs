using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ContextNotes.SelectionProbe;

internal sealed class HistoryHotkey : IDisposable
{
    private readonly HwndSource source = new(new HwndSourceParameters("Context Notes History Shortcut") { ParentWindow = new nint(-3) });
    private readonly Action action;
    public HistoryHotkey(Action action)
    {
        this.action = action;
        source.AddHook(Hook);
        if (!RegisterHotKey(source.Handle, 51, 0x4003, 0x48)) Console.WriteLine("event=history_shortcut_unavailable");
    }
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam == 51) { handled = true; action(); }
        return 0;
    }
    public void Dispose() { UnregisterHotKey(source.Handle, 51); source.Dispose(); }
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint window, int id);
}
