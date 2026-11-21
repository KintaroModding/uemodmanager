namespace UEManager.Core.Mods;

/// <summary>
/// A download unpacked into staging and read, waiting for the user to confirm.
/// Disposing it removes the staged files.
/// </summary>
public sealed class InstallPreview : IDisposable
{
    internal InstallPreview(string source, string stagingDir, ScanResult scan, string suggestedName, string? suggestedVersion)
    {
        Source = source;
        StagingDir = stagingDir;
        Packages = scan.Packages;
        IgnoredFiles = scan.IgnoredFiles;
        LeftOutFiles = scan.LeftOutFiles;
        NestedArchiveCount = scan.NestedArchiveCount;
        ContainsUe4ssRuntime = scan.ContainsUe4ssRuntime;
        SuggestedName = suggestedName;
        SuggestedVersion = suggestedVersion;
    }

    public string Source { get; }

    public string SourceFileName => Path.GetFileName(Path.TrimEndingDirectorySeparator(Source));

    public string StagingDir { get; }

    public IReadOnlyList<PackageGroup> Packages { get; }

    /// <summary>Files in the download that are not packages, such as readmes and screenshots: their paths from its top.</summary>
    public IReadOnlyList<string> IgnoredFiles { get; }

    public int IgnoredFileCount => IgnoredFiles.Count;

    /// <summary>Those of <see cref="IgnoredFiles"/> that a mod may need, so the review warns about them.</summary>
    public IReadOnlyList<string> LeftOutFiles { get; }

    public int NestedArchiveCount { get; }

    /// <summary>The download is (or bundles) the UE4SS runtime itself, which the manager does not install.</summary>
    public bool ContainsUe4ssRuntime { get; }

    public string SuggestedName { get; }

    public string? SuggestedVersion { get; }

    public void Dispose() => FileSystem.TryDeleteDirectory(StagingDir);
}

/// <summary>The result of checking a selection against the game and the library before anything is written.</summary>
/// <param name="NeedsUe4ss">The selection holds Lua, DLL or Blueprint mods, and UE4SS is not installed.</param>
/// <param name="EditedSettings">Settings files of <paramref name="Replaces"/> that the player changed, such as "ModName/Scripts/config.lua".</param>
/// <param name="ShareOffer">
/// Installed mods have files at the download's places, each byte for byte the same, and both sides have files of their
/// own: the player may say they work together and keep both, sharing those files (see <see cref="ModManager.Install"/>'s
/// shareFiles). Null when that is not possible, or already chosen.
/// </param>
/// <param name="Sharing">The installed mods this install shares files with, and how many: by the player's choice, or
/// because those files are shared already. Null when it shares none.</param>
/// <param name="Clashes">Installed mods with files at the download's places that it neither replaces nor shares: the
/// install waits for them (a problem says so), so keeping both in a folder of its own does not help then.</param>
public sealed record InstallCheck(IReadOnlyList<FormattableString> Problems, IReadOnlyList<FormattableString> Warnings, ModRecord? Replaces,
    bool NeedsUe4ss = false, IReadOnlyList<string>? EditedSettings = null, FileSharing? ShareOffer = null, FileSharing? Sharing = null,
    IReadOnlyList<ModRecord>? Clashes = null)
{
    public bool CanInstall => Problems.Count == 0;

    /// <summary>An update would replace settings the player changed, so the player chooses whether to keep them.</summary>
    public bool HasEditedSettings => EditedSettings is { Count: > 0 };
}

/// <summary>Installed mods with files at a download's places, byte for byte the same, and how many such files there are.</summary>
public sealed record FileSharing(IReadOnlyList<ModRecord> Mods, int Files);

/// <summary>
/// An installed mod with a file of the same name as one of a download's files that go to ~mods, such as another style of
/// an outfit, or one from the same mod page.
/// </summary>
/// <param name="Folder">Its own folder below ~mods; null when its files are directly in ~mods.</param>
/// <param name="CanUpdate">Enough of its files are in the download for it to be an older copy.</param>
/// <param name="BySiteNumber">Only the number on the mod site in the download's file name matches, no file: the same
/// page, which can hold several files, so keeping both comes first.</param>
public sealed record SameNameMod(ModRecord Mod, string? Folder, bool CanUpdate, bool BySiteNumber = false);

/// <summary>
/// What an install offers when installed mods have the download's file names or mod page: update one of them, or keep
/// both. The install review lists these places and the command line tool takes its first choice, so both agree.
/// </summary>
/// <param name="Found">The installed mods with the same file names, or the one from the same mod page.</param>
public sealed record InstallPlaces(IReadOnlyList<SameNameMod> Found)
{
    /// <summary>There is something to choose: one of the installed mods may be the one this download updates.</summary>
    public bool Any => Found.Count > 0;

    /// <summary>
    /// Only the number on the mod site matches: the download can be another file of that page, and a wrong update would
    /// remove the mod the player has. Keeping both comes first then, and is the first choice.
    /// </summary>
    public bool SamePage => Found.Count > 0 && Found.All(f => f.BySiteNumber);

    /// <summary>The installed mods this download can update, in the order the review lists them.</summary>
    public IEnumerable<SameNameMod> Updates => Found.Where(f => f.CanUpdate);

    /// <summary>
    /// The update that is the first choice: the first one, unless the same mod page puts keeping both first. Null with
    /// only another mod of the same file names: the player must choose to keep both, as nothing can be updated.
    /// </summary>
    public SameNameMod? FirstUpdate => SamePage ? null : Updates.FirstOrDefault();
}

/// <summary>What a PBZModLoader installed by hand keeps in the game and the manager's copy does not need.</summary>
/// <param name="Places">Files and folders, below the install folder.</param>
/// <param name="SourceOnlyMods">Mods with a main.ts and no main.js: only the old loader could compile them.</param>
public sealed record OldModLoader(IReadOnlyList<string> Places, IReadOnlyList<string> SourceOnlyMods);
