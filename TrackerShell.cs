using Forms = System.Windows.Forms;

namespace NetworkTracker;

internal sealed class TrackerShell : IDisposable
{
    private readonly AppSettings _settings;
    private readonly BandwidthMonitor _bandwidth = new();
    private readonly PingMonitor _ping = new();
    private readonly MainWindow _window;
    private readonly Forms.NotifyIcon _tray;
    private readonly Forms.ContextMenuStrip _menu = new();
    private readonly IntPtr _iconHandle;
    private bool _disposed;

    public TrackerShell(AppSettings settings)
    {
        _settings = settings;
        _window = new MainWindow(settings, _bandwidth, _ping);
        _window.ContextMenuRequested += (_, _) => _menu.Show(Forms.Cursor.Position);
        _window.TooltipChanged += (_, text) => SetTrayText(text);
        _window.TaskbarRecreated += (_, _) => RestoreTray();

        (System.Drawing.Icon icon, IntPtr iconHandle) = TrayIconFactory.Create();
        _iconHandle = iconHandle;
        _tray = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = "NetworkTracker",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                ShowWindow();
        };

        _menu.Opening += (_, _) => RebuildMenu();

        _bandwidth.SetPreferredAdapter(settings.AdapterId);
        _bandwidth.Start();
        _ping.Start();
        _window.Show();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            _window.RememberBounds();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
        }

        _window.Detach();
        _bandwidth.Dispose();
        _ping.Dispose();

        _tray.Visible = false;
        _tray.Dispose();
        _menu.Dispose();
        TrayIconFactory.Destroy(_iconHandle);

        try
        {
            _window.CloseForExit();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
        }
    }

    private void ShowWindow()
    {
        _window.EnsureOnScreen();
        if (!_window.IsVisible)
            _window.Show();
        _window.Opacity = 1;
    }

    private void ToggleWindow()
    {
        if (_window.IsVisible)
            _window.Hide();
        else
            ShowWindow();
    }

    private void RestoreTray()
    {
        _tray.Visible = false;
        _tray.Visible = true;
    }

    private void SetTrayText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            text = "NetworkTracker";
        if (text.Length > 127)
            text = text[..127];

        try
        {
            _tray.Text = text;
        }
        catch (ArgumentException)
        {
            _tray.Text = text.Length > 63 ? text[..63] : text;
        }
    }

    private void RebuildMenu()
    {
        while (_menu.Items.Count > 0)
        {
            Forms.ToolStripItem item = _menu.Items[0];
            _menu.Items.RemoveAt(0);
            item.Dispose();
        }

        IReadOnlyList<AdapterEntry> adapters = AdapterCatalog.List();
        if (!string.IsNullOrEmpty(_settings.AdapterId) && adapters.All(entry => entry.Id != _settings.AdapterId))
        {
            _settings.AdapterId = null;
            _bandwidth.SetPreferredAdapter(null);
            SettingsService.Save(_settings);
        }

        _menu.Items.Add(_window.IsVisible ? "Hide window" : "Show window", null, (_, _) => ToggleWindow());
        _menu.Items.Add(CheckItem("Dock to taskbar", _settings.Docked, value => _window.SetDocked(value)));

        var adapterMenu = new Forms.ToolStripMenuItem("Adapter");
        string? automaticName = AdapterCatalog.Resolve(null)?.Name;
        bool automatic = string.IsNullOrEmpty(_settings.AdapterId);
        var automaticItem = new Forms.ToolStripMenuItem(automaticName == null ? "Automatic" : $"Automatic ({automaticName})")
        {
            Checked = automatic
        };
        automaticItem.Click += (_, _) => SelectAdapter(null);
        adapterMenu.DropDownItems.Add(automaticItem);

        string? selectedId = _settings.AdapterId;
        foreach (AdapterEntry entry in adapters)
        {
            var item = new Forms.ToolStripMenuItem(EscapeMenu(entry.MenuLabel))
            {
                Checked = !automatic && string.Equals(entry.Id, selectedId, StringComparison.OrdinalIgnoreCase)
            };
            string id = entry.Id;
            item.Click += (_, _) => SelectAdapter(id);
            adapterMenu.DropDownItems.Add(item);
        }

        if (adapters.Count == 0)
            adapterMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("No adapters") { Enabled = false });

        _menu.Items.Add(adapterMenu);

        var units = new Forms.ToolStripMenuItem("Units");
        units.DropDownItems.Add(UnitItem("Mbps", SpeedUnit.Mbps));
        units.DropDownItems.Add(UnitItem("MB/s", SpeedUnit.MegabytesPerSecond));
        _menu.Items.Add(units);

        _menu.Items.Add(CheckItem("Show graph", _settings.ShowGraph, value =>
        {
            _settings.ShowGraph = value;
            _window.ApplyPresentation();
            SettingsService.Save(_settings);
        }));
        _menu.Items.Add(CheckItem("Show ping", _settings.ShowPing, value =>
        {
            _settings.ShowPing = value;
            _window.ApplyPresentation();
            SettingsService.Save(_settings);
        }));
        _menu.Items.Add(CheckItem("Start with Windows", StartupService.IsEnabled(), value =>
        {
            StartupService.SetEnabled(value);
            _settings.StartWithWindows = StartupService.IsEnabled();
            SettingsService.Save(_settings);
        }));

        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => System.Windows.Application.Current.Shutdown());
    }

    private void SelectAdapter(string? adapterId)
    {
        _settings.AdapterId = adapterId;
        _bandwidth.SetPreferredAdapter(adapterId);
        SettingsService.Save(_settings);
    }

    private Forms.ToolStripMenuItem UnitItem(string text, SpeedUnit unit)
    {
        var item = new Forms.ToolStripMenuItem(text)
        {
            Checked = _settings.Unit == unit
        };
        item.Click += (_, _) =>
        {
            _settings.Unit = unit;
            _window.ApplyPresentation();
            SettingsService.Save(_settings);
        };
        return item;
    }

    private static Forms.ToolStripMenuItem CheckItem(string text, bool check, Action<bool> apply)
    {
        var item = new Forms.ToolStripMenuItem(text)
        {
            Checked = check,
            CheckOnClick = true
        };
        item.Click += (_, _) => apply(item.Checked);
        return item;
    }

    private static string EscapeMenu(string text)
    {
        string escaped = text.Replace("&", "&&");
        return escaped.Length <= 80 ? escaped : escaped[..80];
    }
}
