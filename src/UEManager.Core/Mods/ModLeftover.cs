namespace UEManager.Core.Mods;

/// <summary>
/// A file a mod left in the game that its record does not own, such as a log it wrote while the game ran. An uninstall
/// keeps it, since the mod made it, and tells the player it is there.
/// </summary>
/// <param name="Path">The file in the game.</param>
/// <param name="RelativePath">The same file below the game's folder, as the interface shows it.</param>
/// <param name="Size">Its size in bytes.</param>
public sealed record ModLeftover(string Path, string RelativePath, long Size)
{
    /// <summary>A log the mod wrote, rather than data the player may want to keep.</summary>
    public bool IsLog => RelativePath.EndsWith(".log", StringComparison.OrdinalIgnoreCase);
}

/// <summary>What an uninstall did: the settings it backed up, the files the mod left, and where those went if moved.</summary>
/// <param name="SettingsBackup">The folder the player's changed settings were backed up to, or null when there were none.</param>
/// <param name="Leftovers">The files the mod left in the game, which the uninstall kept.</param>
/// <param name="LeftoverBackup">The folder the leftovers were moved to, or null when they stayed in the game.</param>
public sealed record ModUninstallResult(string? SettingsBackup, IReadOnlyList<ModLeftover> Leftovers, string? LeftoverBackup);
