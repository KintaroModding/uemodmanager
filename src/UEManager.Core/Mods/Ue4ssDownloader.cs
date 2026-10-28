namespace UEManager.Core.Mods;

/// <param name="TargetFolder">The game's Win64 folder UE4SS went into.</param>
/// <param name="KeptFiles">Files left as they were: the user's settings and files of managed mods.</param>
/// <param name="Updated">UE4SS was already installed and has been updated.</param>
public sealed record Ue4ssInstallResult(string TargetFolder, int FilesWritten, IReadOnlyList<string> KeptFiles, bool Updated);

/// <param name="DisabledMods">Managed mods that lived in UE4SS's folder and are now disabled; their library copies stay.</param>
public sealed record Ue4ssRemoveResult(int DisabledMods);

/// <summary>What "Reset modding" would change, for the question before it.</summary>
/// <param name="OtherFiles">Files in the mods folders that the manager did not install.</param>
public sealed record ResetPreview(int EnabledMods, int InstalledMods, bool Ue4ssInstalled, int OtherFiles);

/// <summary>What "Reset modding" changed.</summary>
/// <param name="BackupFolder">Where the files the manager did not install went, or null when there were none.</param>
public sealed record ResetResult(int ModsTurnedOff, bool Ue4ssRemoved, int FilesMovedAside, string? BackupFolder, int ModsDeleted);

/// <summary>Stored in ue4ss.json: the UE4SS release the manager installed, to tell when an update is available.</summary>
public sealed class Ue4ssInstallRecord
{
    /// <summary>Release version such as "v3.0.1-1181-g83330635"; null when it could not be told.</summary>
    public string? Version { get; set; }

    /// <summary>File name of the package it came from.</summary>
    public string? Source { get; set; }

    public DateTimeOffset InstalledAt { get; set; }
}

/// <summary>Downloads the UE4SS release file named by the manifest or the game profile.</summary>
public static class Ue4ssDownloader
{
    /// <summary>
    /// Downloads the file into <paramref name="folder"/> and returns its path. When a
    /// checksum is given, a file that does not match is deleted and refused.
    /// </summary>
    public static Task<string> DownloadAsync(HttpClient http, string url, string? expectedSha256, string folder,
        IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        var name = Path.GetFileName(new Uri(url).AbsolutePath);
        if (!FileSystem.HasExtension(name, FileSystem.ArchiveExtensions))
            name = "ue4ss.zip";
        return FileDownloader.DownloadAsync(http, url, name, null, expectedSha256, folder, "UE4SS", progress, cancellation);
    }
}
