using System.ComponentModel;
using System.Windows.Media;
using WattWatch.Core.Models;
using WattWatch.Core.Monitor;

namespace WattWatch.App.ViewModels;

/// <summary>One card in the device list.</summary>
public sealed class DeviceViewModel : INotifyPropertyChanged
{
    private static readonly Brush Good = Freeze("#3DDC97");
    private static readonly Brush Warn = Freeze("#F5B942");
    private static readonly Brush Bad = Freeze("#FF6259");
    private static readonly Brush Muted = Freeze("#6B7488");

    private static readonly Geometry MouseIcon = Geometry.Parse(
        "M12,3 C8.1,3 6,6.1 6,10 L6,15 C6,19 8.1,21 12,21 C15.9,21 18,19 18,15 L18,10 C18,6.1 15.9,3 12,3 Z M12,3 L12,9.5");

    private static readonly Geometry KeyboardIcon = Geometry.Parse(
        "M3,7 L21,7 L21,17 L3,17 Z M6,14 L18,14 M6.5,10.5 L7,10.5 M9.5,10.5 L10,10.5 M12.5,10.5 L13,10.5 " +
        "M15.5,10.5 L16,10.5 M18,10.5 L18.4,10.5");

    private static readonly Geometry GamepadIcon = Geometry.Parse(
        "M7.5,8 L16.5,8 C19,8 21,10.2 21,13 C21,15.8 19.2,17.4 17.2,16.6 L15.4,15.8 L8.6,15.8 L6.8,16.6 " +
        "C4.8,17.4 3,15.8 3,13 C3,10.2 5,8 7.5,8 Z M8.2,11 L8.2,13.4 M7,12.2 L9.4,12.2 M15.4,11.6 L15.5,11.6 " +
        "M17.4,12.8 L17.5,12.8");

    private static readonly Geometry HeadsetIcon = Geometry.Parse(
        "M4,15 L4,12 C4,7.6 7.6,4 12,4 C16.4,4 20,7.6 20,12 L20,15 " +
        "M4,15 L6.4,15 L6.4,19.5 L4,19.5 Z M20,15 L17.6,15 L17.6,19.5 L20,19.5 Z");

    private static readonly Geometry UnknownIcon = Geometry.Parse("M12,4 A8,8 0 1 1 11.9,4 Z");

    private readonly BatteryDevice _device;
    private string _remaining = string.Empty;

    public DeviceViewModel(BatteryDevice device)
    {
        _device = device;
        Icon = device.Kind switch
        {
            DeviceKind.Mouse => MouseIcon,
            DeviceKind.Keyboard => KeyboardIcon,
            DeviceKind.Gamepad => GamepadIcon,
            DeviceKind.Headset => HeadsetIcon,
            _ => UnknownIcon,
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public BatteryDevice Device => _device;

    public string Key => _device.Key;

    public string Name => _device.Name;

    public Geometry Icon { get; }

    public int? Percent => _device.Percent;

    public string PercentText => _device.Percent?.ToString() ?? "--";

    public double BarWidth => Math.Clamp(_device.Percent ?? 0, 0, 100) * 0.96;

    /// <summary>Dim the card while the device is not answering.</summary>
    public double CardOpacity => _device.IsOnline ? 1.0 : 0.55;

    public Brush Accent => !_device.IsOnline
        ? Muted
        : _device.Percent switch
        {
            null => Muted,
            >= 50 => Good,
            >= 20 => Warn,
            _ => Bad,
        };

    public string Subtitle => _device.Transport;

    public string StateText
    {
        get
        {
            string age = Age(_device.LastReadAt);
            if (!_device.IsOnline)
                return _device.LastReadAt == null ? "未连接" : $"未连接 · 上次 {age}";
            if (_device.LastError != null && _device.Percent == null)
                return $"无应答 · {age}";
            string charging = _device.Charging switch
            {
                ChargingState.Charging => "充电中",
                ChargingState.Full => "已充满",
                ChargingState.Discharging => "放电中",
                _ => "状态未知",
            };
            return $"{charging} · {age}";
        }
    }

    public string RemainingText
    {
        get => _remaining;
        set
        {
            if (_remaining == value) return;
            _remaining = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RemainingText)));
        }
    }

    public string Tooltip =>
        $"{Name}\n传输：{_device.Transport}\n协议：{_device.ProtocolLabel}\n" +
        (string.IsNullOrEmpty(_device.LastError) ? "最近读数正常" : "最近错误：" + _device.LastError);

    private static string Age(DateTimeOffset? when)
    {
        if (when == null) return "尚未读取";
        TimeSpan delta = DateTimeOffset.Now - when.Value;
        if (delta < TimeSpan.FromMinutes(1)) return "刚刚";
        if (delta < TimeSpan.FromHours(1)) return $"{(int)delta.TotalMinutes} 分钟前";
        if (delta < TimeSpan.FromDays(1)) return $"{(int)delta.TotalHours} 小时前";
        return $"{(int)delta.TotalDays} 天前";
    }

    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    private static Brush Freeze(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
