using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace UEManager.Core.Mods;

/// <summary>An installed mod as stored in mods.json.</summary>
public sealed class ModRecord
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string? Version { get; set; }

    /// <summary>File name of the download the mod was installed from.</summary>
    public string? SourceFileName { get; set; }

    public DateTimeOffset InstalledAt { get; set; }

    public bool Enabled { get; set; }

    /// <summary>The catalog entry the mod came from, such as "kintaromodding/mortalshell2_dashenhanced"; null for a download installed by hand.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CatalogId { get; set; }

    /// <summary>The variant chosen when the download offered several, such as "x2".</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Variant { get; set; }

    public List<ModFileRecord> Files { get; set; } = [];

    /// <summary>Short type name for the interface, such as IoStore or UE4SS Lua.</summary>
    [JsonIgnore]
    public string TypeLabel => ModTypes.Label(Files.Select(f => (f.Target, f.FileName)));

    [JsonIgnore]
    public long TotalSize => Files.Sum(f => f.Size);

    /// <summary>A Lua, C++ or Blueprint mod, which only runs with UE4SS.</summary>
    [JsonIgnore]
    public bool NeedsUe4ss => Files.Any(f => f.Target is DeployTarget.Ue4ssMods or DeployTarget.LogicMods);
}

/// <summary>The game folder a mod file is deployed into.</summary>
public enum DeployTarget
{
    /// <summary>The packaged-mods folder, ~mods.</summary>
    Mods,

    /// <summary>Content\Paks\LogicMods, for Blueprint mods that UE4SS starts.</summary>
    LogicMods,

    /// <summary>UE4SS's Mods folder, for Lua and C++ mods.</summary>
    Ue4ssMods,
}

public sealed class ModFileRecord
{
    /// <summary>
    /// Path below the target folder, with forward slashes: a plain file name for a
    /// packaged mod, "ModName/Scripts/main.lua" for a UE4SS mod.
    /// </summary>
    public string FileName { get; set; } = "";

    /// <summary>The folder the file deploys into. Records from before UE4SS support default to Mods.</summary>
    public DeployTarget Target { get; set; }

    public long Size { get; set; }

    /// <summary>
    /// Checksum of the manager's copy. A deployed file that no longer matches is never deleted,
    /// except a settings file: the player's version of it becomes the manager's copy.
    /// </summary>
    public string Sha256 { get; set; } = "";

    /// <summary>For a settings file, the checksum of the file as the mod shipped it. When <see cref="Sha256"/> differs, the player changed it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultSha256 { get; set; }

    /// <summary>Where the file was deployed, or null while the mod is disabled.</summary>
    public string? DeployedPath { get; set; }

    /// <summary>A file the player is meant to edit; see <see cref="SettingsFiles"/>.</summary>
    [JsonIgnore]
    public bool IsSettingsFile => SettingsFiles.Is(Target, FileName);

    /// <summary>A settings file whose manager copy holds the player's changes.</summary>
    [JsonIgnore]
    public bool IsEdited => IsSettingsFile && !string.Equals(Sha256, DefaultSha256 ?? Sha256, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A Lua library in UE4SS's shared folder ("shared/json/json.lua"), next to UE4SS's own. Several mods may bring
    /// the same one: one copy in the game serves them all, and it goes when the last of them is disabled.
    /// </summary>
    [JsonIgnore]
    public bool IsShared => SharedFiles.Is(Target, FileName);

    /// <summary>
    /// A shared file that was already in the game, with the same content, when the mod was enabled, such as one of
    /// UE4SS's own: the mod uses it, but never removes it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Borrowed { get; set; }
}

/// <summary>UE4SS's shared folder in its Mods folder, where Lua libraries live that every mod can load.</summary>
public static class SharedFiles
{
    public const string Folder = "shared";

