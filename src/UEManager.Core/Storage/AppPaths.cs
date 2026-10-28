namespace UEManager.Core.Storage;

/// <summary>
/// Where the manager keeps its own data: settings, logs, the managed mod
/// library and the per-game install records. Nothing here lives in the game folder.
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string root)
    {
        Root = Path.GetFullPath(root);
    }

    /// <summary>
    /// The portable data folder: Data next to the executable, so the whole app
    /// moves with its folder and leaves nothing behind anywhere else.
    /// </summary>
    public static AppPaths NextToApp() => new(Path.Combine(AppContext.BaseDirectory, "Data"));

    public string Root { get; }

    /// <summary>Creates the data folder and proves that files can be written there.</summary>
    /// <exception cref="UnauthorizedAccessException">The folder is protected, as C:\Program Files is.</exception>
    /// <exception cref="IOException">The folder cannot be written for another reason, such as a read-only drive.</exception>
    public void EnsureWritable()
    {
        Directory.CreateDirectory(Root);
        var probe = Path.Combine(Root, $".write-test-{Guid.NewGuid():N}");
        File.WriteAllText(probe, "");
        File.Delete(probe);
    }

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string LogsDir => Path.Combine(Root, "logs");

    /// <summary>Scratch space for unpacked downloads. Emptied at start-up.</summary>
    public string StagingDir => Path.Combine(Root, "staging");

    public string GameDir(string gameId) => Path.Combine(Root, "games", gameId);

    /// <summary>Manager-owned copies of every installed mod, one folder per mod.</summary>
    public string LibraryDir(string gameId) => Path.Combine(GameDir(gameId), "library");

    public string StateFile(string gameId) => Path.Combine(GameDir(gameId), "mods.json");

    /// <summary>Which UE4SS release the manager installed into the game.</summary>
    public string Ue4ssRecordFile(string gameId) => Path.Combine(GameDir(gameId), "ue4ss.json");

    /// <summary>Copies of settings files the player changed, made before an uninstall or an update replaced them.</summary>
    public string BackupDir(string gameId) => Path.Combine(GameDir(gameId), "backups");

    /// <summary>Zips of the game's save folder, made before each launch.</summary>
    public string SaveBackupDir(string gameId) => Path.Combine(GameDir(gameId), "save-backups");
}
