namespace UEManager.Core.Mods;

/// <summary>
/// Whether the UE4SS in a game is an official build: each of its DLLs that an official release has (UE4SS.dll and
/// the proxy, such as dwmapi.dll) must have the content of one of the official builds (<see cref="Ue4ssRelease.Files"/>).
/// Scripts and settings are not looked at: players and mods may change them, as the Keybinds script, and the DLLs
/// are what makes a build.
/// </summary>
public static class Ue4ssBuild
{
    /// <summary>The places of the installed DLLs that differ from every official build, in order; empty for an official one.</summary>
    /// <param name="exeFolder">The folder of the game's exe, which holds dwmapi.dll and the ue4ss folder.</param>
    public static IReadOnlyList<string> DifferingFiles(string exeFolder, IReadOnlyDictionary<string, IReadOnlyList<string>> official)
    {
        var differing = new List<string>();
        foreach (var (place, hashes) in official)
        {
            if (!place.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                continue;
            var path = Path.Combine(exeFolder, place.Replace('/', Path.DirectorySeparatorChar));
            // A file the player removed, such as one of UE4SS's example mods, does not make the build unofficial.
            if (!File.Exists(path))
                continue;
            var hash = FileSystem.Sha256(path);
            if (!hashes.Any(h => string.Equals(h, hash, StringComparison.OrdinalIgnoreCase)))
                differing.Add(place);
        }
        return differing.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>UE4SS's own settings and mod lists, which the player may change.</summary>
    public static bool IsUserFile(string path) =>
        Path.GetFileName(path) is var name
        && (name.Equals("UE4SS-settings.ini", StringComparison.OrdinalIgnoreCase)
            || name.Equals("mods.txt", StringComparison.OrdinalIgnoreCase)
            || name.Equals("mods.json", StringComparison.OrdinalIgnoreCase)
            || name.Equals("load_order.txt", StringComparison.OrdinalIgnoreCase));
}
