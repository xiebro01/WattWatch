using System.Runtime.InteropServices;

namespace WattWatch.App.Services;

/// <summary>
/// One instance per session. A second launch broadcasts a private window message
/// so the already-running copy shows its window instead of a second tray icon.
/// </summary>
public static class SingleInstance
{
    private const string MutexName = "WattWatch.SingleInstance.v1";
    private static readonly uint ShowMessage = RegisterWindowMessage("WattWatch.ShowWindow.v1");

    public static bool TryAcquire(out Mutex mutex)
    {
        mutex = new Mutex(true, MutexName, out bool created);
        return created;
    }

    public static void SignalExisting() =>
        PostMessage(new IntPtr(0xFFFF), ShowMessage, IntPtr.Zero, IntPtr.Zero);

    public static bool IsShowMessage(int message) => message == unchecked((int)ShowMessage);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
