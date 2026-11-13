using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace NetworkTracker;

public sealed class SpeedGraph : FrameworkElement
{
    private static readonly SolidColorBrush PlotBrush = Freeze(System.Windows.Media.Color.FromRgb(255, 255, 255));
    private static readonly SolidColorBrush DownBrush = Freeze(System.Windows.Media.Color.FromRgb(0x2F, 0x89, 0xFF));
    private static readonly SolidColorBrush UpBrush = Freeze(System.Windows.Media.Color.FromRgb(0xFF, 0x5A, 0x36));
    private static readonly SolidColorBrush LabelBrush = Freeze(System.Windows.Media.Color.FromRgb(0x80, 0x86, 0x8B));
    private static readonly SolidColorBrush LabelBackBrush = Freeze(System.Windows.Media.Color.FromArgb(220, 255, 255, 255));
    private static readonly SolidColorBrush DarkPlotBrush = Freeze(System.Windows.Media.Color.FromRgb(0x1C, 0x1C, 0x1C));
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private IReadOnlyList<ThroughputPoint> _points = Array.Empty<ThroughputPoint>();
    private SpeedUnit _unit = SpeedUnit.Mbps;
    private double _scale = 1;
    private bool _dark;
    private bool _showScale = true;

    public SpeedGraph()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        ClipToBounds = true;
    }

    internal void Update(IReadOnlyList<ThroughputPoint> points, SpeedUnit unit)
    {
        _points = points;
        if (unit != _unit)
        {
            _unit = unit;
            _scale = 0;
        }

        double peak = 0;
        foreach (ThroughputPoint point in points)
        {
            peak = Math.Max(peak, SpeedFormat.ToDisplay(point.DownBytesPerSecond, unit));
            peak = Math.Max(peak, SpeedFormat.ToDisplay(point.UpBytesPerSecond, unit));
        }

        double next = SpeedFormat.NiceScale(peak);
        if (_scale > 0 && peak <= _scale * 0.98 && next < _scale && _scale / Math.Max(next, 0.001) < 1.5)
            next = _scale;

        _scale = next;
        InvalidateVisual();
    }

    internal void SetAppearance(bool dark, bool showScale)
    {
        if (_dark == dark && _showScale == showScale)
            return;
        _dark = dark;
        _showScale = showScale;
        InvalidateVisual();
    }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
    {
        double width = double.IsPositiveInfinity(availableSize.Width) ? 300 : availableSize.Width;
        double height = double.IsPositiveInfinity(availableSize.Height) ? 74 : availableSize.Height;
        return new System.Windows.Size(Math.Max(0, width), Math.Max(0, height));
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        if (width < 2 || height < 2)
            return;

        dc.DrawRectangle(_dark ? DarkPlotBrush : PlotBrush, null, new Rect(0, 0, width, height));

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double scale = _scale <= 0 ? 1 : _scale;
        double rightPad = Snap(_showScale ? 6 : 1, dpi);
        double plotWidth = Math.Max(1, width - rightPad);
        double bottom = Snap(height - 1, dpi);
        double plotHeight = Math.Max(1, bottom - 2);
        const double barSlot = 2;
        int visible = Math.Max(1, (int)Math.Floor(plotWidth / barSlot));
        int start = Math.Max(0, _points.Count - visible);
        double x = Snap(plotWidth - ((_points.Count - start) * barSlot), dpi);

        for (int i = start; i < _points.Count; i++)
        {
            ThroughputPoint point = _points[i];
            double down = SpeedFormat.ToDisplay(point.DownBytesPerSecond, _unit);
            double up = SpeedFormat.ToDisplay(point.UpBytesPerSecond, _unit);
            DrawBar(dc, DownBrush, x, bottom, barSlot, HeightOf(down, scale, plotHeight, dpi), dpi);
            DrawBar(dc, UpBrush, x, bottom, barSlot, HeightOf(up, scale, plotHeight, dpi), dpi);
            x += barSlot;
        }

        if (!_showScale)
            return;

        var label = new FormattedText(
            SpeedFormat.FormatScale(scale, _unit),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            11,
            LabelBrush,
            dpi);
        var backing = new Rect(4, 3, Math.Min(plotWidth - 8, label.Width + 8), label.Height + 2);
        dc.DrawRectangle(LabelBackBrush, null, backing);
        dc.DrawText(label, new System.Windows.Point(8, 4));
    }

    private static double HeightOf(double displayValue, double scale, double plotHeight, double dpi)
    {
        if (displayValue <= 0 || scale <= 0)
            return 0;

        double height = displayValue / scale * plotHeight;
        return Snap(Math.Clamp(height, 1, plotHeight), dpi);
    }

    private static void DrawBar(DrawingContext dc, System.Windows.Media.Brush brush, double x, double bottom, double slot, double barHeight, double dpi)
    {
        if (barHeight <= 0)
            return;

        double left = Snap(x, dpi);
        double barWidth = Math.Max(Snap(1, dpi), Snap(slot - 0.6, dpi));
        dc.DrawRectangle(brush, null, new Rect(left, Snap(bottom - barHeight, dpi), barWidth, barHeight));
    }

    private static double Snap(double value, double dpi) => Math.Round(value * dpi) / dpi;

    private static SolidColorBrush Freeze(System.Windows.Media.Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
