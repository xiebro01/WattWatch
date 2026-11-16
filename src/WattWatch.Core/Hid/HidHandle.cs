using System.Runtime.InteropServices;

namespace WattWatch.Core.Hid;

/// <summary>
/// A short-lived handle to one HID collection.
///
/// Design rule for this project: a handle is opened, used for exactly one
/// reading, and disposed. Holding a handle (or an input read) alive is what
/// keeps 2.4G receivers and their peripherals awake, so we never do it.
/// </summary>
public sealed class HidHandle : IDisposable
{
    private readonly IntPtr _handle;
    private readonly IntPtr _readEvent;
    private readonly IntPtr _writeEvent;
    private readonly object _gate = new();
    private bool _disposed;

    public HidInterfaceInfo Info { get; }

    /// <summary>False when another process (typically vendor software) owns the
    /// collection exclusively. Feature reports still work on a read handle, which
    /// is exactly what the Razer protocol needs.</summary>
    public bool CanWrite { get; }

    private HidHandle(IntPtr handle, HidInterfaceInfo info, bool canWrite)
    {
        _handle = handle;
        Info = info;
        CanWrite = canWrite;
        _readEvent = NativeMethods.CreateEvent(IntPtr.Zero, true, false, null);
        _writeEvent = NativeMethods.CreateEvent(IntPtr.Zero, true, false, null);
        if (_readEvent == IntPtr.Zero || _writeEvent == IntPtr.Zero)
            throw new IOException("CreateEvent failed: " + Marshal.GetLastWin32Error());
    }

    public static HidHandle Open(HidInterfaceInfo info)
    {
        // Three-step fallback, cheapest capability last:
        //  * read+write: needed by output-report protocols (VGN, Flydigi);
        //  * read only:  some vendor software grabs the write side;
        //  * zero access: Windows itself owns mouse/keyboard collections
        //    exclusively, but HidD_SetFeature/HidD_GetFeature still work on an
        //    access-less handle, which is all the Razer protocol needs.
        IntPtr handle = OpenNative(info.DevicePath, NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE);
        bool canWrite = handle != NativeMethods.InvalidHandleValue;

        if (!canWrite)
            handle = OpenNative(info.DevicePath, NativeMethods.GENERIC_READ);

        if (handle == NativeMethods.InvalidHandleValue)
            handle = OpenNative(info.DevicePath, 0);

        if (handle == NativeMethods.InvalidHandleValue)
            throw new IOException($"open failed ({Marshal.GetLastWin32Error()}): {info.DevicePath}");

        return new HidHandle(handle, info, canWrite);
    }

    private static IntPtr OpenNative(string path, uint access) =>
        NativeMethods.CreateFile(path, access,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero, NativeMethods.OPEN_EXISTING, NativeMethods.FILE_FLAG_OVERLAPPED, IntPtr.Zero);

    /// <summary>Send a feature report. Works on read-only handles as well.</summary>
    public bool SetFeature(byte reportId, ReadOnlySpan<byte> payload)
    {
        ThrowIfDisposed();
        int length = Math.Max(Info.FeatureReportLength, payload.Length + 1);
        var buffer = new byte[length];
        buffer[0] = reportId;
        payload.CopyTo(buffer.AsSpan(1));
        return NativeMethods.HidD_SetFeature(_handle, buffer, length);
    }

    /// <summary>Read a feature report; buffer[0] is the report id on return.</summary>
    public byte[]? GetFeature(byte reportId)
    {
        ThrowIfDisposed();
        int length = Info.FeatureReportLength;
        if (length <= 0) return null;
        var buffer = new byte[length];
        buffer[0] = reportId;
        return NativeMethods.HidD_GetFeature(_handle, buffer, length) ? buffer : null;
    }

    /// <summary>Write one output report (buffer[0] must be the report id).</summary>
    public bool Write(byte[] report, int timeoutMs = 500)
    {
        ThrowIfDisposed();
        int length = Info.OutputReportLength > 0 ? Info.OutputReportLength : report.Length;
        var buffer = new byte[length];
        Array.Copy(report, buffer, Math.Min(report.Length, length));

        IntPtr native = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.Copy(buffer, 0, native, length);
            var overlapped = new NativeMethods.Overlapped { HEvent = _writeEvent };
            lock (_gate)
            {
                NativeMethods.ResetEvent(_writeEvent);
                if (NativeMethods.WriteFile(_handle, native, length, out _, ref overlapped)) return true;

                int error = Marshal.GetLastWin32Error();
                if (error != NativeMethods.ERROR_IO_PENDING) return false;

                if (NativeMethods.WaitForSingleObject(_writeEvent, (uint)Math.Max(1, timeoutMs)) != 0)
                {
                    NativeMethods.CancelIoEx(_handle, ref overlapped);
                    NativeMethods.GetOverlappedResult(_handle, ref overlapped, out _, true);
                    return false;
                }

                return NativeMethods.GetOverlappedResult(_handle, ref overlapped, out _, false);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(native);
        }
    }

    /// <summary>Read one input report, or null if nothing arrived before the timeout.</summary>
    public byte[]? Read(int timeoutMs)
    {
        ThrowIfDisposed();
        int length = Info.InputReportLength;
        if (length <= 0) return null;

        IntPtr native = Marshal.AllocHGlobal(length);
        try
        {
            var overlapped = new NativeMethods.Overlapped { HEvent = _readEvent };
            lock (_gate)
            {
                NativeMethods.ResetEvent(_readEvent);
                if (NativeMethods.ReadFile(_handle, native, length, out uint read, ref overlapped))
                    return CopyOut(native, read);

                int error = Marshal.GetLastWin32Error();
                if (error != NativeMethods.ERROR_IO_PENDING) return null;

                if (NativeMethods.WaitForSingleObject(_readEvent, (uint)Math.Max(1, timeoutMs)) != 0)
                {
                    NativeMethods.CancelIoEx(_handle, ref overlapped);
                    NativeMethods.GetOverlappedResult(_handle, ref overlapped, out _, true);
                    return null;
                }

                if (!NativeMethods.GetOverlappedResult(_handle, ref overlapped, out read, false)) return null;
                return CopyOut(native, read);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(native);
        }
    }

    /// <summary>Drain input reports until the deadline; used by request/response protocols.</summary>
    public List<byte[]> ReadFor(TimeSpan window, int sliceMs = 120)
    {
        var reports = new List<byte[]>();
        DateTime deadline = DateTime.UtcNow + window;
        while (DateTime.UtcNow < deadline)
        {
            byte[]? report = Read(sliceMs);
            if (report != null) reports.Add(report);
        }
        return reports;
    }

    private static byte[] CopyOut(IntPtr pointer, uint length)
    {
        var array = new byte[length];
        if (length > 0) Marshal.Copy(pointer, array, 0, (int)length);
        return array;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HidHandle));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        NativeMethods.CloseHandle(_handle);
        if (_readEvent != IntPtr.Zero) NativeMethods.CloseHandle(_readEvent);
        if (_writeEvent != IntPtr.Zero) NativeMethods.CloseHandle(_writeEvent);
    }
}
