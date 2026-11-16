using WattWatch.Core.Hid;
using WattWatch.Core.Models;

namespace WattWatch.Core.Protocols;

/// <summary>One successful reading, plus the raw evidence used to produce it.</summary>
public sealed record ProtocolResult(int Percent, ChargingState Charging, string Detail)
{
    public int Clamped => Math.Clamp(Percent, 0, 100);
}

/// <summary>
/// A battery protocol for one family of 2.4G devices. Implementations are
/// responsible for mentioning in <see cref="RequiresWrite"/> whether reading
/// costs a radio wake-up on the peripheral side.
/// </summary>
public interface IBatteryProtocol
{
    string Id { get; }

    string DisplayName { get; }

    /// <summary>True when the protocol has to send a query frame to get an answer.</summary>
    bool RequiresWrite { get; }

    bool Matches(HidInterfaceInfo info);

    /// <summary>Read the battery once. Returns null when the device is asleep or silent.</summary>
    ProtocolResult? Read(HidHandle handle);

    string FriendlyName(HidInterfaceInfo info);

    DeviceKind Kind(HidInterfaceInfo info);
}
