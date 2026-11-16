using WattWatch.Core.Models;
using WattWatch.Core.Protocols;

namespace WattWatch.Core.Sources;

/// <summary>
/// One readable battery, whatever the transport. Implementations must do their
/// own (short-lived) I/O so that the monitor never holds a device open.
/// </summary>
public interface IBatterySource
{
    /// <summary>Protocol id, e.g. "razer-90b" or "bt-pnp".</summary>
    string Id { get; }

    /// <summary>Stable identity for history (survives replugging).</summary>
    string Key { get; }

    string Name { get; }

    DeviceKind Kind { get; }

    /// <summary>"2.4G 鎺ユ敹鍣? / "缁忓吀钃濈墮" / "钃濈墮 LE".</summary>
    string Transport { get; }

    /// <summary>Protocol label shown in the UI, e.g. "闆疯泧 90 瀛楄妭绉佹湁鍗忚".</summary>
    string ProtocolLabel { get; }

    /// <summary>True when reading costs a radio wake-up on the peripheral.</summary>
    bool RequiresWrite { get; }

    /// <summary>Per-source poll cadence. Null = use the global setting.</summary>
    TimeSpan? PreferredInterval { get; }

    ProtocolResult? Read();
}

