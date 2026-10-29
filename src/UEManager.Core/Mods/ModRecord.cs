using System.Text.Json.Serialization;

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
}

/// <summary>
/// Mod files the player is meant to edit: a UE4SS mod's Scripts/config.lua, and the settings a pak
/// mod keeps next to its pak, such as a .json in ~mods. A change to one never blocks a removal.
/// Disabling keeps the player's version, an update asks whether to keep it, and uninstalling backs it up.
/// </summary>
public static class SettingsFiles
{
    /// <summary>Settings a pak mod may keep next to its pak. Not .txt: that is mostly a readme.</summary>
    public static readonly string[] PakSettingsExtensions = [".json", ".ini", ".cfg", ".toml", ".yaml", ".yml"];

    /// <param name="relativePath">The path below the target folder, such as "ModName/Scripts/config.lua".</param>
    public static bool Is(DeployTarget target, string relativePath)
    {
        if (target is DeployTarget.Mods or DeployTarget.LogicMods)
            return FileSystem.HasExtension(relativePath, PakSettingsExtensions);
        if (target != DeployTarget.Ue4ssMods)
            return false;
        var parts = relativePath.Split('/', '\\');
        return parts.Length == 3
            && parts[1].Equals("Scripts", StringComparison.OrdinalIgnoreCase)
            && parts[2].Equals("config.lua", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>How the player knows the file: "Scripts/config.lua" for "ModName/Scripts/config.lua", else its name.</summary>
    public static string Name(string relativePath) =>
        relativePath.IndexOf('/') is var slash and >= 0 ? relativePath[(slash + 1)..] : relativePath;
}

public sealed class ModState
{
    public int Version { get; set; } = 1;

    public List<ModRecord> Mods { get; set; } = [];
}

public enum ModIntegrity
{
    /// <summary>Every deployed file is in place with its recorded size.</summary>
    Intact,

    /// <summary>The mod is disabled, so nothing is deployed to check.</summary>
    NotDeployed,

    FilesMissing,
    FilesChanged,
}