    public static bool Is(DeployTarget target, string relativePath) =>
        target == DeployTarget.Ue4ssMods && relativePath.StartsWith(Folder + "/", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Mod files the player is meant to edit: a UE4SS mod's Scripts/config.lua or Scripts/config.json (and the like),
/// and the settings a pak mod keeps next to its pak, such as a .json in ~mods. A change to one never blocks a removal.
/// Disabling keeps the player's version, an update asks whether to keep it, and uninstalling backs it up.
/// </summary>
public static partial class SettingsFiles
{
    /// <summary>Settings a pak mod may keep next to its pak.</summary>
    public static readonly string[] PakSettingsExtensions = [".json", ".ini", ".cfg", ".toml", ".yaml", ".yml", ".txt"];

    /// <summary>
    /// A file a pak mod may keep next to its pak as its settings, such as Highlight.json or BuffDuration.txt.
    /// Readmes, licenses and change logs are not settings, and two mods' readme.txt would clash in ~mods.
    /// </summary>
    public static bool IsPakSettings(string path) =>
        FileSystem.HasExtension(path, PakSettingsExtensions) && !DocumentName().IsMatch(Path.GetFileNameWithoutExtension(path));

    /// <summary>The names a UE4SS mod gives its settings file in its Scripts folder.</summary>
    private static readonly string[] Ue4ssSettingsNames = ["config", "settings"];

    /// <summary>The formats of a UE4SS mod's settings file: Lua, as UE4SS's examples, or a data file the mod reads.</summary>
    private static readonly string[] Ue4ssSettingsExtensions = [".lua", ".json", ".ini", ".cfg", ".toml", ".yaml", ".yml", ".txt"];

    /// <param name="relativePath">The path below the target folder, such as "ModName/Scripts/config.lua".</param>
    public static bool Is(DeployTarget target, string relativePath)
    {
        if (target is DeployTarget.Mods or DeployTarget.LogicMods)
            return IsPakSettings(relativePath);
        if (target != DeployTarget.Ue4ssMods)
            return false;
        // A UE4SS mod's settings: Scripts/config.lua, or Scripts/config.json and the like, which the mod reads itself.
        var parts = relativePath.Split('/', '\\');
        return parts.Length == 3
            && parts[1].Equals("Scripts", StringComparison.OrdinalIgnoreCase)
            && Ue4ssSettingsNames.Contains(Path.GetFileNameWithoutExtension(parts[2]), StringComparer.OrdinalIgnoreCase)
            && FileSystem.HasExtension(parts[2], Ue4ssSettingsExtensions);
    }

    /// <summary>How the player knows the file: "Scripts/config.lua" for "ModName/Scripts/config.lua", else its name.</summary>
    public static string Name(string relativePath) =>
        relativePath.IndexOf('/') is var slash and >= 0 ? relativePath[(slash + 1)..] : relativePath;

    /// <summary>Names that mark a document, not settings: readme, license, change log, credits, install notes.</summary>
    [GeneratedRegex(@"^(read[ _-]?me|licen[cs]e|change[ _-]?log|changes|credits|install|notice|copying|authors)", RegexOptions.IgnoreCase)]
    private static partial Regex DocumentName();
}

public sealed class ModState
{
    public int Version { get; set; } = 1;

    public List<ModRecord> Mods { get; set; } = [];

    /// <summary>The ids of the mods the player turned off to test the game without mods; empty when no test is on.</summary>
    public List<string> OffForTest { get; set; } = [];

    public List<ModSet> Sets { get; set; } = [];
}

/// <summary>A named choice of mods that the player turns on together, such as "Co-op" or "Hard mode".</summary>
/// <param name="ModIds">The ids of the mods in the set. An update keeps a mod in its sets; an uninstall removes it.</param>
public sealed record ModSet(string Name, IReadOnlyList<string> ModIds);

public enum ModIntegrity
{
    /// <summary>Every deployed file is in place with its recorded size.</summary>
    Intact,

    /// <summary>The mod is disabled, so nothing is deployed to check.</summary>
    NotDeployed,

    FilesMissing,
    FilesChanged,
}
