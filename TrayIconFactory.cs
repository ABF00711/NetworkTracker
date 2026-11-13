using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace NetworkTracker;

internal static class TrayIconFactory
{
    public static (Drawing.Icon Icon, IntPtr Handle) Create()
    {
        using var bitmap = new Drawing.Bitmap(32, 32, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality;
            graphics.Clear(Drawing.Color.Transparent);

            using var background = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 47, 137, 255));
            graphics.FillEllipse(background, 1, 1, 30, 30);

            graphics.SmoothingMode = Drawing2D.SmoothingMode.None;
            using var white = new Drawing.SolidBrush(Drawing.Color.White);
            graphics.FillRectangle(white, 8, 17, 4, 8);
            graphics.FillRectangle(white, 14, 10, 4, 15);
            graphics.FillRectangle(white, 20, 13, 4, 12);
        }

        IntPtr handle = bitmap.GetHicon();
        return (Drawing.Icon.FromHandle(handle), handle);
    }

    public static void Destroy(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
            DestroyIcon(handle);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
