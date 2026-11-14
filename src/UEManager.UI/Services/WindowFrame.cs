using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace UEManager.UI.Services;

/// <summary>
/// Gives a chromeless window its Windows shadow and dark frame through the Desktop Window Manager, while keeping the
/// sharp corners of the app's look.
/// </summary>
internal static class WindowFrame
{
    /// <summary>DWMWA_NCRENDERING_POLICY: turns non-client rendering, and with it the window shadow, on.</summary>
    private const int NonClientRenderingPolicy = 2;
    private const int RenderingEnabled = 2;

    /// <summary>DWMWA_USE_IMMERSIVE_DARK_MODE: dark frame and heavier shadow on Windows 10 (2004+) and Windows 11.</summary>
    private const int ImmersiveDarkMode = 20;

    /// <summary>DWMWA_WINDOW_CORNER_PREFERENCE: keeps sharp corners on Windows 11 while the shadow stays on.</summary>
    private const int WindowCornerPreference = 33;
    private const int DoNotRound = 1;

    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
                Apply(handle);
        };
    }

    internal static bool Apply(IntPtr handle)
    {
        var policy = RenderingEnabled;
        var policyResult = DwmSetWindowAttribute(handle, NonClientRenderingPolicy, ref policy, sizeof(int));

        var dark = 1;
        _ = DwmSetWindowAttribute(handle, ImmersiveDarkMode, ref dark, sizeof(int));

        var corners = DoNotRound;
        _ = DwmSetWindowAttribute(handle, WindowCornerPreference, ref corners, sizeof(int));

        var margins = new Margins { Left = 1, Right = 1, Top = 1, Bottom = 1 };
        var frameResult = DwmExtendFrameIntoClientArea(handle, ref margins);
        return policyResult == 0 && frameResult == 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
}
