using UEManager.Core.Stores;

namespace UEManager.Core.Games;

/// <summary>Finds a game's install folder and resolves the profile's paths inside it.</summary>
public static class GameLocator
{
    /// <summary>How far "Locate game" walks up from the chosen folder to find the install root.</summary>
    private const int MaxParentLevels = 4;

    /// <summary>Looks for the game in the Steam libraries, then in the Epic Games Launcher.</summary>
    public static GameInstall? Discover(GameProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.SteamAppId))
        {
            var app = SteamLibrary.FindApp(profile.SteamAppId);
            if (app is not null)
            {
                var install = Resolve(profile, app.InstallPath, GameStore.Steam, app.BuildId, SteamLibrary.LaunchUri(app.AppId));
                if (install is not null)
                    return install;
                Log.Warn($"Steam lists {profile.DisplayName} at '{app.InstallPath}', but its paks folder was not found there.");
            }
        }
        if (!string.IsNullOrWhiteSpace(profile.EpicDisplayName))
        {
            var app = EpicLauncher.FindApp(profile.EpicDisplayName);
            if (app is not null)
            {
                var install = Resolve(profile, app.InstallPath, GameStore.Epic, app.Version, app.LaunchUri);
                if (install is not null)
                    return install;
                Log.Warn($"Epic lists {profile.DisplayName} at '{app.InstallPath}', but its paks folder was not found there.");
            }
        }
        return null;
    }

    /// <summary>
    /// Resolves a folder the user picked. The pick may be the install folder or
    /// any folder inside it, such as Binaries\Win64.
    /// </summary>
    /// <param name="epicManifestsFolder">Where the Epic Games Launcher keeps its install manifests; null for the usual place.</param>
    public static GameInstall? FromFolder(GameProfile profile, string folder, string? epicManifestsFolder = null)
    {
        var candidate = new DirectoryInfo(folder);
        for (var level = 0; candidate is not null && level <= MaxParentLevels; level++, candidate = candidate.Parent)
        {
            var install = Resolve(profile, candidate.FullName, GameStore.Manual);
            if (install is null)
                continue;
            // A manual pick of a store's install still gets the store's build id and launcher.
            if (profile.SteamAppId is { Length: > 0 } appId
                && SteamLibrary.FindAppInLibraryOf(install.RootPath, appId) is { } steam
                && FileSystem.IsSameFolder(steam.InstallPath, install.RootPath))
                return install with { Store = GameStore.Steam, BuildId = steam.BuildId, LaunchUri = SteamLibrary.LaunchUri(appId) };
            // Epic's install is matched by its folder, so a profile that guessed the store's name wrong still works.
            if (EpicLauncher.FindAppIn(install.RootPath, epicManifestsFolder) is { } epic)
            {
                if (!string.Equals(epic.DisplayName, profile.EpicDisplayName, StringComparison.OrdinalIgnoreCase))
                    Log.Warn($"The Epic Games Launcher names the game in '{install.RootPath}' \"{epic.DisplayName}\". Set epicDisplayName "
                        + "in the game profile to that name, so the game is also found without choosing its folder.");
                return install with { Store = GameStore.Epic, BuildId = epic.Version, LaunchUri = epic.LaunchUri };
            }
            return install;
        }
        return null;
    }

    /// <summary>Resolves the profile's paths below an install folder, or null when its paks folder is not there.</summary>
    public static GameInstall? Resolve(GameProfile profile, string root, GameStore store,
        string? buildId = null, string? launchUri = null)
    {
        if (!Directory.Exists(root))
            return null;
        foreach (var project in ProjectCandidates(profile, root))
        {
            var paks = GameProfile.ResolvePath(root, profile.PaksPath, project);
            if (!Directory.Exists(paks))
                continue;
            var mods = GameProfile.ResolvePath(root, profile.ModsPath, project);
            return new GameInstall(profile, Path.GetFullPath(root), project, paks, mods, store, buildId, launchUri);
        }
        return null;
    }

    /// <summary>
    /// The profile's project folder first; then every folder in the install that
    /// looks like an Unreal project (Content\Paks and a game exe in Binaries\Win64),
    /// in case the profile's guess is wrong.
    /// </summary>
    private static IEnumerable<string?> ProjectCandidates(GameProfile profile, string root)
    {
        if (!profile.PaksPath.Contains(GameProfile.ProjectToken, StringComparison.OrdinalIgnoreCase))
        {
            yield return null;
            yield break;
        }
        if (!string.IsNullOrWhiteSpace(profile.ProjectFolder))
            yield return profile.ProjectFolder;

        string[] folders;
        try
        {
            folders = Directory.GetDirectories(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }
        foreach (var folder in folders.Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(folder);
            if (name.Equals("Engine", StringComparison.OrdinalIgnoreCase)
                || name.Equals(profile.ProjectFolder, StringComparison.OrdinalIgnoreCase))
                continue;
            if (Directory.Exists(Path.Combine(folder, "Content", "Paks")) && HasExecutable(Path.Combine(folder, "Binaries", "Win64")))
                yield return name;
        }
    }

    private static bool HasExecutable(string folder)
    {
        try
        {
            return Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.exe").Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
