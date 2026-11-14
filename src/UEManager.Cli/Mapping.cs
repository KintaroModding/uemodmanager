using UEManager.Core.Games;
using UEManager.Core.Mods;

namespace UEManager.Cli;

/// <summary>Turns Core records into the JSON shapes the CLI documents.</summary>
internal static class Mapping
{
    public static ModFileDto File(ModFileRecord file) => new(file.FileName, file.Target.ToString(), file.Size, file.Sha256,
        file.DeployedPath, file.DeployedPath is not null, file.IsPlayerFile, file.IsShared, file.IsGameFile, file.Made, file.Borrowed);

    public static ModDto Mod(ModRecord mod) => new(mod.Id, mod.Name, mod.Version, mod.Enabled, mod.TypeLabel, mod.Files.Count, mod.TotalSize,
        mod.InstalledAt, mod.SourceFileName, mod.CatalogId, mod.Recommended, mod.Tags ?? [], mod.NotTracked, mod.NeedsUe4ss,
        ModManager.Integrity(mod).ToString(), Targets(mod), mod.Files.Select(File).ToList());

    /// <summary>The folders the mod's files go to, in the order they first appear.</summary>
    public static IReadOnlyList<string> Targets(ModRecord mod) =>
        mod.Files.Select(f => f.Target.ToString()).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// The game and its install. With no install, the paths are null and the caller says why: status still has to report
    /// the data folder, while the changing commands refuse earlier.
    /// </summary>
    public static GameInfo Game(GameProfile profile, GameInstall? install, bool checkRunning)
    {
        if (install is null)
            return new GameInfo(profile.Id, profile.DisplayName, false, null, null, profile.ProjectFolder, null, null, null, null, null, null);
        var ue4ss = Ue4ssRuntime.Detect(install);
        var running = checkRunning ? GameProcess.IsRunning(install) : (bool?)null;
        return new GameInfo(profile.Id, profile.DisplayName, true, install.RootPath, install.Store.ToString(), install.ProjectFolder,
            install.PaksPath, install.ModsPath, install.LogicModsPath, install.BinariesPath,
            new Ue4ssInfo(ue4ss.Installed, ue4ss.Version, ue4ss.Folder, ue4ss.ModsPath), running);
    }
}
