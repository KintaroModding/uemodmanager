namespace UEManager.Cli;

/// <summary>A file of an installed mod, as mods.json records it.</summary>
internal sealed record ModFileDto(string FileName, string Target, long Size, string Sha256, string? DeployedPath, bool Deployed,
    bool PlayerFile, bool Shared, bool GameFile, bool Made, bool Borrowed);

/// <summary>An installed mod. The field names are the ones the CLI documents, so scripts can rely on them.</summary>
internal sealed record ModDto(string Id, string Name, string? Version, bool Enabled, string Type, int FileCount, long TotalSize,
    DateTimeOffset InstalledAt, string? SourceFileName, string? CatalogId, bool Recommended, IReadOnlyList<string> Tags, bool NotTracked,
    bool NeedsUe4ss, string Integrity, IReadOnlyList<string> Targets, IReadOnlyList<ModFileDto> Files);

internal sealed record StatusPayload(int SchemaVersion, string Command, ToolInfo Tool, string DataDir, string SettingsFile,
    bool SettingsExists, AppDto? App, GameInfo Game, ModsCountDto Mods, IReadOnlyList<string> Warnings);

internal sealed record ListPayload(int SchemaVersion, string Command, string DataDir, string GameId, int Count, IReadOnlyList<ModDto> Mods);

/// <summary>One deployed file checked against the manager's recorded SHA-256.</summary>
internal sealed record VerifyFileDto(string ModId, string ModName, string FileName, string Target, string? DeployedPath,
    string ExpectedSha256, string Status, string? ActualSha256, long? ActualSize, bool ExpectedChange, string? Error);

internal sealed record VerifyModDto(string Id, string Name, bool Enabled, string Status, int FileCount, IReadOnlyList<VerifyFileDto> Files);

internal sealed record VerifySummaryDto(int Mods, int FilesChecked, int FilesOk, int FilesChanged, int FilesMissing, int FilesUnreadable,
    int ExpectedChanges, int Problems);

internal sealed record VerifyPayload(int SchemaVersion, string Command, string DataDir, string GameId, VerifySummaryDto Summary,
    IReadOnlyList<VerifyModDto> Mods, IReadOnlyList<VerifyFileDto> Problems, IReadOnlyList<string> Warnings);

/// <summary>One installable part, as the install review would list it, with whether the plan selects it.</summary>
internal sealed record PlanPartDto(int Index, string Name, string Folder, string Label, string Kind, string Target, string Type,
    long Size, int FileCount, bool Selected, bool Uncertain, bool Alternate, string? Variant, string? VariantOf, string? NeedsLoader,
    IReadOnlyList<string> Problems, IReadOnlyList<string> Warnings, IReadOnlyList<string> Files);

/// <summary>A file the planned parts place in the game, and what is there now.</summary>
internal sealed record PlanEntryDto(string Target, string RelativePath, string Path, bool Exists, string? Owner);

/// <summary>The installed mod the download replaces, when it updates one.</summary>
internal sealed record PlanReplacedDto(string Id, string Name, string? Version, bool Enabled, string? Variant);

internal sealed record PlanIgnoredDto(int Count, int LeftOutCount, IReadOnlyList<string> Files, IReadOnlyList<string> LeftOut,
    int NestedArchiveCount, bool ContainsUe4ssRuntime);

internal sealed record PlanSelectionDto(bool AllParts, int PartCount, int SelectedCount, IReadOnlyList<string> SelectedLabels);

/// <summary>The read-only install plan: the same Check the install review runs, without touching the game.</summary>
/// <param name="KeepBoth">The download is installed as another mod beside the installed one with its file names or mod page.</param>
/// <param name="OwnFolder">
/// The folder below ~mods for the download's files that go there: a kept mod's own folder, or the folder of the mod it
/// updates; null puts them directly in ~mods.
/// </param>
internal sealed record PlanPayload(int SchemaVersion, string Command, string Source, string SourceFileName, string SuggestedName,
    string? SuggestedVersion, string GameId, GameInfo Game, PlanSelectionDto Selection, IReadOnlyList<PlanPartDto> Parts,
    IReadOnlyList<PlanEntryDto> Entries, IReadOnlyList<PlanEntryDto> ExistingFiles, IReadOnlyList<PlanEntryDto> GameFilesReplaced,
    PlanReplacedDto? Replaces, bool KeepBoth, string? OwnFolder, IReadOnlyList<string> EditedSettings, bool NeedsUe4ss,
    bool CanInstall, IReadOnlyList<string> Problems, IReadOnlyList<string> Warnings, PlanIgnoredDto Ignored);

internal sealed record InstallPayload(int SchemaVersion, string Command, string Source, ModDto Installed, PlanReplacedDto? Replaced,
    bool KeepBoth, string? OwnFolder, IReadOnlyList<string> Warnings);

