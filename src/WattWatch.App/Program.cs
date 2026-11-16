using System.Runtime.InteropServices;

namespace WattWatch.App;

/// <summary>
/// Custom entry point: the process must be Per-Monitor-V2 DPI aware *before*
/// WPF creates its first visual, otherwise SystemParameters.WorkArea reports
/// physical pixels and the window lands half off-screen on scaled displays.
/// The WinForms SDK strips DPI settings from the app manifest, so it is set here.
/// </summary>
internal static class Program
{
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    [STAThread]
    private static void Main()
    {
        try
        {
            SetProcessDpiAwarenessContext(PerMonitorAwareV2);
        }
        catch
        {
            // Older Windows builds: fall back to the manifest / system DPI.
        }

        var application = new App();
        application.InitializeComponent();
        application.Run();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
}
