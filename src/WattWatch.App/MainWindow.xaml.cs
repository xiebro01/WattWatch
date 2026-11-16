using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using WattWatch.App.Services;
using WattWatch.App.ViewModels;
using WattWatch.Core.Monitor;

namespace WattWatch.App;

public partial class MainWindow : Window
{
    private readonly BatteryMonitor _monitor;
    private readonly MainViewModel _viewModel;
    private HwndSource? _source;
    private bool _exiting;

    public MainWindow(BatteryMonitor monitor)
    {
        InitializeComponent();

        _monitor = monitor;
        _viewModel = new MainViewModel(monitor);
        DataContext = _viewModel;

        _viewModel.StartWithWindowsChanged += Autostart.SetEnabled;

        _monitor.DevicesChanged += OnDevicesChanged;
        _monitor.DeviceUpdated += OnDeviceUpdated;

        Loaded += (_, _) =>
        {
            PlaceWindow();
            _viewModel.SyncDevices(_monitor.Devices);
        };

        try
        {
            Icon = BitmapFrame.Create(new Uri("pack://application:,,,/assets/app.ico"),
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }
        catch
        {
            // A missing icon must not stop the app.
        }
    }

    public void OpenSettings() => _viewModel.IsSettingsOpen = true;

    /// <summary>
    /// Center on the monitor the window is on. WPF's Left/Top and
    /// SystemParameters.WorkArea mix virtual-screen and DPI spaces on scaled
    /// multi-monitor setups, so the final placement is done with SetWindowPos in
    /// physical pixels taken straight from GetMonitorInfo.
    /// </summary>
    private void PlaceWindow()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        // Always centre on the primary monitor: the window may be created on a
        // secondary screen (mixed DPI), and a tray app belongs where the taskbar is.
        IntPtr monitor = MonitorFromPoint(new Point32 { X = 0, Y = 0 }, MonitorDefaultToPrimary);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;

        uint dpi = 96;
        if (GetDpiForMonitor(monitor, 0, out uint dpiX, out _) != 0 || dpiX == 0)
            dpi = GetDpiForWindow(handle);
        double scale = dpi > 0 ? dpi / 96.0 : 1.0;

        int workWidth = info.Work.Right - info.Work.Left;
        int workHeight = info.Work.Bottom - info.Work.Top;

        MaxHeight = Math.Max(380, workHeight / scale - 32);
        if (Height > MaxHeight) Height = MaxHeight;

        int pixelWidth = (int)Math.Round(Width * scale);
        int pixelHeight = (int)Math.Round(Height * scale);
        int x = info.Work.Left + (workWidth - pixelWidth) / 2;
        int y = info.Work.Top + (workHeight - pixelHeight) / 2;

        SetWindowPos(handle, IntPtr.Zero, x, y, pixelWidth, pixelHeight,
            SwpNoZOrder | SwpNoActivate);
    }

    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    private const uint MonitorDefaultToPrimary = 1;

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point32 point, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr handle);

    public void PrepareForExit() => _exiting = true;

    private void OnDevicesChanged() =>
        Dispatcher.BeginInvoke(() => _viewModel.SyncDevices(_monitor.Devices));

    private void OnDeviceUpdated(BatteryDevice device) =>
        Dispatcher.BeginInvoke(() => _viewModel.UpdateDevice(device));

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _source = PresentationSource.FromVisual(this) as HwndSource;
        _source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (SingleInstance.IsShowMessage(msg))
        {
            (Application.Current as App)?.ShowMainWindow();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not System.Windows.Controls.Button)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CloseToTray) Hide();
        else Application.Current.Shutdown();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (_viewModel.IsSettingsOpen) _viewModel.IsSettingsOpen = false;
        else if (_viewModel.IsOtherOpen) _viewModel.IsOtherOpen = false;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting && _viewModel.CloseToTray)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