internal sealed record ToggleModDto(string Id, string Name, bool EnabledBefore, bool EnabledAfter, bool Changed, int DeployedFiles,
    string Integrity);

internal sealed record TogglePayload(int SchemaVersion, string Command, string GameId, ToggleModDto Mod);

internal sealed record UninstallModDto(string Id, string Name, string? Version, bool Enabled, int FileCount, long TotalSize,
    string LibraryFolder, IReadOnlyList<ModFileDto> Files);

/// <summary>A file a mod left in the game, such as a log it wrote; an uninstall keeps it unless asked to move it.</summary>
internal sealed record LeftoverDto(string Path, string RelativePath, long SizeBytes, bool IsLog);

internal sealed record UninstallPayload(int SchemaVersion, string Command, string GameId, bool Confirmed, string? BackupFolder,
    IReadOnlyList<LeftoverDto> Leftovers, string? LeftoverBackup, int RemainingMods, UninstallModDto Mod);

/// <summary>One save backup: a zip of the game's save folder, with the player's note and whether it is a personal one.</summary>
internal sealed record SaveBackupDto(string Name, string Path, bool Personal, DateTimeOffset CreatedAt, long SizeBytes, string? Note);

internal sealed record BackupListPayload(int SchemaVersion, string Command, string DataDir, string GameId, string BackupDir,
    int Count, int PersonalCount, IReadOnlyList<SaveBackupDto> Backups);

internal sealed record BackupCreatePayload(int SchemaVersion, string Command, string DataDir, string GameId, string SaveFolder,
    int Keep, SaveBackupDto Backup, IReadOnlyList<string> Removed);

internal sealed record BackupRestorePayload(int SchemaVersion, string Command, string DataDir, string GameId, string SaveFolder,
    bool Confirmed, SaveBackupDto Backup, SaveBackupDto? BeforeBackup);

internal sealed record BackupPersonalPayload(int SchemaVersion, string Command, string DataDir, string GameId, SaveBackupDto Backup,
    bool Changed);

internal sealed record BackupNotePayload(int SchemaVersion, string Command, string DataDir, string GameId, SaveBackupDto Backup,
    bool Changed);

internal sealed record BackupDeletePayload(int SchemaVersion, string Command, string DataDir, string GameId, bool Confirmed,
    SaveBackupDto Backup);

/// <summary>The UE4SS release a command used, or the one the manifest names: where it came from and what it is.</summary>
internal sealed record Ue4ssReleaseDto(string Version, string FileName, string Url, string? Sha256, string? ManifestUrl);

/// <summary>
/// The UE4SS in the game, as `ue4ss status` reports it. `version` is the recorded release, else the one UE4SS's own
/// files name; `managed` says whether the manager's record, with its file list, is there. The manifest is read only
/// with --check, so `updateChecked` is false and the update fields are null otherwise.
/// </summary>
internal sealed record Ue4ssStatusPayload(int SchemaVersion, string Command, string GameId, string DataDir,
    string RecordFile, bool RecordExists, bool Installed, string? Version, string? VersionSource, bool Managed,
    string? Source, DateTimeOffset? InstalledAt, int FilesRecorded, string? Folder, string? ModsPath,
    bool UpdateChecked, string? LatestVersion, bool? UpdateAvailable, bool NewerInstalled, string? ManifestUrl,
    IReadOnlyList<string> Warnings);

/// <summary>
/// What `ue4ss install` or `ue4ss update` did: "installed", "updated", "up-to-date" when the manifest names the
/// installed release, or "newer-installed" when the installed one is newer (update never goes back); with these two
/// nothing was downloaded. The warnings name installed C++ mods, which another UE4SS build may stop.
/// </summary>
internal sealed record Ue4ssInstallPayload(int SchemaVersion, string Command, string GameId, string Action,
    Ue4ssReleaseDto? Release, string? Package, string? Version, string? PreviousVersion, string TargetFolder,
    int FilesWritten, bool Updated, IReadOnlyList<string> KeptFiles, IReadOnlyList<string> RemovedFiles,
    IReadOnlyList<string> Warnings);

/// <summary>An installed mod the removal would turn off, or turned off: it stays in the library.</summary>
internal sealed record Ue4ssModRefDto(string Id, string Name);

/// <summary>What `ue4ss remove` did, or, with --json and without --yes, what it would remove.</summary>
internal sealed record Ue4ssRemovePayload(int SchemaVersion, string Command, string GameId, bool Confirmed,
    bool Installed, string? Version, string? Folder, IReadOnlyList<string> Paths, IReadOnlyList<Ue4ssModRefDto> DisabledMods);
