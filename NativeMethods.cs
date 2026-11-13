using System.Runtime.InteropServices;

namespace NetworkTracker;

internal static class NativeMethods
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;

    public const int WmNcLButtonDown = 0x00A1;
    public const int HtCaption = 0x02;
    public const int WmMouseActivate = 0x0021;
    public const int MaNoActivate = 3;

    public static void MakeToolWindow(IntPtr hwnd)
    {
        long style = GetExStyle(hwnd).ToInt64();
        SetExStyle(hwnd, new IntPtr(style | WsExToolWindow));
    }

    public static void BeginDrag(IntPtr hwnd)
    {
        ReleaseCapture();
        SendMessage(hwnd, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
    }

    /// <summary>
    /// The taskbar is topmost too. A click on it paints the bar over this window.
    /// Putting the window back at the top of the topmost band keeps the strip visible.
    /// </summary>
    public static void RaiseAboveTaskbar(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        uint flags = SwpNoMove | SwpNoSize | SwpNoActivate;
        if (!IsWindowVisible(hwnd))
            flags |= SwpShowWindow;

        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, flags);
    }

    [DllImport("user32.dll")]
    public static extern bool RegisterShellHookWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool DeregisterShellHookWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private static IntPtr GetExStyle(IntPtr hwnd) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, GwlExStyle) : (IntPtr)GetWindowLong32(hwnd, GwlExStyle);

    private static void SetExStyle(IntPtr hwnd, IntPtr style)
    {
        if (IntPtr.Size == 8)
            SetWindowLongPtr64(hwnd, GwlExStyle, style);
        else
            SetWindowLong32(hwnd, GwlExStyle, style.ToInt32());
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
