using System.Runtime.InteropServices;
using System.Text;

namespace WattWatch.Core.Hid;

/// <summary>
/// Enumerates HID top-level collections. Only metadata is read: the device is
/// opened with read access and closed immediately, so a sleeping 2.4G peripheral
/// is never woken up by a scan. Results are cached per device path, which makes a
/// rescan a pure SetupAPI walk (~5 ms) with no device I/O at all.
/// </summary>
public static class HidEnumerator
{
    private static readonly Dictionary<string, HidInterfaceInfo> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();

    public static IReadOnlyList<HidInterfaceInfo> Enumerate()
    {
        var results = new List<HidInterfaceInfo>();

        NativeMethods.HidD_GetHidGuid(out Guid hidGuid);
        IntPtr deviceInfoSet = NativeMethods.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
            NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);
        if (deviceInfoSet == NativeMethods.InvalidHandleValue) return results;

        int detailSize = Marshal.SizeOf<NativeMethods.SpDeviceInterfaceDetailData>();
        IntPtr detailBuffer = Marshal.AllocHGlobal(detailSize);
        try
        {
            for (uint index = 0; ; index++)
            {
                var interfaceData = new NativeMethods.SpDeviceInterfaceData
                {
                    CbSize = Marshal.SizeOf<NativeMethods.SpDeviceInterfaceData>(),
                };

                if (!NativeMethods.SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
                    break;

                // cbSize is 8 on x64 and 6 on x86; the path itself starts at offset 4.
                Marshal.WriteInt32(detailBuffer, 0, IntPtr.Size == 8 ? 8 : 6);
                if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData,
                        detailBuffer, detailSize, out _, IntPtr.Zero))
                    continue;

                string path = Marshal.PtrToStringUni(IntPtr.Add(detailBuffer, 4)) ?? string.Empty;
                if (path.Length == 0) continue;

                // kbdhid/mouhid publish a second interface path for the same
                // collection ("...\kbd", "...\mouse"). Skip those aliases.
                if (path.EndsWith("\\kbd", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith("\\mouse", StringComparison.OrdinalIgnoreCase)) continue;

                HidInterfaceInfo? info = GetOrDescribe(path);
                if (info != null) results.Add(info);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(detailBuffer);
            NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return results;
    }

    private static HidInterfaceInfo? GetOrDescribe(string path)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(path, out HidInterfaceInfo? cached)) return cached;
        }

        HidInterfaceInfo? info = Describe(path);
        if (info == null) return null;

        lock (CacheLock)
        {
            Cache[path] = info;
        }
        return info;
    }

    private static HidInterfaceInfo? Describe(string path)
    {
        // Read access is required for HidD_GetPreparsedData, and it is all we need:
        // querying caps/strings generates no traffic towards the peripheral.
        IntPtr handle = NativeMethods.CreateFile(path, NativeMethods.GENERIC_READ,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == NativeMethods.InvalidHandleValue)
            handle = NativeMethods.CreateFile(path, 0,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == NativeMethods.InvalidHandleValue) return null;

        try
        {
            var attributes = new NativeMethods.HiddAttributes { Size = Marshal.SizeOf<NativeMethods.HiddAttributes>() };
            if (!NativeMethods.HidD_GetAttributes(handle, ref attributes)) return null;

            var caps = new NativeMethods.HidpCaps();
            if (NativeMethods.HidD_GetPreparsedData(handle, out IntPtr preparsed))
            {
                try
                {
                    if (NativeMethods.HidP_GetCaps(preparsed, ref caps) != NativeMethods.HIDP_STATUS_SUCCESS)
                        caps = new NativeMethods.HidpCaps();
                }
                finally
                {
                    NativeMethods.HidD_FreePreparsedData(preparsed);
                }
            }

            return new HidInterfaceInfo(
                path,
                attributes.VendorID,
                attributes.ProductID,
                attributes.VersionNumber,
                ReadString(NativeMethods.HidD_GetProductString, handle),
                ReadString(NativeMethods.HidD_GetManufacturerString, handle),
                ReadString(NativeMethods.HidD_GetSerialNumberString, handle),
                caps.UsagePage,
                caps.Usage,
                caps.InputReportByteLength,
                caps.OutputReportByteLength,
                caps.FeatureReportByteLength);
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private delegate bool StringGetter(IntPtr device, byte[] buffer, int bufferLength);

    private static string ReadString(StringGetter getter, IntPtr handle)
    {
        var buffer = new byte[512];
        if (!getter(handle, buffer, buffer.Length)) return string.Empty;
        string value = Encoding.Unicode.GetString(buffer);
        int nul = value.IndexOf('\0');
        return (nul >= 0 ? value[..nul] : value).Trim();
    }
}
