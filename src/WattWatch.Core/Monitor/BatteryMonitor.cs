using WattWatch.Core.Bluetooth;
using WattWatch.Core.Hid;
using WattWatch.Core.Models;
using WattWatch.Core.Protocols;
using WattWatch.Core.Sources;
using WattWatch.Core.Storage;

namespace WattWatch.Core.Monitor;

/// <summary>
/// Owns discovery and the polling schedule.
///
/// Battery policy (the reason this project exists):
///  * a 2.4G reading is taken on discovery, on manual refresh and at most once per
///    <see cref="PollInterval"/> otherwise;
///  * every reading opens and closes its own HID handle, so no read stays queued
///    on an idle device;
///  * Bluetooth levels come from the devnode property Windows already has, which
///    touches no radio at all;
///  * no BLE scanning, no input-report subscription, no GATT connection;
///  * failures back off exponentially instead of hammering a sleeping peripheral;
///  * the device tree is re-walked every 15 s, which is free (metadata only).
/// </summary>
public sealed class BatteryMonitor : IDisposable
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(15);

    private readonly SettingsStore _settingsStore;
    private readonly HistoryStore _history;
    private readonly SemaphoreSlim _wake = new(0);
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, BatteryDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _random = new();
    private Task? _loop;
    private DateTimeOffset _nextScanAt = DateTimeOffset.MinValue;
    private bool _disposed;

    public BatteryMonitor(SettingsStore? settingsStore = null, HistoryStore? historyStore = null)
    {
        _settingsStore = settingsStore ?? new SettingsStore();
        _history = historyStore ?? new HistoryStore();
        Settings = _settingsStore.Load();
    }

    public AppSettings Settings { get; private set; }

    public TimeSpan PollInterval => TimeSpan.FromMinutes(Math.Clamp(Settings.PollIntervalMinutes, 1, 240));

    public DateTimeOffset? LastScanAt { get; private set; }

    public string Status { get; private set; } = "正在初始化…";

    /// <summary>Vendor devices that are present but have no battery reader yet.</summary>
    public IReadOnlyList<OtherDevice> OtherDevices { get; private set; } = Array.Empty<OtherDevice>();

    public event Action? DevicesChanged;

    public event Action<BatteryDevice>? DeviceUpdated;

    public event Action<BatteryDevice>? LowBattery;

    public event Action? StatusChanged;

    public IReadOnlyList<BatteryDevice> Devices
    {
        get
        {
            lock (_gate)
            {
                return _devices.Values
                    .OrderBy(d => d.Kind)
                    .ThenBy(d => d.Name, StringComparer.CurrentCulture)
                    .ToList();
            }
        }
    }

    public void Start()
    {
        _loop ??= Task.Run(RunAsync);
    }

    /// <summary>Mark every device due and wake the loop.</summary>
    public void RefreshNow()
    {
        lock (_gate)
        {
            foreach (BatteryDevice device in _devices.Values) device.NextDueAt = DateTimeOffset.Now;
        }
        Wake();
    }

    public void RefreshDevice(string key)
    {
        lock (_gate)
        {
            if (_devices.TryGetValue(key, out BatteryDevice? device)) device.NextDueAt = DateTimeOffset.Now;
        }
        Wake();
    }

    public void ApplySettings(AppSettings settings)
    {
        Settings = settings;
        _settingsStore.Save(settings);
        lock (_gate)
        {
            DateTimeOffset cap = DateTimeOffset.Now + PollInterval;
            foreach (BatteryDevice device in _devices.Values)
                if (device.NextDueAt > cap) device.NextDueAt = cap;
        }
        Wake();
        StatusChanged?.Invoke();
    }

    public IReadOnlyList<HistoryPoint> HistoryFor(string deviceKey, TimeSpan window) => _history.Recent(deviceKey, window);

    private void Wake() => _wake.Release();

    private async Task RunAsync()
    {
        try
        {
            // Let the UI settle before the first radio wake-up.
            await Task.Delay(900, _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.Now >= _nextScanAt) Scan();

                foreach (BatteryDevice device in TakeDue())
                {
                    if (_cts.IsCancellationRequested) return;
                    ReadOne(device);
                    await Task.Delay(150, _cts.Token).ConfigureAwait(false);
                }

                await _wake.WaitAsync(ComputeWait(), _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                SetStatus("监控循环异常：" + ex.Message);
                try { await Task.Delay(5000, _cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private void Scan()
    {
        var sources = new List<IBatterySource>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<HidInterfaceInfo> interfaces = HidEnumerator.Enumerate();
        foreach (HidInterfaceInfo info in interfaces)
        {
            IBatteryProtocol? protocol = ProtocolCatalog.Match(info);
            if (protocol == null) continue;

            sources.Add(new HidBatterySource(info, protocol));
            claimed.Add(info.VidPid);
        }

        var bluetoothOthers = new List<OtherDevice>();
        foreach (BluetoothBatteryDevice device in BluetoothBatteryReader.Scan())
        {
            if (device.Percent is not null)
            {
                sources.Add(new BluetoothBatterySource(device));
                continue;
            }

            // Audio gear that is connected but has no cached level: list it so the
            // user can tell "not seen" from "seen, but Windows has no level".
            if (device.IsAudio)
                bluetoothOthers.Add(new OtherDevice(device.Name, device.Mac, device.Transport, "系统没有缓存电量"));
        }

        OtherDevices = CollectOthers(interfaces, claimed)
            .Concat(bluetoothOthers)
            .OrderBy(o => o.Name, StringComparer.CurrentCulture)
            .ToList();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool changed = false;

        lock (_gate)
        {
            foreach (IBatterySource source in sources)
            {
                if (!seen.Add(source.Key)) continue;

                if (_devices.TryGetValue(source.Key, out BatteryDevice? existing))
                {
                    bool pathChanged = existing.Source is HidBatterySource oldHid
                                       && source is HidBatterySource newHid
                                       && !string.Equals(oldHid.Info.DevicePath, newHid.Info.DevicePath,
                                           StringComparison.OrdinalIgnoreCase);

                    existing.Source = source;
                    if (pathChanged)
                    {
                        existing.NextDueAt = DateTimeOffset.Now + TimeSpan.FromMilliseconds(400);
                        changed = true;
                    }
                    continue;
                }

                _devices[source.Key] = new BatteryDevice
                {
                    Source = source,
                    NextDueAt = DateTimeOffset.Now + TimeSpan.FromMilliseconds(400),
                };
                changed = true;
            }

            foreach (string key in _devices.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _devices.Remove(key);
                changed = true;
            }
        }

        LastScanAt = DateTimeOffset.Now;
        _nextScanAt = DateTimeOffset.Now + ScanInterval;
        if (changed) DevicesChanged?.Invoke();
        SetStatus(Devices.Count == 0 ? "未发现支持的设备" : $"已发现 {Devices.Count} 台设备");
    }

    private List<BatteryDevice> TakeDue()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        lock (_gate)
        {
            return _devices.Values.Where(d => d.NextDueAt <= now).ToList();
        }
    }

    private TimeSpan ComputeWait()
    {
        lock (_gate)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            TimeSpan wait = _nextScanAt - now;

            if (_devices.Count > 0)
            {
                TimeSpan due = _devices.Values.Min(d => d.NextDueAt) - now;
                if (due < wait) wait = due;
            }

            if (wait < TimeSpan.FromSeconds(2)) wait = TimeSpan.FromSeconds(2);
            if (wait > ScanInterval) wait = ScanInterval;
            return wait;
        }
    }

    /// <summary>
    /// Devices that expose a vendor collection but no battery protocol we know.
    /// Grouped per model so one receiver's ten interfaces stay one row.
    /// </summary>
    private static IReadOnlyList<OtherDevice> CollectOthers(IReadOnlyList<HidInterfaceInfo> interfaces,
        HashSet<string> claimedVidPid)
    {
        var others = new List<OtherDevice>();

        foreach (IGrouping<string, HidInterfaceInfo> group in interfaces
                     .Where(i => i.UsagePage >= 0xFF00 && !string.IsNullOrWhiteSpace(i.Product))
                     .GroupBy(i => i.VidPid + "|" + i.Product, StringComparer.OrdinalIgnoreCase))
        {
            HidInterfaceInfo first = group.First();
            if (claimedVidPid.Contains(first.VidPid)) continue;

            others.Add(new OtherDevice(
                first.Product.Trim(),
                first.VidPid,
                first.TransportLabel,
                "未适配电量协议"));
        }

        return others
            .OrderBy(o => o.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    private void ReadOne(BatteryDevice device)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        device.LastAttemptAt = now;

        ProtocolResult? reading = null;
        string? error = null;
        try
        {
            reading = device.Source.Read();
            if (reading == null) error = "设备无应答（可能处于休眠）";
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        TimeSpan interval = device.Source.PreferredInterval ?? PollInterval;

        if (reading is { } result)
        {
            int? previous = device.Percent;
            bool changed = previous != result.Clamped || device.Charging != result.Charging;

            device.Percent = result.Clamped;
            device.Charging = result.Charging;
            device.LastReadAt = now;
            device.LastError = null;
            device.Failures = 0;
            device.NextDueAt = now + Jitter(interval);

            _history.Append(device.Key, result.Clamped, result.Charging, now);

            if (changed || previous == null) DeviceUpdated?.Invoke(device);
            if (previous is { } before && before > Settings.LowBatteryThreshold
                && result.Clamped <= Settings.LowBatteryThreshold
                && result.Charging is ChargingState.Discharging or ChargingState.Unknown)
                LowBattery?.Invoke(device);

            SetStatus($"已读取 {device.Name}：{result.Clamped}%（{result.Detail}）");
        }
        else
        {
            device.Failures++;
            device.LastError = error;
            // Never retry a silent peripheral sooner than a full interval: retrying
            // is exactly what wakes a sleeping 2.4G mouse.
            double multiplier = Math.Pow(2, Math.Min(device.Failures, 4));
            TimeSpan delay = TimeSpan.FromTicks(Math.Min((long)(interval.Ticks * multiplier), TimeSpan.FromHours(4).Ticks));
            device.NextDueAt = now + delay;

            DeviceUpdated?.Invoke(device);
            SetStatus($"{device.Name} 未应答（第 {device.Failures} 次），已退避");
        }
    }

    private TimeSpan Jitter(TimeSpan interval)
    {
        double factor;
        lock (_random) factor = 0.9 + _random.NextDouble() * 0.2;
        return TimeSpan.FromTicks((long)(interval.Ticks * factor));
    }

    private void SetStatus(string status)
    {
        if (Status == status) return;
        Status = status;
        StatusChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _wake.Release();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        _cts.Dispose();
        _wake.Dispose();
    }
}
