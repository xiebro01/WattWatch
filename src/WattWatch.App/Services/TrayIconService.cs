using System.Drawing;
using System.Windows.Forms;
using WattWatch.Core.Monitor;

namespace WattWatch.App.Services;

/// <summary>
/// Tray presence: a dynamic battery glyph, a dark context menu and balloon tips
/// for low battery. Everything is event driven; the tray does not poll.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _autostartItem;
    private Icon _currentIcon;
    private int? _currentLevel;
    private bool _disposed;

    public TrayIconService(bool autostartEnabled)
    {
        _autostartItem = new ToolStripMenuItem("开机自启动") { Checked = autostartEnabled, CheckOnClick = true };
        _autostartItem.Click += (_, _) => AutostartToggled?.Invoke(_autostartItem.Checked);

        _menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            Font = new Font("Microsoft YaHei UI", 9f),
            ForeColor = Color.FromArgb(226, 232, 240),
            Renderer = new ToolStripProfessionalRenderer(new DarkColorTable()) { RoundedEdges = false },
            BackColor = Color.FromArgb(22, 27, 36),
        };
        _menu.Items.Add(new ToolStripMenuItem("打开 WattWatch", null, (_, _) => OpenRequested?.Invoke()));
        _menu.Items.Add(new ToolStripMenuItem("立即刷新", null, (_, _) => RefreshRequested?.Invoke()));
        _menu.Items.Add(new ToolStripMenuItem("设置", null, (_, _) => SettingsRequested?.Invoke()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_autostartItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => ExitRequested?.Invoke()));

        _currentIcon = TrayIconFactory.Create(null);
        _notifyIcon = new NotifyIcon
        {
            Icon = _currentIcon,
            Visible = true,
            Text = "WattWatch",
            ContextMenuStrip = _menu,
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) OpenRequested?.Invoke();
        };
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public event Action? OpenRequested;

    public event Action? RefreshRequested;

    public event Action? SettingsRequested;

    public event Action? ExitRequested;

    public event Action<bool>? AutostartToggled;

    public void Update(IReadOnlyList<BatteryDevice> devices, bool autostart)
    {
        if (_disposed) return;

        _autostartItem.Checked = autostart;

        int? lowest = devices.Where(d => d.Percent.HasValue).Select(d => d.Percent).Min();
        if (lowest != _currentLevel)
        {
            _currentLevel = lowest;
            Icon next = TrayIconFactory.Create(lowest);
            _notifyIcon.Icon = next;
            _currentIcon.Dispose();
            _currentIcon = next;
        }

        _notifyIcon.Text = BuildText(devices, lowest);
    }

    public void ShowLowBattery(BatteryDevice device)
    {
        if (_disposed) return;
        _notifyIcon.ShowBalloonTip(6000, "电量偏低",
            $"{device.Name} 剩余 {device.Percent ?? 0}%，记得充电。", ToolTipIcon.Warning);
    }

    public void ShowMessage(string title, string text)
    {
        if (_disposed) return;
        _notifyIcon.ShowBalloonTip(4000, title, text, ToolTipIcon.Info);
    }

    private static string BuildText(IReadOnlyList<BatteryDevice> devices, int? lowest)
    {
        if (devices.Count == 0) return "WattWatch · 未发现设备";

        string body = string.Join(" · ", devices
            .Select(d => $"{ShortName(d.Name)} {d.Percent?.ToString() ?? "--"}%"));

        string text = $"WattWatch · 最低 {lowest?.ToString() ?? "--"}%\n{body}";
        return text.Length <= 120 ? text : text[..119] + "…";
    }

    private static string ShortName(string name)
    {
        string cleaned = name
            .Replace("Razer ", string.Empty)
            .Replace("Wireless Controller", "手柄")
            .Replace("VGN ", string.Empty)
            .Replace("EDIFIER ", "漫步者 ");
        return cleaned.Length <= 14 ? cleaned : cleaned[..13] + "…";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _currentIcon.Dispose();
    }

    /// <summary>Dark colour table so the native menu does not flash a white box.</summary>
    private sealed class DarkColorTable : ProfessionalColorTable
    {
        private static readonly Color Surface = Color.FromArgb(22, 27, 36);
        private static readonly Color Hover = Color.FromArgb(33, 40, 52);

        public override Color ToolStripDropDownBackground => Surface;

        public override Color ImageMarginGradientBegin => Surface;

        public override Color ImageMarginGradientMiddle => Surface;

        public override Color ImageMarginGradientEnd => Surface;

        public override Color MenuBorder => Color.FromArgb(45, 54, 69);

        public override Color MenuItemBorder => Color.FromArgb(45, 54, 69);

        public override Color MenuItemSelected => Hover;

        public override Color MenuItemSelectedGradientBegin => Hover;

        public override Color MenuItemSelectedGradientEnd => Hover;

        public override Color MenuItemPressedGradientBegin => Hover;

        public override Color MenuItemPressedGradientEnd => Hover;

        public override Color SeparatorDark => Color.FromArgb(40, 47, 60);

        public override Color SeparatorLight => Color.FromArgb(40, 47, 60);
    }
}
