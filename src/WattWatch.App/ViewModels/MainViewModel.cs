using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using WattWatch.Core.Monitor;
using WattWatch.Core.Storage;

namespace WattWatch.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly BatteryMonitor _monitor;
    private readonly AppSettings _settings;
    private string _statusLine = "正在扫描设备…";
    private bool _isSettingsOpen;
    private bool _isEmpty = true;

    public MainViewModel(BatteryMonitor monitor)
    {
        _monitor = monitor;
        _settings = monitor.Settings;

        RefreshCommand = new RelayCommand(() => _monitor.RefreshNow());
        ToggleSettingsCommand = new RelayCommand(() => IsSettingsOpen = !IsSettingsOpen);
        OpenDataFolderCommand = new RelayCommand(OpenDataFolder);
        RefreshDeviceCommand = new RelayCommand(p =>
        {
            if (p is DeviceViewModel vm) _monitor.RefreshDevice(vm.Key);
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when the user toggles the start-with-Windows option.</summary>
    public event Action<bool>? StartWithWindowsChanged;

    public ObservableCollection<DeviceViewModel> Devices { get; } = new();

    public ICommand RefreshCommand { get; }

    public ICommand ToggleSettingsCommand { get; }

    public ICommand OpenDataFolderCommand { get; }

    public ICommand RefreshDeviceCommand { get; }

    public string StatusLine
    {
        get => _statusLine;
        set
        {
            if (_statusLine == value) return;
            _statusLine = value;
            OnPropertyChanged(nameof(StatusLine));
        }
    }

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set
        {
            if (_isSettingsOpen == value) return;
            _isSettingsOpen = value;
            OnPropertyChanged(nameof(IsSettingsOpen));
        }
    }

    public bool IsEmpty
    {
        get => _isEmpty;
        private set
        {
            if (_isEmpty == value) return;
            _isEmpty = value;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public string FooterHint =>
        $"每 {_settings.PollIntervalMinutes} 分钟查询 · 低电量阈值 {_settings.LowBatteryThreshold}% · 数据仅保存在本机";

    public string PollMinutesText => $"每 {_settings.PollIntervalMinutes} 分钟";

    public string ThresholdText => $"{_settings.LowBatteryThreshold}%";

    public double PollMinutes
    {
        get => _settings.PollIntervalMinutes;
        set
        {
            int rounded = Math.Clamp((int)Math.Round(value / 5) * 5, 5, 60);
            if (_settings.PollIntervalMinutes == rounded) return;
            _settings.PollIntervalMinutes = rounded;
            Apply();
        }
    }

    public double Threshold
    {
        get => _settings.LowBatteryThreshold;
        set
        {
            int rounded = (int)Math.Round(value);
            if (_settings.LowBatteryThreshold == rounded) return;
            _settings.LowBatteryThreshold = rounded;
            Apply();
        }
    }

    public bool StartWithWindows
    {
        get => _settings.StartWithWindows;
        set
        {
            if (_settings.StartWithWindows == value) return;
            _settings.StartWithWindows = value;
            Apply();
            StartWithWindowsChanged?.Invoke(value);
        }
    }

    public bool CloseToTray
    {
        get => _settings.CloseToTray;
        set
        {
            if (_settings.CloseToTray == value) return;
            _settings.CloseToTray = value;
            Apply();
        }
    }

    public void SyncDevices(IReadOnlyList<BatteryDevice> devices)
    {
        Devices.Clear();
        foreach (BatteryDevice device in devices)
        {
            Devices.Add(CreateViewModel(device));
        }

        IsEmpty = Devices.Count == 0;
        StatusLine = Devices.Count == 0
            ? "未发现设备"
            : $"{Devices.Count} 台设备 · {DateTime.Now:HH:mm} 扫描";
        OnPropertyChanged(nameof(StatusLine));
        OnPropertyChanged(nameof(FooterHint));
        OnPropertyChanged(nameof(PollMinutesText));
        OnPropertyChanged(nameof(ThresholdText));
    }

    public void UpdateDevice(BatteryDevice device)
    {
        DeviceViewModel? vm = Devices.FirstOrDefault(d => d.Key == device.Key);
        if (vm == null)
        {
            SyncDevices(_monitor.Devices);
            return;
        }

        vm.RemainingText = RemainingText(device);
        vm.Refresh();
        StatusLine = $"{Devices.Count} 台设备 · {DateTime.Now:HH:mm} 更新";
    }

    public void Refresh()
    {
        Devices.Clear();
        foreach (BatteryDevice device in _monitor.Devices) Devices.Add(CreateViewModel(device));
        OnPropertyChanged(nameof(FooterHint));
        OnPropertyChanged(nameof(PollMinutesText));
        OnPropertyChanged(nameof(ThresholdText));
        OnPropertyChanged(nameof(PollMinutes));
        OnPropertyChanged(nameof(Threshold));
        OnPropertyChanged(nameof(StartWithWindows));
        OnPropertyChanged(nameof(CloseToTray));
    }

    private DeviceViewModel CreateViewModel(BatteryDevice device)
    {
        var vm = new DeviceViewModel(device) { RemainingText = RemainingText(device) };
        return vm;
    }

    private string RemainingText(BatteryDevice device)
    {
        TimeSpan? remaining = RemainingTimeEstimator.Estimate(
            _monitor.HistoryFor(device.Key, TimeSpan.FromHours(12)), device.Percent, device.Charging);
        if (remaining == null) return string.Empty;
        return remaining.Value.TotalHours >= 1
            ? $"预计还可用 {remaining.Value.Hours} 小时 {remaining.Value.Minutes} 分"
            : $"预计还可用 {Math.Max(1, remaining.Value.Minutes)} 分钟";
    }

    private void Apply()
    {
        _monitor.ApplySettings(_settings);
        OnPropertyChanged(nameof(FooterHint));
        OnPropertyChanged(nameof(PollMinutesText));
        OnPropertyChanged(nameof(ThresholdText));
        OnPropertyChanged(nameof(PollMinutes));
        OnPropertyChanged(nameof(Threshold));
        OnPropertyChanged(nameof(StartWithWindows));
        OnPropertyChanged(nameof(CloseToTray));
    }

    private static void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.DataDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", AppSettings.DataDirectory) { UseShellExecute = true });
        }
        catch
        {
            // Opening Explorer failing is not worth an error dialog.
        }
    }

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
