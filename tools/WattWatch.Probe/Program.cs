using System.Text;
using WattWatch.Core.Bluetooth;
using WattWatch.Core.Hid;
using WattWatch.Core.Protocols;
using WattWatch.Core.Sources;

namespace WattWatch.Probe;

/// <summary>
/// Console harness used while bring-up testing protocols against real hardware.
///
///   WattWatch.Probe list
///   WattWatch.Probe read
///   WattWatch.Probe dump 1532:00C1 3      (passive listen, sends nothing)
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string command = args.Length > 0 ? args[0].ToLowerInvariant() : "read";

        return command switch
        {
            "list" => ListDevices(),
            "read" => ReadAll(),
            "dump" => Dump(args),
            _ => Usage(),
        };
    }

    private static int Usage()
    {
        Console.WriteLine("usage: WattWatch.Probe [list|read|dump <vid:pid> [seconds]]");
        return 2;
    }

    private static IEnumerable<IBatterySource> Sources()
    {
        foreach (HidInterfaceInfo info in HidEnumerator.Enumerate())
        {
            IBatteryProtocol? protocol = ProtocolCatalog.Match(info);
            if (protocol != null) yield return new HidBatterySource(info, protocol);
        }

        foreach (BluetoothBatteryDevice device in BluetoothBatteryReader.Scan())
            if (device.Percent is not null) yield return new BluetoothBatterySource(device);
    }

    private static int ListDevices()
    {
        Console.WriteLine("=== HID collections ===");
        foreach (HidInterfaceInfo info in HidEnumerator.Enumerate())
        {
            IBatteryProtocol? protocol = ProtocolCatalog.Match(info);
            Console.WriteLine($"{info.VidPid}  up=0x{info.UsagePage:X4}/0x{info.Usage:X4}  " +
                              $"in={info.InputReportLength,3} out={info.OutputReportLength,3} feat={info.FeatureReportLength,3}  " +
                              $"{(protocol == null ? "-" : protocol.Id),-12} \"{info.Product}\"");
        }

        Console.WriteLine();
        Console.WriteLine("=== Bluetooth battery nodes ===");
        foreach (BluetoothBatteryDevice device in BluetoothBatteryReader.Scan())
            Console.WriteLine($"{device.Mac}  {(device.Percent?.ToString() ?? "--"),3}%  {device.Kind,-8} {device.Transport}  \"{device.Name}\"  ({device.Evidence})");

        return 0;
    }

    private static int ReadAll()
    {
        bool any = false;
        foreach (IBatterySource source in Sources())
        {
            any = true;
            Console.WriteLine($"--- {source.Name}  [{source.Id}]  {source.Transport}  key={source.Key}");
            try
            {
                ProtocolResult? result = source.Read();
                Console.WriteLine(result == null
                    ? "    result: no answer (device asleep?)"
                    : $"    result: {result.Clamped}%  {result.Charging}  ({result.Detail})");
            }
            catch (Exception ex)
            {
                Console.WriteLine("    error: " + ex.Message);
            }
        }

        if (!any) Console.WriteLine("no supported device found");
        return any ? 0 : 1;
    }

    private static int Dump(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("usage: WattWatch.Probe dump <vid:pid> [seconds]");
            return 2;
        }

        string[] parts = args[1].Split(':');
        ushort vid = Convert.ToUInt16(parts[0], 16);
        ushort pid = Convert.ToUInt16(parts[1], 16);
        int seconds = args.Length > 2 ? int.Parse(args[2]) : 3;

        foreach (HidInterfaceInfo info in HidEnumerator.Enumerate())
        {
            if (info.VendorId != vid || info.ProductId != pid) continue;

            Console.WriteLine($"{info.VidPid} up=0x{info.UsagePage:X4}/0x{info.Usage:X4} in={info.InputReportLength} out={info.OutputReportLength} feat={info.FeatureReportLength} \"{info.Product}\"");
            if (info.InputReportLength == 0) continue;

            try
            {
                using HidHandle handle = HidHandle.Open(info);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
                while (!cts.IsCancellationRequested)
                {
                    byte[]? report = handle.Read(200);
                    if (report != null) Console.WriteLine("    " + Convert.ToHexString(report));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("    error: " + ex.Message);
            }
        }

        return 0;
    }
}
