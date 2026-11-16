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
    /// Center on the primary monitor and keep the window inside its work area.
    /// Deliberately done in WPF units (Left/Top/Height) and never with
    /// SetWindowPos: mixing physical pixels and DIPs is what made the window come
    /// out 420x660 physical on a 175% display, squashing the card layout.
    /// </summary>
    private void PlaceWindow()
    {
        System.Windows.Rect work = SystemParameters.WorkArea;

        MaxHeight = Math.Max(380, work.Height - 32);
        if (Height > MaxHeight) Height = MaxHeight;

        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + (work.Height - Height) / 2;
    }
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

    private bool _resizing;
    private Point _resizeStart;
    private System.Windows.Size _resizeStartSize;

    private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not UIElement grip) return;
        _resizing = true;
        _resizeStart = e.GetPosition(this);
        _resizeStartSize = new System.Windows.Size(ActualWidth, ActualHeight);
        grip.CaptureMouse();
        e.Handled = true;
    }

    private void ResizeGrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_resizing) return;
        Point current = e.GetPosition(this);
        Width = Math.Max(MinWidth, _resizeStartSize.Width + (current.X - _resizeStart.X));
        Height = Math.Max(MinHeight, _resizeStartSize.Height + (current.Y - _resizeStart.Y));
    }

    private void ResizeGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_resizing) return;
        _resizing = false;
        (sender as UIElement)?.ReleaseMouseCapture();
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
