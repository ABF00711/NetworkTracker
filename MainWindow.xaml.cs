using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace NetworkTracker;

public partial class MainWindow : Window
{
    private static readonly SolidColorBrush PingNormal = Freeze(0x20, 0x21, 0x24);
    private static readonly SolidColorBrush PingSlow = Freeze(0xE3, 0x74, 0x00);
    private static readonly SolidColorBrush PingBad = Freeze(0xD9, 0x30, 0x25);
    private static readonly SolidColorBrush PingPending = Freeze(0x5F, 0x63, 0x68);

    private readonly AppSettings _settings;
    private readonly BandwidthMonitor _bandwidth;
    private readonly PingMonitor _ping;
    private readonly List<ThroughputPoint> _points = new();

    private ThroughputPoint _last = ThroughputPoint.Empty;
    private bool _hasSample;
    private bool _resizing;
    private bool _allowClose;
    private bool _detached;
    private double _resizeStartScreenX;
    private double _resizeStartWidth;
    private int _taskbarCreatedMessage;

    internal MainWindow(AppSettings settings, BandwidthMonitor bandwidth, PingMonitor ping)
    {
        _settings = settings;
        _bandwidth = bandwidth;
        _ping = ping;

        InitializeComponent();
        Width = Math.Clamp(settings.Width, MinWidth, MaxWidth);

        _bandwidth.Sampled += OnSampled;
        _ping.Updated += OnPing;
        ApplyPresentation();

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    public event EventHandler? ContextMenuRequested;

    public event EventHandler<string>? TooltipChanged;

    public event EventHandler? TaskbarRecreated;

    public void ApplyPresentation()
    {
        Graph.Visibility = _settings.ShowGraph ? Visibility.Visible : Visibility.Collapsed;
        PingRow.Visibility = _settings.ShowPing ? Visibility.Visible : Visibility.Collapsed;
        _ping.SetEnabled(_settings.ShowPing);
        UpdateSpeedText();
        if (_settings.ShowGraph)
            Graph.Update(_points, _settings.Unit);
        PublishTooltip();
    }

    public void RememberBounds()
    {
        if (!IsLoaded || double.IsNaN(Left) || double.IsNaN(Top))
            return;

        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Width = Width;
        SettingsService.Save(_settings);
    }

    public void EnsureOnScreen()
    {
        double height = ActualHeight > 1 ? ActualHeight : EstimateHeight();
        if (!ScreenContains(Left, Top, Width, height))
            MoveToDefaultCorner(height);
    }

    public void Detach()
    {
        _detached = true;
        _bandwidth.Sampled -= OnSampled;
        _ping.Updated -= OnPing;
    }

    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        RememberBounds();
        base.OnClosing(e);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.MakeToolWindow(hwnd);
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(WndProc);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Place();
        }
        finally
        {
            Opacity = 1;
        }
    }

    private void Place()
    {
        double height = ActualHeight > 1 ? ActualHeight : EstimateHeight();
        if (ScreenContains(_settings.Left, _settings.Top, Width, height))
        {
            Left = _settings.Left;
            Top = _settings.Top;
            return;
        }

        MoveToDefaultCorner(height);
    }

    private void MoveToDefaultCorner(double height)
    {
        Rect area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left, area.Right - Width - 16);
        Top = Math.Max(area.Top, area.Bottom - height - 8);
    }

    private static double EstimateHeight() => 132;

    private void OnSampled(object? sender, ThroughputPoint point)
    {
        if (_detached || Dispatcher.HasShutdownStarted)
            return;

        _ = Dispatcher.InvokeAsync(() =>
        {
            if (_detached)
                return;
            AddPoint(point);
        });
    }

    private void OnPing(object? sender, IReadOnlyList<HostPing> results)
    {
        if (_detached || Dispatcher.HasShutdownStarted)
            return;

        HostPing[] copy = results.ToArray();
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (_detached)
                return;
            ApplyPings(copy);
        });
    }

    private void AddPoint(ThroughputPoint point)
    {
        _last = point;
        _hasSample = true;
        _points.Add(point);
        if (_points.Count > 360)
            _points.RemoveRange(0, _points.Count - 360);

        UpdateSpeedText();
        if (_settings.ShowGraph)
            Graph.Update(_points, _settings.Unit);
        PublishTooltip();
    }

    private void UpdateSpeedText()
    {
        if (!_hasSample)
        {
            string unit = SpeedFormat.UnitLabel(_settings.Unit);
            DownText.Text = $"… {unit}";
            UpText.Text = $"… {unit}";
            return;
        }

        DownText.Text = SpeedFormat.Format(_last.DownBytesPerSecond, _settings.Unit);
        UpText.Text = SpeedFormat.Format(_last.UpBytesPerSecond, _settings.Unit);
    }

    private void ApplyPings(IReadOnlyList<HostPing> results)
    {
        foreach (HostPing ping in results)
        {
            var target = ping.Label == "Baidu" ? BaiduText : GoogleText;
            (string text, SolidColorBrush color) = Describe(ping);
            target.Text = text;
            target.Foreground = color;
        }

        PublishTooltip();
    }

    private static (string Text, SolidColorBrush Color) Describe(HostPing ping)
    {
        return ping.Outcome switch
        {
            PingOutcome.Ok when ping.RoundtripMilliseconds >= 500 => ($"{ping.Label}  {ping.RoundtripMilliseconds} ms", PingBad),
            PingOutcome.Ok when ping.RoundtripMilliseconds >= 300 => ($"{ping.Label}  {ping.RoundtripMilliseconds} ms", PingSlow),
            PingOutcome.Ok => ($"{ping.Label}  {ping.RoundtripMilliseconds} ms", PingNormal),
            PingOutcome.Timeout => ($"{ping.Label}  timeout", PingBad),
            PingOutcome.Error => ($"{ping.Label}  fail", PingBad),
            _ => ($"{ping.Label}  …", PingPending)
        };
    }

    private void PublishTooltip()
    {
        var text = new StringBuilder();
        text.Append("↓ ").Append(DownText.Text).Append("   ↑ ").Append(UpText.Text);
        if (_settings.ShowPing)
            text.Append("   ").Append(GoogleText.Text).Append("   ").Append(BaiduText.Text);

        string tip = text.ToString();
        if (tip.Length > 127)
            tip = tip[..127];
        TooltipChanged?.Invoke(this, tip);
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_resizing || e.OriginalSource == Grip)
            return;

        NativeMethods.BeginDrag(new WindowInteropHelper(this).Handle);
        RememberBounds();
    }

    private void Root_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Dispatcher.BeginInvoke(() => ContextMenuRequested?.Invoke(this, EventArgs.Empty));
    }

    private void Grip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _resizing = true;
        _resizeStartScreenX = PointToScreen(e.GetPosition(this)).X;
        _resizeStartWidth = Width;
        Grip.CaptureMouse();
        e.Handled = true;
    }

    private void Grip_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_resizing)
            return;

        double x = PointToScreen(e.GetPosition(this)).X;
        Width = Math.Clamp(_resizeStartWidth + (x - _resizeStartScreenX), MinWidth, MaxWidth);
    }

    private void Grip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndResize();
        e.Handled = true;
    }

    private void Grip_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e) => EndResize();

    private void EndResize()
    {
        if (!_resizing)
            return;

        _resizing = false;
        if (Grip.IsMouseCaptured)
            Grip.ReleaseMouseCapture();
        RememberBounds();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmMouseActivate)
        {
            handled = true;
            return (IntPtr)NativeMethods.MaNoActivate;
        }

        if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
            TaskbarRecreated?.Invoke(this, EventArgs.Empty);

        return IntPtr.Zero;
    }

    private static bool ScreenContains(double left, double top, double width, double height)
    {
        if (double.IsNaN(left) || double.IsNaN(top) || width <= 0 || height <= 0)
            return false;

        var window = new Rect(left, top, width, height);
        var screen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        Rect intersection = Rect.Intersect(window, screen);
        return !intersection.IsEmpty && intersection.Width >= 40 && intersection.Height >= 20;
    }

    private static SolidColorBrush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
