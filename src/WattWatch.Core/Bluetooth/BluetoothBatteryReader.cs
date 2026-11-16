using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using WattWatch.Core.Models;

namespace WattWatch.Core.Bluetooth;

/// <summary>One Bluetooth device that exposes a battery level through its PnP node.</summary>
public sealed record BluetoothBatteryDevice(
    string Mac,
    string Name,
    int Percent,
    DeviceKind Kind,
    string Transport,
    string Evidence);

/// <summary>
/// Reads battery levels that the Windows Bluetooth stack already publishes as a
/// device property (<c>DEVPKEY_Device_BatteryLevel</c>).
///
/// This covers two families without touching the radio:
///  * classic BR/EDR headsets, where the HFP gateway node carries the level the
///    headset reported over AT+IPHONEACCEV (EDIFIER W830NB verifies this);
///  * Bluetooth LE devices, where the node carries the GATT battery level.
///
/// No GATT connection is opened and no AT command is sent, so reading costs the
/// headset nothing.
/// </summary>
public static class BluetoothBatteryReader
{
    private const uint DIGCF_PRESENT = 0x02;
    private const uint DIGCF_ALLCLASSES = 0x04;
    private const uint SPDRP_FRIENDLYNAME = 0x0C;
    private const uint DEVPROP_TYPE_BYTE = 0x03;
    private const uint DEVPROP_TYPE_UINT16 = 0x05;
    private const uint DEVPROP_TYPE_UINT32 = 0x07;

    private static readonly IntPtr InvalidHandle = new(-1);

    private static readonly DevPropKey BatteryKey = new()
    {
        FmtId = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"),
        Pid = 2,
    };

