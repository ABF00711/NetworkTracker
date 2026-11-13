using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace NetworkTracker;

public partial class MainWindow : Window
{
    private static readonly SolidColorBrush PingNormal = Freeze(0x20, 0x21, 0x24);
    private static readonly SolidColorBrush PingSlow = Freeze(0xE3, 0x74, 0x00);
    private static readonly SolidColorBrush PingBad = Freeze(0xD9, 0x30, 0x25);
    private static readonly SolidColorBrush PingPending = Freeze(0x5F, 0x63, 0x68);
    private static readonly SolidColorBrush DockPingNormal = Freeze(0xE8, 0xEA, 0xED);
    private static readonly SolidColorBrush DockPingSlow = Freeze(0xFF, 0xB7, 0x4D);
    private static readonly SolidColorBrush DockPingBad = Freeze(0xFF, 0x6E, 0x6E);
    private static readonly SolidColorBrush DockPingPending = Freeze(0x9A, 0xA0, 0xA6);
    private static readonly SolidColorBrush DockBackground = Freeze(0x1C, 0x1C, 0x1C);

    private readonly AppSettings _settings;
    private readonly BandwidthMonitor _bandwidth;
    private readonly PingMonitor _ping;
    private readonly List<ThroughputPoint> _points = new();
    private readonly DispatcherTimer _dockFollow;

    private ThroughputPoint _last = ThroughputPoint.Empty;
    private HostPing _google = HostPing.Pending("Google");
    private HostPing _baidu = HostPing.Pending("Baidu");
    private bool _hasSample;
    private bool _resizing;
    private bool _dockDrag;
    private bool _allowClose;
    private bool _detached;
    private double _resizeStartScreenX;
    private double _resizeStartWidth;
    private double _resizeAnchorRight;
    private System.Windows.Point _dockDragScreen;
    private double _dockDragLeft;
    private double _dockDragTop;
    private int _taskbarCreatedMessage;

