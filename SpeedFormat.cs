using System.Globalization;

namespace NetworkTracker;

public enum SpeedUnit
{
    Mbps,
    MegabytesPerSecond
}

internal static class SpeedFormat
{
    public static double ToDisplay(double bytesPerSecond, SpeedUnit unit)
    {
        if (bytesPerSecond < 0 || double.IsNaN(bytesPerSecond) || double.IsInfinity(bytesPerSecond))
            return 0;

        return unit == SpeedUnit.Mbps
            ? bytesPerSecond * 8d / 1_000_000d
            : bytesPerSecond / 1_000_000d;
    }

    public static string UnitLabel(SpeedUnit unit) =>
        unit == SpeedUnit.Mbps ? "Mbps" : "MB/s";

    public static string Format(double bytesPerSecond, SpeedUnit unit)
    {
        double value = ToDisplay(bytesPerSecond, unit);
        string format = value >= 100 ? "0" : value >= 1 ? "0.0" : "0.00";
        return $"{value.ToString(format, CultureInfo.InvariantCulture)} {UnitLabel(unit)}";
    }

    public static string FormatScale(double displayValue, SpeedUnit unit)
    {
        string format = displayValue >= 100 ? "0" : "0.0";
        return $"{displayValue.ToString(format, CultureInfo.InvariantCulture)} {UnitLabel(unit)}";
    }

    /// <summary>
    /// Axis maximum with a little headroom. Steps stay near the recent peak
    /// so the bars fill the graph the way a taskbar meter does.
    /// </summary>
    public static double NiceScale(double peak)
    {
        if (peak <= 0 || double.IsNaN(peak) || double.IsInfinity(peak))
            return 1;

        double padded = peak * 1.08;
        double step = padded switch
        {
            >= 100 => 10,
            >= 20 => 2,
            >= 10 => 1,
            >= 1 => 0.5,
            _ => 0.1
        };

        double scale = Math.Ceiling(padded / step - 1e-9) * step;
        return Math.Max(Math.Round(scale, 4), step);
    }
}
