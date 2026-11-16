using WattWatch.Core.Models;
using WattWatch.Core.Sources;

namespace WattWatch.Core.Monitor;

/// <summary>Runtime state of one monitored device.</summary>
public sealed class BatteryDevice
{
    public required IBatterySource Source { get; set; }

    public string Key => Source.Key;

    public string Name => Source.Name;

    public DeviceKind Kind => Source.Kind;

    public string Transport => Source.Transport;

    public string ProtocolLabel => Source.ProtocolLabel;

    public bool RequiresWrite => Source.RequiresWrite;

    public int? Percent { get; set; }

    public ChargingState Charging { get; set; } = ChargingState.Unknown;

    public DateTimeOffset? LastReadAt { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    public string? LastError { get; set; }

    public int Failures { get; set; }

    public DateTimeOffset NextDueAt { get; set; }
}
