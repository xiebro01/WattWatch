using System.Windows;
using System.Windows.Threading;
using WattWatch.App.Services;
using WattWatch.Core.Monitor;
using WattWatch.Core.Storage;

namespace WattWatch.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private BatteryMonitor? _monitor;
    private MainWindow? _window;
    private TrayIconService? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!SingleInstance.TryAcquire(out _mutex))
        {
            SingleInstance.SignalExisting();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _monitor = new BatteryMonitor();
        _window = new MainWindow(_monitor);
        _tray = new TrayIconService(_monitor.Settings.StartWithWindows);

        _tray.OpenRequested += ShowMainWindow;
        _tray.SettingsRequested += ShowSettings;
        _tray.RefreshRequested += () =>
        {
            _monitor.RefreshNow();
            _tray.ShowMessage("WattWatch", "已开始刷新设备电量");
        };
        _tray.AutostartToggled += Autostart.SetEnabled;
        _tray.ExitRequested += ExitApplication;

        _monitor.DevicesChanged += () => Dispatcher.BeginInvoke(UpdateTray);
        _monitor.DeviceUpdated += _ => Dispatcher.BeginInvoke(UpdateTray);
        _monitor.LowBattery += device => Dispatcher.BeginInvoke(() => _tray?.ShowLowBattery(device));
        _monitor.Start();

        UpdateTray();

        bool startHidden = e.Args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));
        if (!startHidden) ShowMainWindow();

        if (e.Args.Any(a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase)))
        {
            ShowMainWindow();
            _window?.OpenSettings();
        }
    }

    public void ShowMainWindow()
    {
        if (_window == null) return;

        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;

        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

    private void ShowSettings()
    {
        ShowMainWindow();
        _window?.OpenSettings();
    }

    private void UpdateTray()
    {
        if (_monitor == null || _tray == null) return;
        _tray.Update(_monitor.Devices, Autostart.IsEnabled());
    }

    private void ExitApplication()
    {
        _window?.PrepareForExit();
        _tray?.Dispose();
        _tray = null;
        _monitor?.Dispose();
        _monitor = null;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _monitor?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        e.Handled = true;
    }

    private static void Log(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.DataDirectory);
            File.AppendAllText(Path.Combine(AppSettings.DataDirectory, "error.log"),
                $"[{DateTimeOffset.Now:O}] {exception}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never throw.
        }
    }
}
