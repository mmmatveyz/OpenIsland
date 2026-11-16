using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace OpenIsland;

static class Native
{
    public enum MediaKey { VolumeDown = 9, VolumeUp = 10, NextTrack = 11, PreviousTrack = 12 }

    const int GWL_STYLE = -16;
    const int GWL_EXSTYLE = -20;
    const long WS_MAXIMIZE = 0x01000000;
    const long WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000;
    const long WS_EX_TOOLWINDOW = 0x00000080;
    const long WS_EX_NOACTIVATE = 0x08000000;
    const long WS_EX_TRANSPARENT = 0x00000020, WS_EX_LAYERED = 0x00080000, WS_EX_TOPMOST = 0x00000008;
    const long WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    const long WS_POPUP = 0x80000000;
    const uint LWA_ALPHA = 2;
    const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;
    const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
    const int MDT_EFFECTIVE_DPI = 0;
    const double DefaultDpi = 96;
    const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    const uint MONITOR_DEFAULTTONEAREST = 2;
    const uint MONITORINFOF_PRIMARY = 1;
    const int VK_CONTROL = 0x11;
    const int HSHELL_APPCOMMAND = 12;
    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;
    const uint VK_O = 0x4F;
    const ulong WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED = 0x0D83063EA3BF1C75;
    const byte AC_LINE_ONLINE = 1, BATTERY_FLAG_NO_BATTERY = 128, BATTERY_FLAG_UNKNOWN = 255, BATTERY_PERCENT_MAX = 100;
    const int ClassNameCapacity = 64;
    const int DWMWA_USE_HOSTBACKDROPBRUSH = 17;
    const int DQTYPE_THREAD_CURRENT = 2, DQTAT_COM_STA = 2;
    static readonly IntPtr HWND_TOPMOST = new(-1);
    static bool _dispatcherQueueStarted;
    static readonly int ShellMessage = (int)RegisterWindowMessage("SHELLHOOK");
    public static readonly int TrayCallback = (int)RegisterWindowMessage("OpenIsland.TrayCallback");