    internal MainWindow(AppSettings settings, BandwidthMonitor bandwidth, PingMonitor ping)
    {
        _settings = settings;
        _bandwidth = bandwidth;
        _ping = ping;

        InitializeComponent();
        Width = Math.Clamp(settings.Width, MinWidth, 720);

        _dockFollow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _dockFollow.Tick += (_, _) =>
        {
            if (_settings.Docked && !_dockDrag && !_resizing)
                SnapToTaskbar();
        };

        _bandwidth.Sampled += OnSampled;
        _ping.Updated += OnPing;
        ApplyPresentation();

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    public event EventHandler? ContextMenuRequested;

    public event EventHandler<string>? TooltipChanged;

    public event EventHandler? TaskbarRecreated;

    public void SetDocked(bool docked)
    {
        if (_settings.Docked == docked)
            return;

        if (docked)
        {
            if (IsLoaded && !double.IsNaN(Left) && !double.IsNaN(Top))
            {
                _settings.Left = Left;
                _settings.Top = Top;
                _settings.Width = Width;
            }

            _settings.Docked = true;
            ApplyChrome();
            SnapToTaskbar();
            _dockFollow.Start();
        }
        else
        {
            if (IsLoaded)
                StoreDockInset();
            _settings.Docked = false;
            _dockFollow.Stop();
            ApplyChrome();
            RestoreFloating();
        }

        SettingsService.Save(_settings);
    }

    public void ApplyPresentation()
    {
        ApplyChrome();
        _ping.SetEnabled(_settings.ShowPing);
        UpdateSpeedText();
        RenderPings();
        if (_settings.ShowGraph)
            Graph.Update(_points, _settings.Unit);
        PublishTooltip();
    }

    public void RememberBounds()
    {
        if (!IsLoaded || double.IsNaN(Left) || double.IsNaN(Top))
            return;

        if (_settings.Docked)
            StoreDockInset();
        else
        {
            _settings.Left = Left;
            _settings.Top = Top;
            _settings.Width = Width;
        }

        SettingsService.Save(_settings);
    }

    public void EnsureOnScreen()
    {
        if (_settings.Docked)
        {
            SnapToTaskbar();
            return;
        }

        double height = ActualHeight > 1 ? ActualHeight : EstimateHeight();
        if (!ScreenContains(Left, Top, Width, height))
            MoveToDefaultCorner(height);
    }

    public void Detach()
    {
        _detached = true;
        _dockFollow.Stop();
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

        if (_settings.Docked)
            _dockFollow.Start();
    }

    private void Place()
    {
        if (_settings.Docked)
        {
            SnapToTaskbar();
            return;
        }

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

    private void ApplyChrome()
    {
        bool docked = _settings.Docked;
        Card.Visibility = docked ? Visibility.Collapsed : Visibility.Visible;
        DockStrip.Visibility = docked ? Visibility.Visible : Visibility.Collapsed;
        Background = docked ? DockBackground : Brushes.White;

        MoveGraph(docked ? DockGraphHost : CardGraphHost);
        Graph.SetAppearance(docked, showScale: !docked);

        CardGraphHost.Visibility = !docked && _settings.ShowGraph ? Visibility.Visible : Visibility.Collapsed;
        SpeedRow.Visibility = docked ? Visibility.Collapsed : Visibility.Visible;
        PingRow.Visibility = !docked && _settings.ShowPing ? Visibility.Visible : Visibility.Collapsed;

        bool graph = _settings.ShowGraph;
        bool ping = _settings.ShowPing;
        DockGraphColumn.Width = graph ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        DockGraphColumn.MinWidth = graph ? 64 : 0;
        DockGraphHost.Visibility = graph ? Visibility.Visible : Visibility.Collapsed;
        DockGoogleText.Visibility = ping ? Visibility.Visible : Visibility.Collapsed;
        DockBaiduText.Visibility = ping ? Visibility.Visible : Visibility.Collapsed;
        DockGoogleColumn.Width = ping ? GridLength.Auto : new GridLength(0);
        DockBaiduColumn.Width = ping ? GridLength.Auto : new GridLength(0);

        if (docked)
        {
            SizeToContent = SizeToContent.Manual;
        }
        else
        {
            MinHeight = 0;
            MaxHeight = double.PositiveInfinity;
            SizeToContent = SizeToContent.Height;
        }
    }

    private void MoveGraph(System.Windows.Controls.Border host)
    {
        if (ReferenceEquals(Graph.Parent, host))
            return;

        if (Graph.Parent is System.Windows.Controls.Border previous)
            previous.Child = null;

        host.Child = Graph;
    }

    private void SnapToTaskbar()
    {
        if (!_settings.Docked || !IsLoaded)
            return;

        TaskbarSlot slot = TaskbarLocator.GetSlot(this);
        if (!slot.Found)
            return;

        if (slot.Thickness < 8)
        {
            PlaceOnSliver(slot);
            return;
        }

        double room = slot.Span >= 160 ? slot.Span : 160;
        double length = Math.Clamp(_settings.DockedWidth, 200, Math.Min(1400, room));
        double inset = Math.Max(0, _settings.DockedInset);

        if (slot.Horizontal)
        {
            double top = slot.Cross;
            double height = slot.Thickness;
            double maxLeft = Math.Max(slot.FreeStart, slot.FreeEnd - length);
            double left = Math.Clamp(slot.FreeEnd - length - inset, slot.FreeStart, maxLeft);
            MoveIfNeeded(left, top, length, height);
            MinHeight = height;
            MaxHeight = height;
            return;
        }

        double stripHeight = slot.Thickness;
        double maxTop = Math.Max(slot.FreeStart, slot.FreeEnd - stripHeight);
        double topAlong = Math.Clamp(slot.FreeEnd - stripHeight - inset, slot.FreeStart, maxTop);
        double leftEdge = slot.Edge == TaskbarEdge.Left
            ? slot.Cross + slot.Thickness
            : slot.Cross - length;
        MoveIfNeeded(leftEdge, topAlong, length, stripHeight);
    }

    private void PlaceOnSliver(TaskbarSlot slot)
    {
        if (slot.Horizontal)
            MoveIfNeeded(Left, slot.Cross, Width, Math.Max(1, slot.Thickness));
        else
            MoveIfNeeded(slot.Cross, Top, Math.Max(1, slot.Thickness), Height);
    }

    private void MoveIfNeeded(double left, double top, double width, double height)
    {
        if (Math.Abs(Left - left) < 0.5 && Math.Abs(Top - top) < 0.5 &&
            Math.Abs(Width - width) < 0.5 && Math.Abs(Height - height) < 0.5)
            return;

        Width = width;
        Height = height;
        Left = left;
        Top = top;
    }

    private void StoreDockInset()
    {
        TaskbarSlot slot = TaskbarLocator.GetSlot(this);
        if (!slot.Found)
            return;

        _settings.DockedWidth = Width;
        double farEdge = slot.Horizontal ? Left + Width : Top + Height;
        _settings.DockedInset = Math.Max(0, slot.FreeEnd - farEdge);
    }

    private void RestoreFloating()
    {
        MinHeight = 0;
        MaxHeight = double.PositiveInfinity;
        SizeToContent = SizeToContent.Height;
        Width = Math.Clamp(_settings.Width, MinWidth, 720);
        double height = EstimateHeight();
        if (ScreenContains(_settings.Left, _settings.Top, Width, height))
        {
            Left = _settings.Left;
            Top = _settings.Top;
            return;
        }

        MoveToDefaultCorner(height);
    }

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
        string down;
        string up;
        if (!_hasSample)
        {
            string unit = SpeedFormat.UnitLabel(_settings.Unit);
            down = $"… {unit}";
            up = $"… {unit}";
        }
        else
        {
            down = SpeedFormat.Format(_last.DownBytesPerSecond, _settings.Unit);
            up = SpeedFormat.Format(_last.UpBytesPerSecond, _settings.Unit);
        }

        DownText.Text = down;
        UpText.Text = up;
        DockDownText.Text = down;
        DockUpText.Text = up;
    }

    private void ApplyPings(IReadOnlyList<HostPing> results)
    {
        foreach (HostPing ping in results)
        {
            if (ping.Label == "Baidu")
                _baidu = ping;
            else
                _google = ping;
        }

        RenderPings();
        PublishTooltip();
    }

    private void RenderPings()
    {
        (string google, SolidColorBrush googleColor) = Describe(_google, shortForm: false);
        (string baidu, SolidColorBrush baiduColor) = Describe(_baidu, shortForm: false);
        GoogleText.Text = google;
        GoogleText.Foreground = googleColor;
        BaiduText.Text = baidu;
        BaiduText.Foreground = baiduColor;

        (string dockGoogle, SolidColorBrush dockGoogleColor) = Describe(_google, shortForm: true);
        (string dockBaidu, SolidColorBrush dockBaiduColor) = Describe(_baidu, shortForm: true);
        DockGoogleText.Text = dockGoogle;
        DockGoogleText.Foreground = dockGoogleColor;
        DockBaiduText.Text = dockBaidu;
        DockBaiduText.Foreground = dockBaiduColor;
    }

    private static (string Text, SolidColorBrush Color) Describe(HostPing ping, bool shortForm)
    {
        string name = shortForm ? Initial(ping.Label) : ping.Label;
        SolidColorBrush normal = shortForm ? DockPingNormal : PingNormal;
        SolidColorBrush slow = shortForm ? DockPingSlow : PingSlow;
        SolidColorBrush bad = shortForm ? DockPingBad : PingBad;
        SolidColorBrush pending = shortForm ? DockPingPending : PingPending;
        string gap = shortForm ? " " : "  ";

        return ping.Outcome switch
        {
            PingOutcome.Ok when ping.RoundtripMilliseconds >= 500 => ($"{name}{gap}{ping.RoundtripMilliseconds}{(shortForm ? "" : " ms")}", bad),
            PingOutcome.Ok when ping.RoundtripMilliseconds >= 300 => ($"{name}{gap}{ping.RoundtripMilliseconds}{(shortForm ? "" : " ms")}", slow),
            PingOutcome.Ok => ($"{name}{gap}{ping.RoundtripMilliseconds}{(shortForm ? "" : " ms")}", normal),
            PingOutcome.Timeout => ($"{name}{gap}{(shortForm ? "—" : "timeout")}", bad),
            PingOutcome.Error => ($"{name}{gap}{(shortForm ? "—" : "fail")}", bad),
            _ => ($"{name}{gap}…", pending)
        };
    }

    private static string Initial(string label) =>
        string.IsNullOrEmpty(label) ? "?" : label[..1];

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

        if (_settings.Docked)
        {
            _dockDrag = true;
            _dockDragScreen = PointToScreen(e.GetPosition(this));
            _dockDragLeft = Left;
            _dockDragTop = Top;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        NativeMethods.BeginDrag(new WindowInteropHelper(this).Handle);
        RememberBounds();
    }

    private void Root_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dockDrag)
            return;

