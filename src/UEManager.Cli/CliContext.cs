using UEManager.Core;
using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Cli;

/// <summary>
/// The state every command shares: where the manager's data folder is, which game is meant, and, for commands that
/// need it, the game install folder. The data folder is found the way the app finds it, and --data-dir overrides it.
/// </summary>
internal sealed class CliContext
{
    private CliContext(string dataDir, AppPaths paths, AppSettings settings, GameProfile profile, string gameId, GameInstall? game)
    {
        DataDir = dataDir;
        Paths = paths;
        Settings = settings;
        Profile = profile;
        GameId = gameId;
        Game = game;
    }

    public string DataDir { get; }

    public AppPaths Paths { get; }

    public AppSettings Settings { get; }

    public GameProfile Profile { get; }

    public string GameId { get; }

    /// <summary>The located install; null for read-only commands that work from the data folder alone.</summary>
    public GameInstall? Game { get; }

    public string GamesDir => Path.Combine(DataDir, "games");

    /// <summary>
    /// Opens the data folder and picks the game. Nothing is written unless <paramref name="write"/> is true: then the
    /// data folder is created if missing, proved writable, and the Core log goes into its logs folder.
    /// </summary>
    /// <param name="needInstall">The command changes the game or plans against it, so an install folder must be found.</param>
    /// <exception cref="CliError">The data folder or the game cannot be found.</exception>
    public static CliContext Open(CliArguments arguments, bool needInstall, bool write)
    {
        var dataDir = FindDataDir(arguments.DataDir, write);
        var paths = new AppPaths(dataDir);
        if (write)
        {
            try
            {
                paths.EnsureWritable();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                throw new CliError("data-dir-not-writable", $"The data folder '{dataDir}' cannot be written: {error.Message}");
            }
            Log.Initialize(paths.LogsDir);
        }
        var settings = AppSettings.Load(paths);
        var gameId = FindGameId(arguments.Game, settings, paths);
        var profile = GameProfile.BuiltIn().FirstOrDefault(p => p.Id.Equals(gameId, StringComparison.OrdinalIgnoreCase))
            ?? throw new CliError("game-not-found", $"No built-in game profile has the id '{gameId}'.",
                details: ["Known ids: " + string.Join(", ", GameProfile.BuiltIn().Select(p => p.Id))]);
        var game = FindInstall(profile, arguments.GameRoot, settings);
        if (game is null && needInstall)
        {
            throw new CliError("game-install-not-found", $"The {profile.DisplayName} install folder was not found.",
                details:
                [
                    "Point the app at the game first (its settings.json gamePaths entry), or pass --game-root <folder>.",
                    "Nothing was changed.",
                ]);
        }
        return new CliContext(dataDir, paths, settings, profile, profile.Id, game);
    }

    /// <summary>
    /// The data folder, in the order the CLI documents it: --data-dir, the UEMM_DATA_DIR environment variable, Data
    /// beside the executable (the published layout), then the app's usual install in C:\Games\UEMM.
    /// </summary>
    private static string FindDataDir(string? given, bool write)
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable("UEMM_DATA_DIR");
        var beside = Path.Combine(AppContext.BaseDirectory, "Data");
        var choice = new (string? Path, string Source)[]
        {
            (given, "--data-dir"),
            (fromEnvironment, "UEMM_DATA_DIR"),
            (Directory.Exists(beside) ? beside : null, "Data beside the executable"),
            (Program.DefaultDataFolder, @"C:\Games\UEMM\Data"),
        };
        var (path, source) = choice.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Path));
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new CliError("data-dir-not-found", "The manager's data folder was not found.",
                details:
                [
                    $"Pass --data-dir <folder>, set UEMM_DATA_DIR, or install the app so that {Program.DefaultDataFolder} exists.",
                    "Nothing was changed.",
                ]);
        }
        var full = Path.GetFullPath(path);
        if (File.Exists(full))
            throw new CliError("data-dir-not-found", $"'{full}' (from {source}) is a file, not a folder.");
        if (!Directory.Exists(full))
        {
            if (!write)
                throw new CliError("data-dir-not-found", $"The data folder '{full}' (from {source}) does not exist.");
            Directory.CreateDirectory(full);
        }
        return full;
    }

    /// <summary>The game: --game, the one in settings.json, or the only game with a mod list in the data folder.</summary>
    private static string FindGameId(string? given, AppSettings settings, AppPaths paths)
    {
        if (!string.IsNullOrWhiteSpace(given))
            return given;
        if (!string.IsNullOrWhiteSpace(settings.Game))
            return settings.Game;
        var installed = Directory.Exists(Path.Combine(paths.Root, "games"))
            ? Directory.GetDirectories(Path.Combine(paths.Root, "games"))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(id => File.Exists(paths.StateFile(id)))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];
        return installed.Count switch
        {
            1 => installed[0],
            0 => throw new CliError("game-not-found", "No game is chosen in settings.json and no game has a mod list in the data folder.",
                details: ["Run the app once and choose a game, or pass --game <id>."]),
            _ => throw new CliError("game-not-found", "settings.json names no game and the data folder holds several.",
                details: ["Pass --game <id>. Games with a mod list: " + string.Join(", ", installed)]),
        };
    }

    /// <summary>The install folder: --game-root, else the path saved in settings.json, else Steam and Epic.</summary>
    /// <exception cref="CliError">--game-root names a folder that is not this game's install.</exception>
    public static GameInstall? FindInstall(GameProfile profile, string? gameRoot, AppSettings settings)
    {
        if (gameRoot is not null)
        {
            var full = Path.GetFullPath(gameRoot);
            if (!Directory.Exists(full))
                throw new CliError("game-install-not-found", $"The folder '{full}' does not exist.");
            var install = GameLocator.FromFolder(profile, full);
            if (install is null)
            {
                throw new CliError("game-install-not-found", $"'{full}' is not a {profile.DisplayName} install folder.",
                    details:
                    [
                        "The folder chosen by hand needs the Engine folder, the game's project folder"
                            + (string.IsNullOrWhiteSpace(profile.ProjectFolder) ? "" : $" ({profile.ProjectFolder})")
                            + " and its exe.",
                        "Nothing was changed.",
                    ]);
            }
            return install;
        }
        var saved = settings.GamePaths.TryGetValue(profile.Id, out var path) ? path : null;
        return GameLocator.Find(profile, saved);
    }

    /// <summary>The manager for this game, with its default check that the game is not running.</summary>
    public ModManager OpenManager()
    {
        if (Game is null)
            throw new CliError("game-install-not-found", $"No {Profile.DisplayName} install folder was found, so the mod list cannot be changed.");
        return new ModManager(Game, Paths);
    }
}
