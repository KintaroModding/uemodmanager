using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace UEManager.UI.Services;

internal static class Shell
{
    /// <summary>Opens a folder in Explorer, or a file in its default app (Notepad when none is set).</summary>
    public static void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Win32Exception) when (File.Exists(path))
        {
            Process.Start("notepad.exe", $"\"{path}\"");
        }
    }
}
