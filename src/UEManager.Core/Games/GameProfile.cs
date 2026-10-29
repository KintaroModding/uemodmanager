using System.Text.Json;
using UEManager.Core.Storage;

namespace UEManager.Core.Games;

/// <summary>
/// Everything the manager knows about one game. Profiles are JSON files in
/// Games\Profiles, built into the assembly, so supporting a game or correcting
/// its folder layout is a data change rather than a code change.
/// </summary>
public sealed class GameProfile
{
    /// <summary>Placeholder in path templates for the Unreal project folder.</summary>
    public const string ProjectToken = "{Project}";

    private const string ResourcePrefix = "UEManager.Core.Games.Profiles.";

    public string Id { get; init; } = "";

    public string DisplayName { get; init; } = "";

    public string? Tagline { get; init; }

    public string? Developer { get; init; }

    /// <summary>Steam application id, used to find the install through Steam's library manifests.</summary>
    public string? SteamAppId { get; init; }

    /// <summary>Name the game has in the Epic Games Launcher's install manifests.</summary>
    public string? EpicDisplayName { get; init; }

    /// <summary>
    /// The Unreal project folder below the install folder: the one holding
    /// Content and Binaries. When it is missing or wrong, the folder is detected.
    /// </summary>
    public string? ProjectFolder { get; init; }

    /// <summary>Folder the game mounts .pak files from, relative to the install folder.</summary>
    public string PaksPath { get; init; } = "{Project}/Content/Paks";

    /// <summary>Folder packaged mods are deployed into, relative to the install folder.</summary>
    public string ModsPath { get; init; } = "{Project}/Content/Paks/~mods";

    /// <summary>Folder Blueprint mods are deployed into; UE4SS's BPModLoaderMod loads them from here.</summary>
    public string LogicModsPath { get; init; } = "{Project}/Content/Paks/LogicMods";

    /// <summary>
    /// Folder of the game's shipping executable, where UE4SS is installed. A <c>*</c> in one part stands
    /// for any folder, as in <c>platform/*</c> for a game with a folder per store: the one that holds the
    /// shipping executable wins, the store's own first.
    /// </summary>
    public string BinariesPath { get; init; } = "{Project}/Binaries/Win64";

    /// <summary>
    /// File name of the game's real executable in <see cref="BinariesPath"/>, the one UE4SS is loaded into.
    /// Null for Unreal's usual <c>&lt;Project&gt;-Win64-Shipping.exe</c>.
    /// </summary>
    public string? ShippingExecutable { get; init; }

    /// <summary>Where "How to install UE4SS" sends the user; null for the official RE-UE4SS releases.</summary>
    public string? Ue4ssDownloadUrl { get; init; }

    /// <summary>A short hint shown with that link, such as which build to pick for this game.</summary>
    public string? Ue4ssNote { get; init; }

    /// <summary>
    /// The online manifest naming the current UE4SS release for this game, read at every
    /// start. It drives one-click install and update offers without a new app build.
    /// </summary>
    public string? Ue4ssManifestUrl { get; init; }

    /// <summary>
    /// A fixed UE4SS release file (a zip) for one-click install when the manifest cannot be
    /// reached. Without either, the user downloads the file and picks it.
    /// </summary>
    public string? Ue4ssPackageUrl { get; init; }

    /// <summary>SHA-256 of that file. A download that does not match is refused.</summary>
    public string? Ue4ssPackageSha256 { get; init; }

    /// <summary>Started by "Launch game" when no store launcher is known, relative to the install folder.</summary>
    public string? Executable { get; init; }

    /// <summary>
    /// The folder holding the game's save files, for save backups: relative to the install folder, or a
    /// full path in which environment variables such as %LOCALAPPDATA% are expanded. Set it only when the
    /// folder is known for sure: without it, save backups are not offered for the game.
    /// </summary>
    public string? SaveFolder { get; init; }

    /// <summary>The profiles built into the application, sorted by display name.</summary>
    public static IReadOnlyList<GameProfile> BuiltIn()
    {
        var assembly = typeof(GameProfile).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(name =>
            {
                using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
                return Parse(reader.ReadToEnd(), name[ResourcePrefix.Length..]);
            })
            .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Reads a profile from JSON. Comments and trailing commas are allowed.</summary>
    public static GameProfile Parse(string json, string source)
    {
        GameProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<GameProfile>(json, JsonFile.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Game profile '{source}' is not valid JSON: {ex.Message}", ex);
        }
        if (profile is null || string.IsNullOrWhiteSpace(profile.Id) || string.IsNullOrWhiteSpace(profile.DisplayName))
            throw new InvalidDataException($"Game profile '{source}' needs an id and a displayName.");
        return profile;
    }

    /// <summary>
    /// Turns a profile path template into a full path below the install folder.
    /// Both slash styles are accepted, and an empty project folder simply drops out.
    /// </summary>
    public static string ResolvePath(string installRoot, string template, string? projectFolder)
    {
        var expanded = template.Replace(ProjectToken, projectFolder ?? "", StringComparison.OrdinalIgnoreCase);
        var parts = expanded.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return Path.GetFullPath(Path.Combine([installRoot, .. parts]));
    }
}
