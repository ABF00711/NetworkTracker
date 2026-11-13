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

    public bool Docked { get; set; }

    public double DockedWidth { get; set; } = 420;

    /// <summary>Distance from the tray edge, so the strip stays beside the clock.</summary>
    public double DockedInset { get; set; }

    public bool StartWithWindows { get; set; }
}