    const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10;
    const uint NIIF_INFO = 1;
    public const int WM_NULL = 0, WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONUP = 0x0205, WM_CONTEXTMENU = 0x007B;
    public const int WM_CLIPBOARDUPDATE = 0x031D;
    public const uint MF_STRING = 0, MF_SEPARATOR = 0x800;
    public const uint TPM_RIGHTBUTTON = 0x0002, TPM_RETURNCMD = 0x0100;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [StructLayout(LayoutKind.Sequential)]
    struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DispatcherQueueOptions { public int dwSize, threadType, apartmentType; }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowEx(uint exStyle, string className, string? title, uint style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("CoreMessaging.dll")]
    static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);

    [DllImport("kernel32.dll")]
    static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    static extern bool RegisterShellHookWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    /// <summary>Подписка на изменения буфера обмена (сообщение <see cref="WM_CLIPBOARDUPDATE"/>).</summary>
    public static bool WatchClipboard(IntPtr hwnd, bool on)
        => on ? AddClipboardFormatListener(hwnd) : RemoveClipboardFormatListener(hwnd);

    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint RegisterWindowMessage(string name);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint count);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool AppendMenu(IntPtr menu, uint flags, IntPtr id, string? text);

    [DllImport("user32.dll")]
    static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hwnd, int message, IntPtr w, IntPtr l);

    [DllImport("ntdll.dll")]
    static extern int NtQueryWnfStateData(ref ulong name, IntPtr type, IntPtr scope, out uint stamp, out int data, ref uint size);

    public static IntPtr ForegroundWindow => GetForegroundWindow();

    public static void BringForward(IntPtr hwnd) => SetForegroundWindow(hwnd);

    public static Point CursorPosition => GetCursorPos(out POINT at) ? new Point(at.X, at.Y) : default;

    public static bool IsCtrlDown => GetAsyncKeyState(VK_CONTROL) < 0;

    public static bool RegisterShellHook(IntPtr hwnd) => RegisterShellHookWindow(hwnd);

    /// <summary>Регистрирует глобальный хоткей Ctrl+Alt+O (без автоповтора).</summary>
    public static bool RegisterOpenCodeHotkey(IntPtr hwnd, int id) =>
        RegisterHotKey(hwnd, id, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_O);

    public static void UnregisterOpenCodeHotkey(IntPtr hwnd, int id) => UnregisterHotKey(hwnd, id);

    public static bool IsHotkeyMessage(int message, IntPtr wParam, int id) =>
        message == WM_HOTKEY && wParam.ToInt64() == id;

    public static MediaKey? MediaKeyOf(int message, IntPtr wParam, IntPtr lParam) =>
        message == ShellMessage && wParam.ToInt64() == HSHELL_APPCOMMAND ? (MediaKey)((lParam.ToInt64() >> 16) & 0xFFF) : null;

    public static void HideFromTaskSwitcher(IntPtr hwnd)
    {
        long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_TOOLWINDOW));
    }

    public static void SetActivatable(IntPtr hwnd, bool activatable)
    {
        long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        if (activatable) style &= ~WS_EX_NOACTIVATE;
        else style |= WS_EX_NOACTIVATE;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
    }

    public static Int32Rect VirtualScreen => new(GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
        GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));

    static IntPtr MonitorAt(Point point) => MonitorFromPoint(new POINT { X = (int)point.X, Y = (int)point.Y }, MONITOR_DEFAULTTONEAREST);

    public static double ScaleAt(Point point) =>
        GetDpiForMonitor(MonitorAt(point), MDT_EFFECTIVE_DPI, out uint dpi, out _) == 0 && dpi > 0 ? dpi / DefaultDpi : 1;

    public static Rect WorkAreaAt(Point point)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        IntPtr monitor = MonitorAt(point);
        if (!GetMonitorInfo(monitor, ref info)) return Rect.Empty;
        RECT work = info.rcWork;
        return new Rect(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top);
    }

    public static void MoveAndSize(IntPtr hwnd, int x, int y, int width, int height) =>
        SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, SWP_NOZORDER | SWP_NOACTIVATE);

    public static IntPtr CreateOverlayWindow()
    {
        long style = WS_EX_NOREDIRECTIONBITMAP | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
        IntPtr hwnd = CreateWindowEx((uint)style, "Static", null, (uint)WS_POPUP, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        SetLayeredWindowAttributes(hwnd, 0, byte.MaxValue, LWA_ALPHA);
        return hwnd;
    }

    public static IntPtr CreateBackdropWindow()
    {
        IntPtr hwnd = CreateOverlayWindow();
        int on = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_HOSTBACKDROPBRUSH, ref on, sizeof(int));
        return hwnd;
    }

    public static void Destroy(IntPtr hwnd) => DestroyWindow(hwnd);

    public static void PlaceBehind(IntPtr hwnd, IntPtr front)
    {
        if (!GetWindowRect(front, out RECT rect)) return;
        SetWindowPos(hwnd, front, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, SWP_NOACTIVATE);
        ShowWindow(hwnd, SW_SHOWNOACTIVATE);
    }

    public static void PutBehind(IntPtr hwnd, IntPtr front) =>
        SetWindowPos(hwnd, front, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    public static void ShowQuietly(IntPtr hwnd) => ShowWindow(hwnd, SW_SHOWNOACTIVATE);

    public static void Hide(IntPtr hwnd) => ShowWindow(hwnd, SW_HIDE);

    public static void EnsureDispatcherQueue()
    {
        if (_dispatcherQueueStarted) return;
        var options = new DispatcherQueueOptions { dwSize = Marshal.SizeOf<DispatcherQueueOptions>(), threadType = DQTYPE_THREAD_CURRENT, apartmentType = DQTAT_COM_STA };
        _dispatcherQueueStarted = CreateDispatcherQueueController(options, out _) >= 0;
    }

    public static bool IsToolWindow(IntPtr hwnd) => (GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOOLWINDOW) != 0;

    public static void KeepOnTop(IntPtr hwnd) =>
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    public static bool IsForegroundFullscreen(IntPtr self)
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == self || foreground == GetDesktopWindow() || foreground == GetShellWindow()) return false;

        var className = new StringBuilder(ClassNameCapacity);
        GetClassName(foreground, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "XamlExplorerHostIslandWindow") return false;

        long style = GetWindowLongPtr(foreground, GWL_STYLE).ToInt64();
        bool framed = (style & WS_CAPTION) == WS_CAPTION || (style & WS_THICKFRAME) != 0;
        if ((style & WS_MAXIMIZE) != 0 && framed) return false;
        if (!GetWindowRect(foreground, out RECT window)) return false;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(foreground, MONITOR_DEFAULTTONEAREST), ref info)) return false;
        if ((info.dwFlags & MONITORINFOF_PRIMARY) == 0) return false;

        RECT monitor = info.rcMonitor;
        return window.Left <= monitor.Left && window.Top <= monitor.Top && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
    }

    public static bool? IsDoNotDisturbOn()
    {
        ulong name = WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED;
        uint size = sizeof(int);
        try
        {
            if (NtQueryWnfStateData(ref name, IntPtr.Zero, IntPtr.Zero, out _, out int profile, ref size) != 0) return null;
            return size >= sizeof(int) && profile != 0;
        }
        catch
        {
            return null;
        }
    }

    public static bool TryGetBattery(out int percent, out bool plugged)
    {
        percent = 0;
        plugged = false;
        if (!GetSystemPowerStatus(out SYSTEM_POWER_STATUS status)) return false;
        if (status.BatteryFlag == BATTERY_FLAG_UNKNOWN || (status.BatteryFlag & BATTERY_FLAG_NO_BATTERY) != 0
            || status.BatteryLifePercent > BATTERY_PERCENT_MAX) return false;
        percent = status.BatteryLifePercent;
        plugged = status.ACLineStatus == AC_LINE_ONLINE;
        return true;
    }

    // --- Значок в системном трее (Shell_NotifyIcon) ---

    public static IntPtr LoadSmallAppIcon()
    {
        try
        {
            if (Environment.ProcessPath is not { } exe) return IntPtr.Zero;
            var large = new IntPtr[1];
            var small = new IntPtr[1];
            if (ExtractIconEx(exe, 0, large, small, 1) == 0) return IntPtr.Zero;
            if (small[0] != IntPtr.Zero)
            {
                if (large[0] != IntPtr.Zero) DestroyIcon(large[0]);
                return small[0];
            }
            return large[0];
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    public static void FreeIcon(IntPtr icon)
    {
        if (icon != IntPtr.Zero) DestroyIcon(icon);
    }

    public static bool AddTrayIcon(IntPtr hwnd, uint id, IntPtr icon, string tip)
    {
        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = id,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = (uint)TrayCallback,
            hIcon = icon,
            szTip = tip,
            szInfo = "",
            szInfoTitle = "",
        };
        return Shell_NotifyIcon(NIM_ADD, ref data);
    }

    public static void RemoveTrayIcon(IntPtr hwnd, uint id)
    {
        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = id,
            szTip = "",
            szInfo = "",
            szInfoTitle = "",
        };
        Shell_NotifyIcon(NIM_DELETE, ref data);
    }

    /// <summary>Показывает системное уведомление через значок в трее (Windows 10/11 рисует его как тост).</summary>
    public static void ShowBalloon(IntPtr hwnd, uint id, string title, string text)
    {
        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = id,
            uFlags = NIF_INFO,
            szTip = "",
            szInfo = Truncate(text, 255),
            szInfoTitle = Truncate(title, 63),
            dwInfoFlags = NIIF_INFO,
        };
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    public static IntPtr NewMenu() => CreatePopupMenu();

    public static void MenuItem(IntPtr menu, int id, string text) => AppendMenu(menu, MF_STRING, new IntPtr(id), text);

    public static void MenuSeparator(IntPtr menu) => AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);

    public static void DeleteMenu(IntPtr menu) => DestroyMenu(menu);

    /// <summary>Показывает контекстное меню трея у курсора и возвращает id выбранного пункта (0 — отменено).</summary>
    public static int ShowTrayMenu(IntPtr hwnd, IntPtr menu)
    {
        if (!GetCursorPos(out POINT at)) return 0;
        SetForegroundWindow(hwnd);
        int command = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, at.X, at.Y, 0, hwnd, IntPtr.Zero);
        PostMessage(hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        return command;
    }
}
