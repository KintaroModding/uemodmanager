using System.ComponentModel;
using System.Diagnostics;

namespace UEManager.Core.Games;

/// <summary>Finds the running game, so that no file is changed while the game uses it.</summary>
public static class GameProcess
{
    /// <summary>Whether an executable from the game's Win64 folder is running.</summary>
    public static bool IsRunning(GameInstall game)
    {
        string[] executables;
        try
        {
            executables = Directory.Exists(game.BinariesPath) ? Directory.GetFiles(game.BinariesPath, "*.exe") : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        foreach (var executable in executables)
        {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
            {
                using (process)
                {
                    if (!IsFrom(process, executable))
                        continue;
                    Log.Info($"{game.Profile.DisplayName} is running: {Path.GetFileName(executable)}, process {process.Id}.");
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>A process with the same name whose path cannot be read (an elevated one, for example) counts as the game.</summary>
    private static bool IsFrom(Process process, string executable)
    {
        try
        {
            var path = process.MainModule?.FileName;
            return path is null || string.Equals(Path.GetFullPath(path), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase);
        }
        catch (Win32Exception)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            // The process has just ended.
            return false;
        }
    }
}
