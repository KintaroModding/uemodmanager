using Microsoft.Win32;

namespace UEManager.Core.Stores;

public sealed record SteamApp(string AppId, string InstallPath, string? BuildId);

/// <summary>Reads Steam's library list and app manifests. Never writes to Steam.</summary>
public static class SteamLibrary
{
    public static string LaunchUri(string appId) => $"steam://rungameid/{appId}";

    /// <summary>Finds an installed app in any Steam library on this machine.</summary>
    public static SteamApp? FindApp(string appId, IEnumerable<string>? steamRoots = null)
    {
        steamRoots ??= DefaultSteamRoots();
        var libraries = new List<string>();
        foreach (var root in steamRoots.Where(Directory.Exists))
        {
            libraries.Add(root);
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
                continue;
            try
            {
                libraries.AddRange(ParseLibraryFolders(File.ReadAllText(vdf)));
            }
            catch (Exception ex) when (ex is IOException or FormatException)
            {
                Log.Warn($"Steam library list '{vdf}' could not be read: {ex.Message}");
            }
        }
        foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var app = FindAppInLibrary(library, appId);
            if (app is not null)
                return app;
        }
        return null;
    }

    /// <summary>Reads the app manifest of the Steam library a game folder sits in, if it sits in one.</summary>
    public static SteamApp? FindAppInLibraryOf(string gamePath, string appId)
    {
        for (var dir = new DirectoryInfo(gamePath); dir is not null; dir = dir.Parent)
        {
            if (dir.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
                return dir.Parent is null ? null : FindAppInLibrary(dir.Parent.FullName, appId);
        }
        return null;
    }

    private static SteamApp? FindAppInLibrary(string library, string appId)
    {
        var manifest = Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf");
        if (!File.Exists(manifest))
            return null;
        try
        {
            var parsed = ParseAppManifest(File.ReadAllText(manifest));
            if (parsed is null)
                return null;
            var install = Path.Combine(library, "steamapps", "common", parsed.Value.InstallDir);
            return Directory.Exists(install) ? new SteamApp(appId, install, parsed.Value.BuildId) : null;
        }
        catch (Exception ex) when (ex is IOException or FormatException)
        {
            Log.Warn($"Steam manifest '{manifest}' could not be read: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Library paths from libraryfolders.vdf. Current Steam writes "0" { "path" "D:\\SteamLibrary" ... };
    /// older versions wrote "1" "D:\\SteamLibrary" directly.
    /// </summary>
    internal static IReadOnlyList<string> ParseLibraryFolders(string vdf)
    {
        var folders = VdfNode.Parse(vdf).Child("libraryfolders");
        if (folders is null)
            return [];
        return folders.Children
            .Where(entry => entry.Key.All(char.IsAsciiDigit))
            .Select(entry => entry.Value.Value ?? entry.Value.Text("path"))
            .OfType<string>()
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static (string InstallDir, string? BuildId)? ParseAppManifest(string acf)
    {
        var state = VdfNode.Parse(acf).Child("AppState");
        var installDir = state?.Text("installdir");
        return string.IsNullOrEmpty(installDir) ? null : (installDir, state!.Text("buildid"));
    }

    private static IEnumerable<string> DefaultSteamRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string userPath)
                yield return Path.GetFullPath(userPath);
            if (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) is string machinePath)
                yield return machinePath;
        }
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
    }
}
