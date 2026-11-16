using WattWatch.Core.Bluetooth;
using WattWatch.Core.Models;
using WattWatch.Core.Protocols;

namespace WattWatch.Core.Sources;

/// <summary>
/// Battery that Windows already knows from the Bluetooth stack. Reading it is a
/// devnode property query: no GATT connection, no AT command, zero radio traffic.
/// </summary>
public sealed class BluetoothBatterySource : IBatterySource
{
    private readonly BluetoothBatteryDevice _device;

    public BluetoothBatterySource(BluetoothBatteryDevice device) => _device = device;

    public string Mac => _device.Mac;

    public string Id => "bt-pnp";

    public string Key => "bt:" + _device.Mac;

    public string Name => _device.Name;

    public DeviceKind Kind => _device.Kind;

    public string Transport => _device.Transport;

    public string ProtocolLabel => "蓝牙系统电量属性";

    public bool RequiresWrite => false;

    // Property reads are free, so this can be a bit fresher than the 2.4G cadence.
    public TimeSpan? PreferredInterval => TimeSpan.FromMinutes(2);

    public ProtocolResult? Read()
    {
        BluetoothBatteryDevice? current = BluetoothBatteryReader.Scan()
            .FirstOrDefault(d => string.Equals(d.Mac, Mac, StringComparison.OrdinalIgnoreCase));

        return current?.Percent is int percent
            ? new ProtocolResult(percent, ChargingState.Unknown, current.Evidence)
            : null;
    }
}