        System.Windows.Point now = PointToScreen(e.GetPosition(this));
        TaskbarSlot slot = TaskbarLocator.GetSlot(this);
        if (!slot.Found)
            return;

        if (slot.Horizontal)
        {
            double left = _dockDragLeft + (now.X - _dockDragScreen.X);
            double maxLeft = Math.Max(slot.FreeStart, slot.FreeEnd - Width);
            Left = Math.Clamp(left, slot.FreeStart, maxLeft);
            Top = slot.Cross;
            return;
        }

        double top = _dockDragTop + (now.Y - _dockDragScreen.Y);
        double maxTop = Math.Max(slot.FreeStart, slot.FreeEnd - Height);
        Top = Math.Clamp(top, slot.FreeStart, maxTop);
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dockDrag)
            return;

        _dockDrag = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
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
        _resizeAnchorRight = Left + Width;
        Grip.CaptureMouse();
        e.Handled = true;
    }

    private void Grip_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_resizing)
            return;

        double x = PointToScreen(e.GetPosition(this)).X;
        double limit = _settings.Docked ? 1400 : 720;
        double width = Math.Clamp(_resizeStartWidth + (x - _resizeStartScreenX), MinWidth, limit);
        if (_settings.Docked)
        {
            TaskbarSlot slot = TaskbarLocator.GetSlot(this);
            if (slot.Found && slot.Horizontal)
                width = Math.Min(width, Math.Max(MinWidth, slot.FreeEnd - slot.FreeStart));
            Width = width;
            Left = _resizeAnchorRight - width;
            if (slot.Found && slot.Horizontal)
                Left = Math.Clamp(Left, slot.FreeStart, Math.Max(slot.FreeStart, slot.FreeEnd - Width));
        }
        else
        {
            Width = width;
        }
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

        if (msg == 0x001A && _settings.Docked)
            Dispatcher.BeginInvoke(new Action(SnapToTaskbar));

        if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
        {
            if (_settings.Docked)
                Dispatcher.BeginInvoke(new Action(SnapToTaskbar));
            TaskbarRecreated?.Invoke(this, EventArgs.Empty);
        }

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
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
