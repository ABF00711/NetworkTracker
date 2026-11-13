using System.Windows;
using Forms = System.Windows.Forms;

namespace NetworkTracker;

public partial class App : Application
{
    private const string MutexName = @"Local\NetworkTracker.SingleInstance";

    private Mutex? _mutex;
    private bool _ownsMutex;
    private TrackerShell? _shell;

    protected override void OnStartup(StartupEventArgs e)
    {
        Forms.Application.EnableVisualStyles();
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Write(args.Exception);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Write(args.Exception);
            args.SetObserved();
        };

        try
        {
            _mutex = new Mutex(true, MutexName, out bool created);
            _ownsMutex = created;
            if (!created)
            {
                Shutdown();
                return;
            }
        }
        catch (AbandonedMutexException)
        {
            // The previous instance crashed and abandoned the mutex. This process owns it now.
            _ownsMutex = true;
        }

        try
        {
            AppSettings settings = SettingsService.Load();
            settings.StartWithWindows = StartupService.IsEnabled();
            _shell = new TrackerShell(settings);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            MessageBox.Show(
                "NetworkTracker could not start." + Environment.NewLine + Environment.NewLine + ex.Message,
                "NetworkTracker",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shell?.Dispose();
        _shell = null;

        if (_ownsMutex)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Already released.
            }
        }

        _mutex?.Dispose();
        _mutex = null;
        base.OnExit(e);
    }
}
