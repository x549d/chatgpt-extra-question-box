using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ContextNotes.SelectionProbe;

internal readonly record struct WindowIdentity(nint Handle, uint ProcessId)
{
    public bool IsEmpty => Handle == 0 || ProcessId == 0;
}

internal readonly record struct ScreenRectangle(double X, double Y, double Width, double Height)
{
    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y) && Width > 0 && Height > 0
        && double.IsFinite(X + Width) && double.IsFinite(Y + Height);

    public bool Contains(int x, int y) => X <= x && x < X + Width && Y <= y && y < Y + Height;

    public bool Intersects(ScreenRectangle other) =>
        Math.Max(X, other.X) < Math.Min(X + Width, other.X + other.Width)
        && Math.Max(Y, other.Y) < Math.Min(Y + Height, other.Y + other.Height);
}

internal sealed record WindowGeometry(
    uint Dpi, long Monitor, int MonitorCount, ScreenRectangle Bounds, int CursorRectangle);

internal static class NativeWindows
{
    private const string CodexPackage = "OpenAI.Codex_2p2nqsd0c76g0";
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int MonitorCountMetric = 80;
    private const int AccessDenied = 5;

    public static void UsePhysicalPixels()
    {
        nint perMonitorV2 = new(-4);
        if (!SetProcessDpiAwarenessContext(perMonitorV2)
            && !AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), perMonitorV2))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "PerMonitorV2 is required for screen coordinates.");
        }
    }

    public static WindowIdentity Foreground()
    {
        nint handle = GetForegroundWindow();
        GetWindowThreadProcessId(handle, out uint pid);
        return new WindowIdentity(handle, pid);
    }

    public static bool IsCodex(WindowIdentity window)
    {
        if (window.IsEmpty)
            return false;

        nint process = OpenProcess(ProcessQueryLimitedInformation, false, window.ProcessId);
        if (process == 0)
            return false;

        try
        {
            var package = new StringBuilder(256);
            uint packageLength = (uint)package.Capacity;
            int status = GetPackageFamilyName(process, ref packageLength, package);
            var image = new StringBuilder(1024);
            int imageLength = image.Capacity;
            if (!QueryFullProcessImageNameW(process, 0, image, ref imageLength))
                return false;

            string path = image.ToString();
            if (!string.Equals(Path.GetFileName(path), "ChatGPT.exe", StringComparison.OrdinalIgnoreCase))
                return false;
            return status == 0
                ? string.Equals(package.ToString(), CodexPackage, StringComparison.OrdinalIgnoreCase)
                : status == AccessDenied && IsProtectedCodexImage(path, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        }
        finally
        {
            CloseHandle(process);
        }
    }

    internal static bool IsProtectedCodexImage(string image, string programFiles)
    {
        if (string.IsNullOrEmpty(programFiles))
            return false;
        string prefix = Path.Combine(programFiles, "WindowsApps") + Path.DirectorySeparatorChar;
        if (!image.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string[] relative = image[prefix.Length..].Split(Path.DirectorySeparatorChar);
        if (relative is not [var folder, var app, var executable]
            || !app.Equals("app", StringComparison.OrdinalIgnoreCase)
            || !executable.Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase))
            return false;
        string[] packageParts = folder.Split('_');
        return packageParts is [var name, var version, "x64" or "arm64" or "x86", "", var publisher]
            && Version.TryParse(version, out _)
            && string.Equals(name + "_" + publisher, CodexPackage, StringComparison.OrdinalIgnoreCase);
    }

    public static WindowGeometry Geometry(WindowIdentity window, ScreenRectangle[] selection)
    {
        GetWindowRect(window.Handle, out Rect bounds);
        int cursorRectangle = GetCursorPos(out Point cursor)
            ? Array.FindIndex(selection, rectangle => rectangle.Contains(cursor.X, cursor.Y)) + 1
            : 0;
        return new WindowGeometry(GetDpiForWindow(window.Handle), MonitorFromWindow(window.Handle, 2).ToInt64(),
            GetSystemMetrics(MonitorCountMetric), new ScreenRectangle(bounds.Left, bounds.Top,
                bounds.Right - bounds.Left, bounds.Bottom - bounds.Top), cursorRectangle);
    }

    public static System.Windows.Point? CursorInWindow(WindowIdentity window)
    {
        if (!GetCursorPos(out Point cursor) || GetAncestor(WindowFromPoint(cursor), 2) != window.Handle)
            return null;
        return new System.Windows.Point(cursor.X, cursor.Y);
    }

    public static bool IsOnDisplay(ScreenRectangle rectangle)
    {
        var bounds = new Rect
        {
            Left = (int)Math.Floor(rectangle.X),
            Top = (int)Math.Floor(rectangle.Y),
            Right = (int)Math.Ceiling(rectangle.X + rectangle.Width),
            Bottom = (int)Math.Ceiling(rectangle.Y + rectangle.Height)
        };
        return MonitorFromRect(ref bounds, 0) != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(nint context);
    [DllImport("user32.dll")]
    private static extern nint GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")]
    private static extern bool AreDpiAwarenessContextsEqual(nint first, nint second);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out Rect rectangle);
    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll")]
    private static extern nint MonitorFromRect(ref Rect rectangle, uint flags);
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int metric);
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int GetPackageFamilyName(nint process, ref uint length, StringBuilder name);
    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(nint process, int flags, StringBuilder path, ref int length);
}