    private static readonly Regex MacRegex = new(@"(?:DEV_|&)([0-9A-Fa-f]{12})(?=[_\\]|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ServiceUuidRegex = new(@"^BTHENUM\\\{([0-9A-Fa-f]{8})-",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] NameSuffixes =
    {
        " Hands-Free AG", " Hands-Free", " Avrcp Transport", " Avrcp 传输", " A2DP", " HFP", " AG",
    };

    private static readonly object CacheLock = new();
    private static IReadOnlyList<BluetoothBatteryDevice>? _cache;
    private static DateTimeOffset _cacheAt;

    public static IReadOnlyList<BluetoothBatteryDevice> Scan()
    {
        lock (CacheLock)
        {
            if (_cache != null && DateTimeOffset.Now - _cacheAt < TimeSpan.FromSeconds(3)) return _cache;
            _cache = ScanUncached();
            _cacheAt = DateTimeOffset.Now;
            return _cache;
        }
    }

    private static IReadOnlyList<BluetoothBatteryDevice> ScanUncached()
    {
        var nodes = new List<Node>();
        IntPtr set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
        if (set == InvalidHandle || set == IntPtr.Zero) return Array.Empty<BluetoothBatteryDevice>();

        try
        {
            var info = new SpDevInfoData { CbSize = (uint)Marshal.SizeOf<SpDevInfoData>() };
            var idBuffer = new StringBuilder(512);

            for (uint index = 0; SetupDiEnumDeviceInfo(set, index, ref info); index++)
            {
                idBuffer.Clear();
                if (!SetupDiGetDeviceInstanceIdW(set, ref info, idBuffer, idBuffer.Capacity, out _)) continue;

                string instanceId = idBuffer.ToString();
                bool classic = instanceId.StartsWith("BTHENUM\\", StringComparison.OrdinalIgnoreCase);
                bool lowEnergy = instanceId.StartsWith("BTHLE\\", StringComparison.OrdinalIgnoreCase)
                                 || instanceId.StartsWith("BTHLEDEVICE\\", StringComparison.OrdinalIgnoreCase);
                if (!classic && !lowEnergy) continue;

                Match macMatch = MacRegex.Match(instanceId);
                if (!macMatch.Success) continue;

                int? battery = ReadBattery(set, ref info);
                string name = ReadFriendlyName(set, ref info);
                Match service = ServiceUuidRegex.Match(instanceId);

                nodes.Add(new Node(
                    macMatch.Groups[1].Value.ToUpperInvariant(),
                    name,
                    battery,
                    classic ? "经典蓝牙" : "蓝牙 LE",
                    service.Success ? service.Groups[1].Value.ToLowerInvariant() : null));
            }
        }
        catch
        {
            // A failed property read must never break discovery.
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return Build(nodes);
    }

    private static IReadOnlyList<BluetoothBatteryDevice> Build(List<Node> nodes)
    {
        var result = new List<BluetoothBatteryDevice>();

        foreach (IGrouping<string, Node> group in nodes.GroupBy(n => n.Mac, StringComparer.OrdinalIgnoreCase))
        {
            List<Node> withBattery = group.Where(n => n.Battery is > 0 and <= 100).ToList();
            if (withBattery.Count == 0) continue;

            // Prefer the parent device node's name ("EDIFIER W830NB") over a
            // service child ("EDIFIER W830NB Hands-Free AG").
            string name = group
                .Where(n => n.Service == null && !string.IsNullOrWhiteSpace(n.Name))
                .Select(n => CleanName(n.Name))
                .FirstOrDefault(n => n.Length > 0)
                ?? group
                    .Select(n => n.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(CleanName)
                    .Where(n => n.Length > 0)
                    .OrderByDescending(n => n.Length)
                    .FirstOrDefault()
                ?? "蓝牙设备";

            Node carrier = withBattery.OrderByDescending(n => n.Battery).First();
            DeviceKind kind = ResolveKind(name, group.Where(n => n.Service != null).Select(n => n.Service!));

            result.Add(new BluetoothBatteryDevice(
                group.Key,
                name,
                carrier.Battery!.Value,
                kind,
                carrier.Transport,
                $"DEVPKEY_Device_BatteryLevel={carrier.Battery} node={carrier.Transport}/{carrier.Service ?? "-"}"));
        }

        // Two identical models (e.g. two Xbox pads) must not look like duplicates.
        var duplicatedNames = result
            .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (duplicatedNames.Count > 0)
        {
            result = result
                .Select(d => duplicatedNames.Contains(d.Name)
                    ? d with { Name = $"{d.Name} · {d.Mac[^4..]}" }
                    : d)
                .ToList();
        }

        return result;
    }

    private static string CleanName(string name)
    {
        string cleaned = name.Trim();
        foreach (string suffix in NameSuffixes)
        {
            if (cleaned.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned[..^suffix.Length].TrimEnd();
        }
        return cleaned.Trim();
    }

    private static DeviceKind ResolveKind(string name, IEnumerable<string> services)
    {
        var set = services.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (set.Contains("0000111e") || set.Contains("0000110b") || set.Contains("00001112"))
            return DeviceKind.Headset;

        string lower = name.ToLowerInvariant();
        if (lower.Contains("controller") || lower.Contains("gamepad") || lower.Contains("手柄") ||
            lower.Contains("dualsense") || lower.Contains("xbox")) return DeviceKind.Gamepad;
        if (lower.Contains("keyboard") || lower.Contains("键盘")) return DeviceKind.Keyboard;
        if (lower.Contains("mouse") || lower.Contains("鼠标")) return DeviceKind.Mouse;
        if (lower.Contains("buds") || lower.Contains("headset") || lower.Contains("耳机") ||
            lower.Contains("airpods") || lower.Contains("edifier") || lower.Contains("漫步者") ||
            lower.Contains("wh-") || lower.Contains("wf-")) return DeviceKind.Headset;
        return DeviceKind.Unknown;
    }

    private static string ReadFriendlyName(IntPtr set, ref SpDevInfoData info)
    {
        var buffer = new byte[1024];
        if (!SetupDiGetDeviceRegistryPropertyW(set, ref info, SPDRP_FRIENDLYNAME, out _, buffer, buffer.Length, out int required))
            return string.Empty;
        int length = Math.Max(0, required - 2);
        return Encoding.Unicode.GetString(buffer, 0, length).TrimEnd('\0').Trim();
    }

    private static int? ReadBattery(IntPtr set, ref SpDevInfoData info)
    {
        byte[]? raw = ReadProperty(set, ref info, out uint type);
        if (raw == null) return null;

        return type switch
        {
            DEVPROP_TYPE_BYTE => raw.Length > 0 ? raw[0] : null,
            DEVPROP_TYPE_UINT16 => raw.Length >= 2 ? BitConverter.ToUInt16(raw, 0) : null,
            DEVPROP_TYPE_UINT32 => raw.Length >= 4 ? (int)BitConverter.ToUInt32(raw, 0) : null,
            _ => null,
        };
    }

    private static byte[]? ReadProperty(IntPtr set, ref SpDevInfoData info, out uint type)
    {
        type = 0;
        DevPropKey key = BatteryKey;
        SetupDiGetDevicePropertyW(set, ref info, ref key, out _, null, 0, out int required, 0);
        if (required <= 0) return null;

        var buffer = new byte[required];
        return SetupDiGetDevicePropertyW(set, ref info, ref key, out type, buffer, required, out _, 0)
            ? buffer
            : null;
    }

    private sealed record Node(string Mac, string Name, int? Battery, string Transport, string? Service);

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevInfoData
    {
        public uint CbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey
    {
        public Guid FmtId;
        public uint Pid;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SpDevInfoData info);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref SpDevInfoData info,
        StringBuilder instanceId, int instanceIdSize, out int requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref SpDevInfoData info,
        uint property, out uint propertyType, byte[]? buffer, int bufferSize, out int requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SpDevInfoData info,
        ref DevPropKey key, out uint propertyType, byte[]? buffer, int bufferSize, out int requiredSize, uint flags);

    [DllImport("setupapi.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
}
