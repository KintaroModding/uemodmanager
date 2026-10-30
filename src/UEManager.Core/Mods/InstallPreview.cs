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
        IgnoredFileCount = scan.IgnoredFileCount;
        NestedArchiveCount = scan.NestedArchiveCount;
        ContainsUe4ssRuntime = scan.ContainsUe4ssRuntime;
        SuggestedName = suggestedName;
        SuggestedVersion = suggestedVersion;
    }

    public string Source { get; }

    public string SourceFileName => Path.GetFileName(Path.TrimEndingDirectorySeparator(Source));

    public string StagingDir { get; }

    public IReadOnlyList<PackageGroup> Packages { get; }

    /// <summary>Files in the download that are not packages, such as readmes and screenshots.</summary>
    public int IgnoredFileCount { get; }

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
public sealed record InstallCheck(IReadOnlyList<FormattableString> Problems, IReadOnlyList<FormattableString> Warnings, ModRecord? Replaces,
    bool NeedsUe4ss = false, IReadOnlyList<string>? EditedSettings = null)
{
    public bool CanInstall => Problems.Count == 0;

    /// <summary>An update would replace settings the player changed, so the player chooses whether to keep them.</summary>
    public bool HasEditedSettings => EditedSettings is { Count: > 0 };
}
