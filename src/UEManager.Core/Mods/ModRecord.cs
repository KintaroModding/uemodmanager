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

    /// <summary>
    /// The mod's page on openmods, as the list last gave it, so the Mods page links to it without the list. It cannot be
    /// built from <see cref="CatalogId"/>: a project listed for another author has a page of its own. An update keeps it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CatalogPage { get; set; }

    /// <summary>
    /// The mod the app recommends for this game (see <see cref="RecommendedMod"/>): the Mods page shows it apart from
    /// the player's own mods, and mod sets leave it as it is.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Recommended { get; set; }

    /// <summary>The variant chosen when the download offered several, such as "x2".</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Variant { get; set; }

    /// <summary>The player's own words for the mod (see <see cref="ModTags"/>); null with none. An update keeps them.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Tags { get; set; }

    /// <summary>
    /// The player keeps this version: the update check leaves the mod out, and its box says Not tracked. An update keeps
    /// the choice, as it keeps the tags.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool NotTracked { get; set; }

    public List<ModFileRecord> Files { get; set; } = [];

    /// <summary>Short type name for the interface, such as PAK or UE4SS Lua. A settings file the mod made says nothing about it.</summary>
    [JsonIgnore]
    public string TypeLabel => ModTypes.Label(Files.Where(f => !f.Made).Select(f => (f.Target, f.FileName)));

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

    /// <summary>
    /// A path below the game's install folder as the download places it, such as
    /// "Game/Saved/EnhancedInputUserSettings.sav". The file is the game's or the player's own: the manager
    /// saves it before the mod's copy takes its place, and puts it back when the mod is disabled or removed.
    /// </summary>
    InstallRoot,

    /// <summary>
    /// The game's exe folder, next to the executable it starts: where a proxy DLL such as dxgi.dll, a ReShade
    /// add-on or an .asi loader goes, with the files that load with it. Like <see cref="InstallRoot"/>, a file
    /// the game or another tool had there is saved first and put back when the mod leaves.
    /// </summary>
    ExeFolder,
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

    /// <summary>A file the player is meant to edit; see <see cref="SettingsFiles"/>. The settings editor shows these.</summary>
    [JsonIgnore]
    public bool IsSettingsFile => SettingsFiles.Is(Target, FileName);

    /// <summary>
    /// The player changed this file outside the manager and chose to keep their version, as a value in a mod's
    /// main.lua. It is then handled as a settings file (<see cref="IsPlayerFile"/>), but the settings editor does not show it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Kept { get; set; }

    /// <summary>
    /// A file the player may change: a settings file, or a kept one. A change never stops a disable, an update or an
    /// uninstall; disabling keeps the player's version, an update asks whether to keep it, and uninstalling backs it up.
    /// </summary>
    [JsonIgnore]
    public bool IsPlayerFile => IsSettingsFile || Kept;

    /// <summary>
    /// A file in one of the game's own folders, which the game or another tool may write; see
    /// <see cref="DeployTarget.InstallRoot"/> and <see cref="DeployTarget.ExeFolder"/>.
    /// </summary>
    [JsonIgnore]
    public bool IsGameFile => Target is DeployTarget.InstallRoot or DeployTarget.ExeFolder;

    /// <summary>
    /// For a file that was in the game before the mod's copy took its place, the checksum of that file.
    /// The manager keeps a copy of it in the mod's library folder, and puts it back when the mod leaves.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReplacedSha256 { get; set; }

    /// <summary>
    /// A settings file the mod made in the game itself, such as Auto Collect Loot's PBZAutoCollect.txt in ~mods. No
    /// download brings it, so all of it is the player's settings: an update takes it over, an uninstall backs it up,
    /// and "Reset settings" leaves it, as the mod shipped no version of it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Made { get; set; }

    /// <summary>A settings file or a kept one whose manager copy holds the player's changes; a made one always does.</summary>
    [JsonIgnore]
    public bool IsEdited => IsPlayerFile && (Made || !string.Equals(Sha256, DefaultSha256 ?? Sha256, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A file several mods bring, byte for byte the same: one copy in the game serves them all, and it goes when the last
    /// of them is disabled. A Lua library in UE4SS's shared folder ("shared/json/json.lua") always is one; any other file
    /// only when the player said at install that the mods work together (<see cref="SharedWithMods"/>).
    /// </summary>
    [JsonIgnore]
    public bool IsShared => SharedFiles.Is(Target, FileName) || SharedWithMods;

    /// <summary>
    /// The player installed a mod that brings this file too, byte for byte the same, and said the mods work together, as
    /// the character mods of one author that bring the same framework (2026-11-21, a player's report: Mo Yuan and Play
    /// as Bai Chilian). Set on the file of each of those mods; only such an install sets it, so every other mod keeps
    /// the rule that a file in the game is one mod's.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SharedWithMods { get; set; }

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
/// Mod files the player is meant to edit: a UE4SS mod's Scripts/config.lua (and the like) and every data file in its
/// folder, such as the dlls/Minimap.ini a C++ mod reads, the settings a pak mod keeps next to its pak, such as a
/// .json in ~mods, and those in the folder of a C# Loader or PBZModLoader mod. A change to one never blocks a removal. Disabling keeps the player's version, an update asks whether
/// to keep it, and uninstalling backs it up.
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
        FileSystem.HasExtension(path, PakSettingsExtensions) && !IsDocument(path);

    /// <summary>The file types a document comes in. A script named install.js or a table named license.json is a mod's own file.</summary>
    internal static readonly string[] DocumentExtensions = ["", ".txt", ".md", ".rtf", ".pdf", ".htm", ".html"];

    /// <summary>A document rather than a mod file: a readme, a license, a change log, install notes.</summary>
    public static bool IsDocument(string path) => HasDocumentName(path) && FileSystem.HasExtension(path, DocumentExtensions);

    /// <summary>Named like a document, whatever the file type: install.bat is no mod file, license.json may be one.</summary>
    public static bool HasDocumentName(string path) => DocumentName().IsMatch(Path.GetFileNameWithoutExtension(path));

    /// <summary>The names a UE4SS mod gives its settings file in its Scripts folder.</summary>
    private static readonly string[] Ue4ssSettingsNames = ["config", "settings"];

    /// <summary>The formats of a UE4SS mod's settings file in its Scripts folder: Lua, as UE4SS's examples, or a data file the mod reads.</summary>
    private static readonly string[] Ue4ssSettingsExtensions = [".lua", ".json", ".ini", ".cfg", ".toml", ".yaml", ".yml", ".txt"];

    /// <summary>
    /// The formats of the data files a UE4SS mod reads, wherever they are in its folder: a C++ mod keeps its .ini next to
    /// main.dll, others keep tables the player may change, such as the JSON lists of Custom Shell System's catalog folder.
    /// No code and no text: a .lua is code unless it is the settings file in Scripts, and enabled.txt is UE4SS's switch.
    /// </summary>
    private static readonly string[] Ue4ssDataExtensions = [".ini", ".json", ".cfg", ".toml", ".yaml", ".yml"];

    /// <param name="relativePath">The path below the target folder, such as "ModName/Scripts/config.lua".</param>
    public static bool Is(DeployTarget target, string relativePath)
    {
        if (target is DeployTarget.Mods or DeployTarget.LogicMods)
            return IsPakSettings(relativePath);
        if (target is DeployTarget.InstallRoot or DeployTarget.ExeFolder)
            return IsCSharpLoaderSettings(relativePath) || (target == DeployTarget.InstallRoot && IsModLoaderSettings(relativePath));
        if (target != DeployTarget.Ue4ssMods || SharedFiles.Is(target, relativePath))
            return false;
        // Only files in a mod's folder: one loose in UE4SS's Mods folder, such as mods.json, is UE4SS's own.
        var parts = relativePath.Split('/', '\\');
        if (parts.Length < 2)
            return false;
        var name = parts[^1];
        // Scripts/config.lua, or Scripts/config.json and the like, which the mod reads itself.
        if (parts.Length == 3
            && parts[1].Equals("Scripts", StringComparison.OrdinalIgnoreCase)
            && Ue4ssSettingsNames.Contains(Path.GetFileNameWithoutExtension(name), StringComparer.OrdinalIgnoreCase)
            && FileSystem.HasExtension(name, Ue4ssSettingsExtensions))
            return true;
        // A data file anywhere in the mod's folder, such as dlls/Minimap.ini; a license.json is no setting.
        return FileSystem.HasExtension(name, Ue4ssDataExtensions) && !HasDocumentName(name);
    }

    /// <summary>
    /// Black Myth: Wukong's C# Loader: its settings, CSharpLoader/b1cs.ini, and the data files of its mods, such as
    /// CSharpLoader/Mods/&lt;mod&gt;/config.json, as game files or as files for the exe folder.
    /// </summary>
    /// <summary>
    /// Phantom Blade Zero's PBZModLoader: the settings and data files in a mod's folder, such as
    /// ProjectPBZ/PBZModLoader/mods/&lt;mod&gt;/&lt;mod&gt;.txt, which the player edits and Mods Menu writes. The
    /// mod's scripts and its readme or license are not.
    /// </summary>
    private static bool IsModLoaderSettings(string relativePath) =>
        PackageScanner.IsModLoaderModPath(relativePath) && IsPakSettings(relativePath);

    private static bool IsCSharpLoaderSettings(string relativePath)
    {
        var parts = relativePath.Split('/', '\\');
        var loader = Array.FindIndex(parts, part => part.Equals("CSharpLoader", StringComparison.OrdinalIgnoreCase));
        if (loader < 0)
            return false;
        var rest = parts[(loader + 1)..];
        if (rest.Length == 1)
            return rest[0].Equals("b1cs.ini", StringComparison.OrdinalIgnoreCase);
        return rest.Length >= 3 && rest[0].Equals("Mods", StringComparison.OrdinalIgnoreCase)
            && FileSystem.HasExtension(rest[^1], Ue4ssDataExtensions) && !HasDocumentName(rest[^1]);
    }

    /// <summary>How the player knows the file: "Scripts/config.lua" for "ModName/Scripts/config.lua", else its name.</summary>
    public static string Name(string relativePath) =>
        relativePath.IndexOf('/') is var slash and >= 0 ? relativePath[(slash + 1)..] : relativePath;

    /// <summary>
    /// Names that mark a document, not settings: readme, license, change log, credits, install notes, and the note
    /// openmods.nexus puts into the mods it lists (openmods.nexus_opensourcemods.txt).
    /// </summary>
    [GeneratedRegex(@"^(read[ _-]?me|licen[cs]e|change[ _-]?log|changes|credits|install|notice|copying|authors|openmods)", RegexOptions.IgnoreCase)]
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
