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
                    return WarnWithoutShippingExecutable(install);
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
                    return WarnWithoutShippingExecutable(install);
                Log.Warn($"Epic lists {profile.DisplayName} at '{app.InstallPath}', but its paks folder was not found there.");
            }
        }
        return null;
    }

    /// <summary>
    /// Resolves a folder the user picked. The pick may be the install folder or
    /// any folder inside it, such as Binaries\Win64. It must be this game's install
    /// folder, not that of another Unreal game: see <see cref="IsGameRoot"/>.
    /// </summary>
    /// <param name="epicManifestsFolder">Where the Epic Games Launcher keeps its install manifests; null for the usual place.</param>
    public static GameInstall? FromFolder(GameProfile profile, string folder, string? epicManifestsFolder = null)
    {
        var candidate = new DirectoryInfo(folder);
        for (var level = 0; candidate is not null && level <= MaxParentLevels; level++, candidate = candidate.Parent)
        {
            var install = Resolve(profile, candidate.FullName, GameStore.Manual);
            if (install is null || !IsGameRoot(install))
                continue;
            // A manual pick of a store's install still gets the store's build id and launcher.
            if (profile.SteamAppId is { Length: > 0 } appId
                && SteamLibrary.FindAppInLibraryOf(install.RootPath, appId) is { } steam
                && FileSystem.IsSameFolder(steam.InstallPath, install.RootPath))
                return Resolve(profile, install.RootPath, GameStore.Steam, steam.BuildId, SteamLibrary.LaunchUri(appId)) ?? install;
            // Epic's install is matched by its folder, so a profile that guessed the store's name wrong still works.
            if (EpicLauncher.FindAppIn(install.RootPath, epicManifestsFolder) is { } epic)
            {
                if (!string.Equals(epic.DisplayName, profile.EpicDisplayName, StringComparison.OrdinalIgnoreCase))
                    Log.Warn($"The Epic Games Launcher names the game in '{install.RootPath}' \"{epic.DisplayName}\". Set epicDisplayName "
                        + "in the game profile to that name, so the game is also found without choosing its folder.");
                return Resolve(profile, install.RootPath, GameStore.Epic, epic.Version, epic.LaunchUri) ?? install;
            }
            return install;
        }
        return null;
    }

    /// <summary>
    /// Whether an install found in a folder picked by hand is this game's: the folder holds the Engine folder,
    /// the profile's own project folder (not just any Unreal project, as with a store's install) and the
    /// shipping executable. Another Unreal game's folder has the first, but not the others.
    /// </summary>
    private static bool IsGameRoot(GameInstall install)
    {
        var profile = install.Profile;
        var missing = !Directory.Exists(Path.Combine(install.RootPath, "Engine")) ? "the Engine folder"
            : !string.IsNullOrWhiteSpace(profile.ProjectFolder)
                && !string.Equals(install.ProjectFolder, profile.ProjectFolder, StringComparison.OrdinalIgnoreCase)
                ? $"the {profile.ProjectFolder} folder"
            : install.FindShippingExecutable() is null ? $"the game's exe in {install.BinariesPath}"
            : null;
        if (missing is null)
            return true;
        Log.Info($"'{install.RootPath}' is not the {profile.DisplayName} install folder: {missing} is missing.");
        return false;
    }

    /// <summary>A store's install is used without its exe too, for pak mods; the log says what UE4SS will miss.</summary>
    private static GameInstall WarnWithoutShippingExecutable(GameInstall install)
    {
        if (install.FindShippingExecutable() is null)
            Log.Warn($"The game's exe was not found in '{install.BinariesPath}'. UE4SS cannot be installed until the "
                + "game profile names the folder it is in.");
        return install;
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
            var binaries = FindBinaries(profile, root, project, store);
            return new GameInstall(profile, Path.GetFullPath(root), project, paks, mods, binaries, store, buildId, launchUri);
        }
        return null;
    }

    /// <summary>
    /// The folder of the shipping executable. A part of the profile's binariesPath with a * in it matches
    /// any folder there: the one named after the store comes first, then the others by name, and the first
    /// that holds the shipping executable wins. Without one, the first that exists, else the path up to the *.
    /// </summary>
    private static string FindBinaries(GameProfile profile, string root, string? project, GameStore store)
    {
        var parts = profile.BinariesPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        var wild = Array.FindIndex(parts, part => part.Contains('*'));
        if (wild < 0)
            return GameProfile.ResolvePath(root, profile.BinariesPath, project);

        var parent = GameProfile.ResolvePath(root, string.Join('/', parts[..wild]), project);
        string[] folders;
        try
        {
            folders = Directory.Exists(parent) ? Directory.GetDirectories(parent, parts[wild]) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            folders = [];
        }
        var candidates = folders
            .OrderBy(folder => Path.GetFileName(folder).Equals(store.ToString(), StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(folder => folder, StringComparer.OrdinalIgnoreCase)
            .Select(folder => GameProfile.ResolvePath(folder, string.Join('/', parts[(wild + 1)..]), project))
            .ToList();
        return candidates.FirstOrDefault(folder => GameInstall.FindShippingExecutable(profile, folder) is not null)
            ?? candidates.FirstOrDefault(Directory.Exists)
            ?? parent;
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
