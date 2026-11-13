namespace NetworkTracker;

public sealed class AppSettings
{
    public AppSettings()
    {
    }

    public string? AdapterId { get; set; }

    public SpeedUnit Unit { get; set; } = SpeedUnit.Mbps;

    public bool ShowGraph { get; set; } = true;

    public bool ShowPing { get; set; } = true;

    public double Left { get; set; } = double.NaN;

    public double Top { get; set; } = double.NaN;

    public double Width { get; set; } = 300;

    public bool StartWithWindows { get; set; }
}
