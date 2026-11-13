using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace NetworkTracker;

internal enum TaskbarEdge
{
    Left,
    Top,
    Right,
    Bottom
}

internal readonly record struct TaskbarSlot(
    bool Found,
    TaskbarEdge Edge,
    double Cross,
    double Thickness,
    double FreeStart,
    double FreeEnd)
{
    public bool Horizontal => Edge is TaskbarEdge.Top or TaskbarEdge.Bottom;

    public double Span => Math.Max(0, FreeEnd - FreeStart);
}

internal static class TaskbarLocator
{
    public static TaskbarSlot GetSlot(Visual visual)
    {
        try
        {
            double scale = VisualTreeHelper.GetDpi(visual).PixelsPerDip;
            if (scale <= 0)
                scale = 1;

            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero || !GetWindowRect(tray, out RECT bar))
                return default;

            if (!TryGetMonitor(tray, out RECT monitor))
            {
                monitor = new RECT
                {
                    Left = 0,
                    Top = 0,
                    Right = bar.Right,
                    Bottom = bar.Bottom
                };
            }

            TaskbarEdge edge = DetectEdge(bar, monitor);
            bool horizontal = edge is TaskbarEdge.Top or TaskbarEdge.Bottom;
            CollectChrome(tray, bar, horizontal, out int freeStartPx, out int freeEndPx);

            double cross = horizontal ? bar.Top / scale : bar.Left / scale;
            double thickness = horizontal
                ? Math.Max(0, (bar.Bottom - bar.Top) / scale)
                : Math.Max(0, (bar.Right - bar.Left) / scale);

            return new TaskbarSlot(
                true,
                edge,
                cross,
                thickness,
                freeStartPx / scale,
                freeEndPx / scale);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            return default;
        }
    }

    private static void CollectChrome(IntPtr tray, RECT bar, bool horizontal, out int freeStart, out int freeEnd)
    {
        int barStart = horizontal ? bar.Left : bar.Top;
        int barEnd = horizontal ? bar.Right : bar.Bottom;
        int rebarStart = barStart;
        var rightEdges = new List<int>();

        IntPtr child = IntPtr.Zero;
        while (true)
        {
            child = FindWindowEx(tray, child, null, null);
            if (child == IntPtr.Zero)
                break;
            if (!GetWindowRect(child, out RECT rect))
                continue;

            int size = horizontal ? rect.Right - rect.Left : rect.Bottom - rect.Top;
            if (size < 8)
                continue;

            string className = ClassName(child);
            int start = horizontal ? rect.Left : rect.Top;
            int end = horizontal ? rect.Right : rect.Bottom;

            if (className is "ReBarWindow32")
            {
                rebarStart = start;
                continue;
            }

            if (className is "MSTaskSwWClass" or "MSTaskListWClass")
                continue;

            // Ignore the stretched task-button band. Keep the widgets, tray, and show-desktop button.
            if (start > barStart + ((barEnd - barStart) / 3))
                rightEdges.Add(start);
        }

        freeStart = rebarStart > barStart ? rebarStart : barStart + 48;
        freeEnd = rightEdges.Count > 0 ? rightEdges.Min() : barEnd - 160;
        if (freeEnd <= freeStart + 80)
        {
            freeStart = barStart + 48;
            freeEnd = barEnd - 8;
        }
    }

    private static TaskbarEdge DetectEdge(RECT bar, RECT monitor)
    {
        bool horizontal = bar.Right - bar.Left >= bar.Bottom - bar.Top;
        if (horizontal)
        {
            int toTop = bar.Top - monitor.Top;
            int toBottom = monitor.Bottom - bar.Bottom;
            return toTop <= toBottom ? TaskbarEdge.Top : TaskbarEdge.Bottom;
        }

        int toLeft = bar.Left - monitor.Left;
        int toRight = monitor.Right - bar.Right;
        return toLeft <= toRight ? TaskbarEdge.Left : TaskbarEdge.Right;
    }

    private static string ClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        _ = GetClassName(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static bool TryGetMonitor(IntPtr hwnd, out RECT monitor)
    {
        IntPtr handle = MonitorFromWindow(hwnd, 2);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (handle != IntPtr.Zero && GetMonitorInfo(handle, ref info))
        {
            monitor = info.rcMonitor;
            return true;
        }

        monitor = default;
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
}
