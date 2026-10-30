using System.IO.Compression;
using UEManager.Core.Mods;

namespace UEManager.Core.Games;

/// <summary>
/// Zips of the game's save folder, made before the game starts, because a broken mod can break a save.
/// Only the newest few are kept, and the player can put one back.
/// </summary>
public static class SaveBackups
{
    public const int Keep = 5;

    private const string Extension = ".zip";

    /// <summary>
    /// Save backups are offered only when the game profile names the save folder. Games keep their saves in
    /// different places (AppData, Documents\My Games, a folder with a name of their own), so it is never guessed.
    /// </summary>
    public static bool IsAvailable(GameProfile profile) => !string.IsNullOrWhiteSpace(profile.SaveFolder);

    /// <summary>
    /// The save folder of a game: the player's choice, else the profile's, with %LOCALAPPDATA% and the like
    /// expanded. Null when save backups are not available for the game, or when the profile's folder is
    /// inside the install folder and the game was not found.
    /// </summary>
    public static string? Folder(GameProfile profile, GameInstall? game, string? chosen)
    {
        if (!IsAvailable(profile))
            return null;
        if (!string.IsNullOrWhiteSpace(chosen))
            return chosen;
        var folder = Environment.ExpandEnvironmentVariables(profile.SaveFolder!);
        if (Path.IsPathFullyQualified(folder))
            return Path.GetFullPath(folder);
        return game is null ? null : GameProfile.ResolvePath(game.RootPath, folder, game.ProjectFolder);
    }

    /// <summary>Whether the save folder holds any file. A new player has none: the game makes the folder at its first save.</summary>
    public static bool HasSaves(string saveFolder) =>
        Directory.Exists(saveFolder) && Directory.EnumerateFiles(saveFolder, "*", SearchOption.AllDirectories).Any();

    /// <summary>Zips the save folder into a new backup, then deletes all but the newest <see cref="Keep"/>.</summary>
    /// <returns>The new backup.</returns>
    public static string Create(string saveFolder, string backupDir, DateTime now)
    {
        if (!Directory.Exists(saveFolder))
            throw new DirectoryNotFoundException($"The save folder was not found: {saveFolder}");
        if (!HasSaves(saveFolder))
            throw new IOException($"The save folder is empty: {saveFolder}");

        Directory.CreateDirectory(backupDir);
        var backup = Path.Combine(backupDir, $"saves {now:yyyy-MM-dd HH-mm-ss}{Extension}");
        for (var i = 2; File.Exists(backup); i++)
            backup = Path.Combine(backupDir, $"saves {now:yyyy-MM-dd HH-mm-ss} ({i}){Extension}");
        // Written under another name first, so a failed zip never counts as a backup.
        var temp = backup + ".tmp";
        try
        {
            ZipFile.CreateFromDirectory(saveFolder, temp, CompressionLevel.Optimal, includeBaseDirectory: false);
            File.Move(temp, backup);
        }
        catch
        {
            FileSystem.TryDeleteFile(temp);
            throw;
        }
        foreach (var old in List(backupDir).Skip(Keep))
            FileSystem.TryDeleteFile(old);
        Log.Info($"Backed up the saves in '{saveFolder}' to '{backup}'.");
        return backup;
    }

    /// <summary>
    /// Puts a backup back: the save folder then holds exactly the backup's files. The saves of now are backed up
    /// first, so a restore can be undone. The backup is unpacked next to the save folder before anything changes,
    /// and the old save folder moves aside until the new one is in place; when a step fails, it moves back.
    /// </summary>
    /// <returns>The backup of the saves before the restore, or null when there were none.</returns>
    public static string? Restore(string backup, string saveFolder, string backupDir, DateTime now)
    {
        if (!File.Exists(backup))
            throw new ModException($"The save backup is gone: {backup}");
        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(saveFolder));
        // Next to the save folder, so both moves stay on one drive; names of their own, so an interrupted
        // restore never leaves anything another one would delete.
        var staging = $"{folder}.uemm-restore-{now:yyyyMMddHHmmss}";
        var aside = $"{folder}.uemm-old-{now:yyyyMMddHHmmss}";
        try
        {
            ZipFile.ExtractToDirectory(backup, staging);
            var before = HasSaves(folder) ? Create(folder, backupDir, now) : null;
            if (Directory.Exists(folder))
                Directory.Move(folder, aside);
            try
            {
                Directory.Move(staging, folder);
            }
            catch
            {
                if (Directory.Exists(aside))
                    Directory.Move(aside, folder);
                throw;
            }
            FileSystem.TryDeleteDirectory(aside);
            Log.Info($"Restored the saves in '{folder}' from '{backup}'.");
            return before;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log.Warn($"The saves could not be restored from '{backup}': {ex.Message}");
            throw new ModException($"The saves could not be put back, so they stay as they are. Close the game, then try again. ({ex.Message})");
        }
        finally
        {
            FileSystem.TryDeleteDirectory(staging);
        }
    }

    /// <summary>The backups, newest first.</summary>
    public static IReadOnlyList<string> List(string backupDir) =>
        Directory.Exists(backupDir)
            ? Directory.EnumerateFiles(backupDir, "*" + Extension)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ThenByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];
}
