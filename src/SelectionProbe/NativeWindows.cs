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
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double Area => IsValid ? Width * Height : 0;

    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y) && Width > 0 && Height > 0
        && double.IsFinite(X + Width) && double.IsFinite(Y + Height);

    public bool Contains(int x, int y) => X <= x && x < X + Width && Y <= y && y < Y + Height;

    public bool Intersects(ScreenRectangle other) =>
        Math.Max(X, other.X) < Math.Min(X + Width, other.X + other.Width)
        && Math.Max(Y, other.Y) < Math.Min(Y + Height, other.Y + other.Height);

    public ScreenRectangle Intersect(ScreenRectangle other)
    {
        double left = Math.Max(X, other.X);
        double top = Math.Max(Y, other.Y);
        return new(left, top, Math.Max(0, Math.Min(Right, other.Right) - left),
            Math.Max(0, Math.Min(Bottom, other.Bottom) - top));
    }
}

internal sealed record WindowGeometry(
    uint Dpi, long Monitor, int MonitorCount, ScreenRectangle Bounds, int CursorRectangle);

internal readonly record struct DisplayArea(nint Handle, ScreenRectangle Bounds, ScreenRectangle WorkArea);

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

    public static bool IsCodex(WindowIdentity window) => !window.IsEmpty && IsCodexProcess(window.ProcessId);

    public static bool CodexRunning()
    {
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("ChatGPT"))
        {
            using (process)
            {
                if (IsCodexProcess((uint)process.Id))
                    return true;
            }
        }
        return false;
    }

    public static bool IsCodexProcess(uint processId)
    {
        nint process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
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
        int cursorRectangle = GetCursorPos(out Point cursor)
            ? Array.FindIndex(selection, rectangle => rectangle.Contains(cursor.X, cursor.Y)) + 1
            : 0;
        return new WindowGeometry(GetDpiForWindow(window.Handle), MonitorFromWindow(window.Handle, 2).ToInt64(),
            GetSystemMetrics(MonitorCountMetric), WindowBounds(window.Handle), cursorRectangle);
    }

    public static ScreenRectangle WindowBounds(nint window) =>
        GetWindowRect(window, out Rect bounds) ? bounds.Screen : default;

    public static nint WindowDisplay(nint window) => MonitorFromWindow(window, 2);

    public static System.Windows.Point? CursorInWindow(WindowIdentity window)
    {
        if (!GetCursorPos(out Point cursor) || GetAncestor(WindowFromPoint(cursor), 2) != window.Handle)
            return null;
        return new System.Windows.Point(cursor.X, cursor.Y);
    }

    public static bool LeftButtonDown() =>
        (GetAsyncKeyState(GetSystemMetrics(23) != 0 ? 0x02 : 0x01) & 0x8000) != 0;

    public static bool PointerOverWindow(nint window) =>
        window != 0 && GetCursorPos(out Point cursor) && GetAncestor(WindowFromPoint(cursor), 2) == window;

    public static void SetOwner(nint window, nint owner)
    {
        if (window != 0 && owner != 0)
            SetWindowLongPtrW(window, -8, owner);
    }

    public static bool ActivateWindow(nint window, nint ownerWindow)
    {
        if (window == 0)
            return false;

        nint currentForeground = GetForegroundWindow();
        uint currentThread = GetCurrentThreadId();
        uint foregroundThread = currentForeground != 0 ? GetWindowThreadProcessId(currentForeground, out _) : 0;
        uint ownerThread = ownerWindow != 0 ? GetWindowThreadProcessId(ownerWindow, out _) : 0;
        bool attachedForeground = false;
        bool attachedOwner = false;
        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
                attachedForeground = AttachThreadInput(currentThread, foregroundThread, true);
            if (ownerThread != 0 && ownerThread != currentThread && ownerThread != foregroundThread)
                attachedOwner = AttachThreadInput(currentThread, ownerThread, true);

            BringWindowToTop(window);
            _ = SetForegroundWindow(window);
            _ = SetActiveWindow(window);
            _ = SetFocus(window);
        }
        finally
        {
            if (attachedOwner)
                _ = AttachThreadInput(currentThread, ownerThread, false);
            if (attachedForeground)
                _ = AttachThreadInput(currentThread, foregroundThread, false);
        }
        return GetForegroundWindow() == window;
    }

    public static void RestoreForeground(nint ownerWindow)
    {
        if (ownerWindow != 0 && IsWindow(ownerWindow))
            _ = SetForegroundWindow(ownerWindow);
    }

    public static DisplayArea[] Displays()
    {
        var displays = new List<DisplayArea>();
        EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfoW(monitor, ref info))
                displays.Add(new DisplayArea(monitor, info.Monitor.Screen, info.Work.Screen));
            return true;
        }, 0);
        return [.. displays];
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

    /// <summary>Mandatory integrity RID of the current process token (8192 = medium, 4096 = low).</summary>
    public static int IntegrityLevel()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x0008, out nint token))
            return -1;
        try
        {
            GetTokenInformation(token, 25, 0, 0, out int length);
            if (length <= 0)
                return -1;
            nint buffer = Marshal.AllocHGlobal(length);
            try
            {
                if (!GetTokenInformation(token, 25, buffer, length, out _))
                    return -1;
                nint sid = Marshal.ReadIntPtr(buffer);
                int count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                return count == 0 ? -1 : Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly ScreenRectangle Screen => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public uint Size; public Rect Monitor, Work; public uint Flags; }

    private delegate bool MonitorCallback(nint monitor, nint dc, nint bounds, nint data);

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
    private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")]
    private static extern nint SetActiveWindow(nint window);
    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint window);
    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(nint window);
    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool enable);
    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern nint SetWindowLongPtrW(nint window, int index, nint newLong);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
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
    private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
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
    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(nint token, int informationClass, nint buffer, int length, out int returned);
    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthorityCount(nint sid);
    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthority(nint sid, uint index);
    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int GetPackageFamilyName(nint process, ref uint length, StringBuilder name);
    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(nint process, int flags, StringBuilder path, ref int length);
}
