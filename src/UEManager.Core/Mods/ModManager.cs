using UEManager.Core.Games;
using UEManager.Core.Storage;

namespace UEManager.Core.Mods;

/// <summary>
/// Installs, enables, disables and removes mods for one game install: packaged
/// mods in ~mods, Blueprint mods in LogicMods, and UE4SS Lua and C++ mods.
///
/// Every installed mod keeps a manager-owned copy in the library. Enabling copies
/// it into the game; disabling removes exactly the files that were deployed, and
/// only when their checksum still matches. A file the manager did not deploy is
/// never overwritten or deleted. Nothing is changed while the game runs, and a
/// removal changes all of a mod's files or none of them. Saving the mod list is
/// the last step of a change; when it fails, the change is undone.
///
/// The mod list belongs to the game, not to one install of it. When the player
/// chooses another install while enabled mods are still in the first one, nothing
/// changes until <see cref="MoveHere"/> has moved them.
/// </summary>
public sealed class ModManager
{
    /// <summary>UE4SS starts a mod folder holding this file, whatever mods.txt says, so mods.txt is never edited.</summary>
    private const string Ue4ssEnabledMarker = "enabled.txt";

    /// <summary>Suffix of files and folders moved aside while they are removed.</summary>
    private const string RemovingSuffix = ".uemm-removing";

    private readonly object gate = new();
    private readonly AppPaths paths;
    private readonly List<ModRecord> mods;
    private readonly Func<GameInstall, bool> isGameRunning;

    /// <summary>The ids of the mods <see cref="TurnOffForTest"/> turned off, saved with the mod list.</summary>
    private List<string> offForTest;

    /// <summary>The player's mod sets, saved with the mod list. A change puts a new list in place, so the old one can be put back.</summary>
    private List<ModSet> sets;

    /// <param name="isGameRunning">Whether the game runs now from an install. By default, the install's executables are looked for among the running processes.</param>
    public ModManager(GameInstall game, AppPaths paths, Func<GameInstall, bool>? isGameRunning = null)
    {
        Game = game;
        this.paths = paths;
        this.isGameRunning = isGameRunning ?? GameProcess.IsRunning;
        var state = JsonFile.Read<ModState>(StateFile);
        mods = state?.Mods ?? [];
        offForTest = state?.OffForTest ?? [];
        sets = state?.Sets ?? [];
        FollowMovedInstall();
        OtherInstallRoot = FindOtherInstall();
    }

    public GameInstall Game { get; }

    public IReadOnlyList<ModRecord> Mods
    {
        get
        {
            lock (gate)
                return mods.ToList();
        }
    }

    /// <summary>
    /// Another install folder of the game that still holds files of enabled mods, as when the player
    /// chose a second copy of the game; null when every deployed file is in this install. No mod can
    /// be changed until <see cref="MoveHere"/> has moved them.
    /// </summary>
    public string? OtherInstallRoot { get; private set; }

    /// <summary>The enabled mods whose files are in <see cref="OtherInstallRoot"/>.</summary>
    public IReadOnlyList<ModRecord> ModsInOtherInstall
    {
        get
        {
            lock (gate)
                return mods.Where(IsInOtherInstall).ToList();
        }
    }

    private string StateFile => paths.StateFile(Game.Profile.Id);

    public string LibraryFolderOf(ModRecord mod) => Path.Combine(paths.LibraryDir(Game.Profile.Id), mod.Id);

    /// <summary>Removes downloads left in staging by an earlier session.</summary>
    public static void ClearStaging(AppPaths paths) => FileSystem.TryDeleteDirectory(paths.StagingDir);

    /// <summary>The game folder a target deploys into. UE4SS's folder depends on the installed version.</summary>
    public string TargetFolder(DeployTarget target) => target switch
    {
        DeployTarget.LogicMods => Game.LogicModsPath,
        DeployTarget.Ue4ssMods => Ue4ssRuntime.Detect(Game).ModsPath,
        // A game file keeps the path the download gives it, below the install folder.
        DeployTarget.InstallRoot => Game.RootPath,
        // A proxy DLL and the files that load with it go next to the game's exe.
        DeployTarget.ExeFolder => Game.BinariesPath,
        _ => Game.ModsPath,
    };

    /// <summary>Unpacks a download into staging and reads it. Nothing reaches the game yet.</summary>
    public InstallPreview Inspect(string source)
    {
        var staging = Path.Combine(paths.StagingDir, Guid.NewGuid().ToString("N"));
        try
        {
            ArchiveStager.Stage(source, staging);
            var (name, version) = ModNaming.FromSource(source);
            var scan = PackageScanner.Scan(staging, name, Game.ProjectFolder ?? Game.Profile.ProjectFolder,
                Path.GetRelativePath(Game.RootPath, Game.BinariesPath).Replace('\\', '/'));
            Log.Info($"Inspected '{source}': {scan.Packages.Count} part(s), {scan.IgnoredFileCount} other file(s).");
            return new InstallPreview(source, staging, scan, name, version);
        }
        catch
        {
            FileSystem.TryDeleteDirectory(staging);
            throw;
        }
    }

    /// <summary>Checks a selection against the game folders and the installed mods.</summary>
    /// <param name="catalogId">
    /// The catalog entry the download comes from, if any. The installed mod with the same id is the
    /// one it updates, and it never takes over the files of another enabled mod.
    /// </param>
    /// <param name="ownFolder">The folder below ~mods for the files that go there; null puts them directly in ~mods.</param>
    public InstallCheck Check(InstallPreview preview, IReadOnlyCollection<PackageGroup> selected, string? catalogId = null, string? ownFolder = null)
    {
        var problems = new List<FormattableString>();
        var warnings = new List<FormattableString>();
        if (preview.ContainsUe4ssRuntime)
        {
            problems.Add($"This download contains the UE4SS runtime itself, which the manager does not install. Install UE4SS by hand into {Game.BinariesPath}, then install its mods here.");
            return new InstallCheck(problems, warnings, null);
        }
        if (preview.Packages.Count == 0)
        {
            if (preview.NestedArchiveCount > 0)
                problems.Add($"No mod files were found. The download holds another archive: extract it and install that one.");
            else
            {
                var places = string.Join(", ", new[] { Game.ProjectFolder ?? Game.Profile.ProjectFolder, Path.GetRelativePath(Game.RootPath, Game.BinariesPath).Replace('\\', '/') }
                    .Where(place => !string.IsNullOrWhiteSpace(place)));
                problems.Add($"No mod files were found. The manager looks for .pak, .utoc or .ucas files, a UE4SS mod folder with Scripts\\main.lua or dlls, and files below the game's own folders ({places}). This download may need an install by hand: see its install notes.");
            }
            return new InstallCheck(problems, warnings, null);
        }
        if (selected.Count == 0)
        {
            problems.Add($"Select at least one part to install.");
            return new InstallCheck(problems, warnings, null);
        }
        foreach (var package in selected)
        {
            problems.AddRange(package.Problems);
            warnings.AddRange(package.Warnings);
        }

        // The two script loaders run together since Mods Menu 0.9.0 and PBZModLoader 2.1.0. An older one of either causes
        // problems with the other, so a known older version is worth a word. Only one of them can be the game's hid.dll.
        var menuNow = IsModsMenu(selected) && IsModLoaderInstalled;
        var loaderNow = IsModLoader(preview, catalogId) && IsModsMenuInstalled;
        if ((menuNow && (IsOlder(preview.SuggestedVersion, MenuWithLoader) || IsOlder(InstalledModLoaderVersion, LoaderWithMenu)))
            || (loaderNow && (IsOlder(preview.SuggestedVersion, LoaderWithMenu) || IsOlder(InstalledModsMenuVersion, MenuWithLoader))))
            warnings.Add($"Mods Menu 0.9.0 and PBZModLoader 2.1.0 or newer run together. An older version of either causes problems with the other: update the older one first.");
        if (loaderNow && selected.Any(p => p.RelativePaths.Any(path => Path.GetFileName(path).Equals(ModsMenuEntry, StringComparison.OrdinalIgnoreCase))))
            problems.Add($"Mods Menu uses hid.dll, so PBZModLoader cannot use its hid entry beside it. Choose another entry.");
        else if (menuNow && IsModLoaderEntry(Path.Combine(Game.BinariesPath, ModsMenuEntry)))
            problems.Add($"PBZModLoader uses hid.dll, which Mods Menu needs. Change PBZModLoader to another entry first.");

        // A script mod runs only in its loader, so it waits for that loader. A loader's own download may bring its mods along.
        foreach (var needed in selected.Select(p => p.NeedsLoader).OfType<string>().Distinct())
        {
            if (needed == PackageScanner.ForModsMenu && !IsModsMenuInstalled)
                problems.Add($"This mod runs in Mods Menu, which is not installed. Install Mods Menu first, then this mod.");
            else if (needed == PackageScanner.ForModLoader && !IsModLoaderInstalled && !IsModLoader(preview, catalogId))
                problems.Add($"This mod runs in PBZModLoader, which is not installed. Install it first from the top of the Mods page, then this mod.");
        }

        var sourceOnly = PackageScanner.SourceOnlyMods(selected);
        if (sourceOnly.Count > 0)
            warnings.Add($"PBZModLoader 2.0 cannot use a mod with only a main.ts file, so this will not run: {string.Join(", ", sourceOnly)}.");

        if (preview.LeftOutFiles.Count > 0)
        {
            const int shown = 8;
            var names = string.Join(", ", preview.LeftOutFiles.Take(shown)) + (preview.LeftOutFiles.Count > shown ? ", …" : "");
            warnings.Add($"Files left out ({preview.LeftOutFiles.Count}): {names}. The manager does not know where they go. If the mod does not work without them, see its install notes.");
        }

        var needsUe4ss = !Ue4ssRuntime.Detect(Game).Installed
            && selected.Any(p => p.Target is DeployTarget.Ue4ssMods or DeployTarget.LogicMods);
        if (needsUe4ss)
            warnings.Add($"UE4SS is not installed in this game. Lua, DLL and Blueprint mods only run with it. They are installed anyway and start working once UE4SS is in place.");

        var entries = selected
            .SelectMany(p => p.RelativePaths.Select((path, i) => (p.Target, Path: InFolder(ownFolder, p.Target, path), Source: p.Files[i])))
            .ToList();
        // A shared library that goes with several selected UE4SS mods is the same file each time.
        foreach (var duplicate in entries.GroupBy(e => (e.Target, e.Path.ToUpperInvariant()))
                     .Where(g => g.Select(e => e.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
            problems.Add($"{duplicate.First().Path} is in more than one selected part. Select only one of them.");
        var shared = entries.Where(e => SharedFiles.Is(e.Target, e.Path)).DistinctBy(e => e.Path.ToUpperInvariant()).ToList();

        ModRecord? replaces;
        IReadOnlyList<string> editedSettings = [];
        lock (gate)
        {
            var owners = new List<ModRecord>();
            // UE4SS's own files are not a mod's to replace: overwriting them would break UE4SS and every mod on it.
            var ue4ss = ReadUe4ssRecord()?.Files;
            // Shared libraries say nothing about which mod this is: other mods may bring the same ones.
            foreach (var (target, path, _) in entries.Where(e => !SharedFiles.Is(e.Target, e.Path)).DistinctBy(e => (e.Target, e.Path.ToUpperInvariant())))
            {
                var owner = mods.FirstOrDefault(m => m.Files.Any(f => IsSameFile(f, target, path)));
                if (owner is not null)
                {
                    if (!owners.Contains(owner))
                        owners.Add(owner);
                }
                else if (target == DeployTarget.ExeFolder && ue4ss is not null && ue4ss.ContainsKey(path))
                {
                    problems.Add($"{path} belongs to UE4SS, which the manager installed. Uninstall UE4SS first, or remove that file from the download.");
                }
                else if (target is DeployTarget.InstallRoot or DeployTarget.ExeFolder)
                {
                    // The file belongs to the game, the player or another tool: replacing it is what the mod is for.
                    // The manager saves it first, so nothing of theirs is lost.
                    if (File.Exists(Path.Combine(TargetFolder(target), Local(path))))
                        warnings.Add($"{path} is in the game already. The manager keeps a copy of it and puts it back when the mod is disabled or removed.");
                }
                else if (File.Exists(Path.Combine(TargetFolder(target), Local(path))))
                {
                    problems.Add($"{path} is already in {TargetName(target)}, but the manager did not install it. Move or delete it first.");
                }
            }

            if (catalogId is not null)
            {
                // The same catalog entry, or else the same mod installed by hand from a download.
                replaces = mods.FirstOrDefault(m => string.Equals(m.CatalogId, catalogId, StringComparison.OrdinalIgnoreCase))
                    ?? (owners is [{ CatalogId: null } handInstalled] && IsSameMod(handInstalled, entries.Select(e => (e.Target, e.Path))) ? handInstalled : null);
                foreach (var other in owners.Where(o => o != replaces && o.Enabled))
                    problems.Add($"\"{other.Name}\" uses the same files. Disable or uninstall it first.");
            }
            else
            {
                if (owners.Count > 1)
                    problems.Add($"This download uses files of several installed mods: {string.Join(", ", owners.Select(o => o.Name))}. Uninstall them first.");
                replaces = owners is [var only] && IsSameMod(only, entries.Select(e => (e.Target, e.Path))) ? only : null;
                if (owners is [{ Enabled: true } other] && replaces is null)
                    problems.Add($"\"{other.Name}\" uses the same files. Disable or uninstall it first.");
            }
            // The download's other choice is installed: this one takes its place, so the mod is never in the game twice.
            if (replaces is null && owners.Count == 0 && ownFolder is null)
            {
                var others = preview.Packages.Where(p => p.VariantOf is not null && !selected.Contains(p) && selected.Any(s => s.VariantOf == p.VariantOf)).ToList();
                replaces = mods.FirstOrDefault(m => others.Any(o => IsSameMod(m, o.RelativePaths.Select(path => (o.Target, path)))));
            }
            // A download whose Nexus mod number matches an installed mod is its update, even when the mod moved from a
            // pak to a script and the file names changed: the old one goes, the new one takes its place.
            var siteId = ModNaming.SiteModId(preview.SourceFileName);
            if (replaces is null && siteId is not null)
                replaces = mods.FirstOrDefault(m => ModNaming.SiteModId(m.SourceFileName) == siteId);
            if (replaces is not null)
                editedSettings = EditedSettings(replaces);

            // One copy of a shared library serves every mod: one that is there must have the same content.
            foreach (var (target, path, source) in shared)
            {
                var place = Path.Combine(TargetFolder(target), Local(path));
                if (!File.Exists(place) || SameContent(place, source))
                    continue;
                var user = mods.FirstOrDefault(m => m != replaces && m.Enabled && m.Files.Any(f => IsSameFile(f, target, path) && f.DeployedPath is not null));
                if (user is not null)
                    problems.Add($"{path} is a library in UE4SS's shared folder. \"{user.Name}\" uses it in another version, and both cannot be in the game at once. Disable that mod first.");
                else if (replaces?.Files.Any(f => IsSameFile(f, target, path) && f.DeployedPath is not null) != true)
                    problems.Add($"{path} is already in {TargetName(target)}, but the manager did not install it. Move or delete it first.");
            }
        }
        return new InstallCheck(problems, warnings, replaces, needsUe4ss, editedSettings);
    }

    /// <summary>
    /// Copies the selected parts into the library and deploys them. When the
    /// files belong to an installed mod, <paramref name="replace"/> must be set:
    /// that mod is then swapped out, and put back if the new install fails.
    /// </summary>
    /// <param name="catalogId">The catalog entry the download comes from; see <see cref="Check"/>.</param>
    /// <param name="keepSettings">
    /// On an update, settings files the player changed are kept instead of the new version's defaults.
    /// Either way, the player's version is backed up first.
    /// </param>
    /// <param name="tags">The mod's tags, none when empty; null keeps the ones of the mod it updates.</param>
    /// <param name="ownFolder">The folder below ~mods for the files that go there; see <see cref="Check"/>.</param>
    public ModRecord Install(InstallPreview preview, IReadOnlyCollection<PackageGroup> selected,
        string name, string? version, bool replace = false, string? catalogId = null, bool keepSettings = true,
        IReadOnlyList<string>? tags = null, string? ownFolder = null)
    {
        lock (gate)
        {
            EnsureCanChange();
            var check = Check(preview, selected, catalogId, ownFolder);
            // The install window shows every problem before this; a direct call reports the first.
            if (!check.CanInstall)
                throw new ModException(check.Problems[0]);
            var old = check.Replaces;
            if (old is not null && !replace)
                throw new ModException($"This download uses the same files as \"{old.Name}\". Replace that mod, or uninstall it first.");

            var record = new ModRecord
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                Name = string.IsNullOrWhiteSpace(name) ? preview.SuggestedName : name.Trim(),
                Version = string.IsNullOrWhiteSpace(version) ? null : version.Trim(),
                SourceFileName = preview.SourceFileName,
                InstalledAt = DateTimeOffset.Now,
                CatalogId = catalogId ?? old?.CatalogId,
                Recommended = old?.Recommended ?? false,
                Tags = tags is null ? old?.Tags : tags.Count == 0 ? null : [.. tags],
                Variant = VariantOf(selected),
            };
            var libraryFolder = LibraryFolderOf(record);
            var position = old is null ? mods.Count : mods.IndexOf(old);

            void Undo()
            {
                mods.Remove(record);
                RemoveDeployedFiles(record);
                FileSystem.TryDeleteDirectory(libraryFolder);
                if (old is not null && !mods.Contains(old))
                    mods.Insert(Math.Min(position, mods.Count), old);
            }

            // Refuses before anything is touched when the old files were changed or are in use.
            // Settings the player edited in the game go into the old mod's library copy first.
            var removal = old is { Enabled: true } ? Undeploy(old) : null;
            try
            {
                if (old is not null)
                    BackUpEditedSettings(old);
                Directory.CreateDirectory(libraryFolder);
                foreach (var package in selected)
                {
                    for (var i = 0; i < package.Files.Count; i++)
                    {
                        var relative = InFolder(ownFolder, package.Target, package.RelativePaths[i]);
                        // A shared library that goes with several selected UE4SS mods is kept once.
                        if (record.Files.Any(f => IsSameFile(f, package.Target, relative)))
                            continue;
                        var kept = keepSettings ? EditedSettingsCopy(old, package.Target, relative) : null;
                        var keptByPlayer = old?.Files.Any(f => f.Kept && IsSameFile(f, package.Target, relative)) == true;
                        AddToLibrary(record, package.Target, relative, package.Files[i], kept, keptByPlayer);
                    }
                    if (package.Kind == PackageKind.Ue4ss
                        && !package.RelativePaths.Any(p => p.Equals($"{package.Name}/{Ue4ssEnabledMarker}", StringComparison.OrdinalIgnoreCase)))
                        AddToLibrary(record, package.Target, $"{package.Name}/{Ue4ssEnabledMarker}", source: null);
                }
                Deploy(record);
                if (old is not null)
                    mods.Remove(old);
                mods.Insert(position, record);
            }
            catch
            {
                Undo();
                removal?.PutBack();
                throw;
            }
            var setsBefore = sets;
            if (old is not null)
                sets = WithModReplaced(sets, old.Id, record.Id);
            Commit(removal, () =>
            {
                Undo();
                sets = setsBefore;
            });

            if (old is not null)
                FileSystem.TryDeleteDirectory(LibraryFolderOf(old));
            Log.Info(old is null
                ? $"Installed \"{record.Name}\" ({record.Files.Count} file(s), {record.TypeLabel})."
                : $"Replaced \"{old.Name}\" with \"{record.Name}\" ({record.Files.Count} file(s), {record.TypeLabel}).");
            return record;
        }
    }

    /// <summary>
    /// Gives a mod another name, version and tags, as the install review would have. Only the mod list changes: the
    /// library keeps a mod's files under its id, and the game's files keep their names, so this works while the
    /// game runs.
    /// </summary>
    /// <param name="version">Null or blank for none.</param>
    /// <param name="tags">The new tags, none when empty; null leaves them as they are.</param>
    public void Rename(string id, string name, string? version, IReadOnlyList<string>? tags = null)
    {
        lock (gate)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ModException($"Give the mod a name.");
            var mod = Find(id);
            var (oldName, oldVersion, oldTags) = (mod.Name, mod.Version, mod.Tags);
            mod.Name = name.Trim();
            mod.Version = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
            if (tags is not null)
                mod.Tags = tags.Count == 0 ? null : [.. tags];
            if (mod.Name == oldName && mod.Version == oldVersion && (mod.Tags ?? []).SequenceEqual(oldTags ?? []))
                return;
            Commit(undo: () => (mod.Name, mod.Version, mod.Tags) = (oldName, oldVersion, oldTags));
            Log.Info($"Renamed \"{oldName}\" ({oldVersion ?? "no version"}) to \"{mod.Name}\" ({mod.Version ?? "no version"}), tags: {ModTags.Join(mod.Tags)}.");
        }
    }

    public void SetEnabled(string id, bool enabled)
    {
        lock (gate)
        {
            var mod = Find(id);
            if (mod.Enabled == enabled)
                return;
            EnsureCanChange();
            if (enabled)
            {
                var clash = mods.FirstOrDefault(other => other != mod && other.Enabled
                    && other.Files.Any(f => mod.Files.Any(g => !g.IsShared && IsSameFile(f, g.Target, g.FileName))));
                if (clash is not null)
                    throw new ModException($"\"{mod.Name}\" uses the same files as \"{clash.Name}\". Disable that mod first.");
                Deploy(mod);
                Commit(undo: () => RemoveDeployedFiles(mod));
            }
            else
            {
                Commit(Undeploy(mod));
            }
            Log.Info($"{(enabled ? "Enabled" : "Disabled")} \"{mod.Name}\".");
        }
    }

    /// <summary>
    /// The mods that <see cref="TurnOffForTest"/> turned off and that are still installed and off; empty when no
    /// such test is on.
    /// </summary>
    public IReadOnlyList<ModRecord> OffForTest
    {
        get
        {
            lock (gate)
                return mods.Where(m => !m.Enabled && offForTest.Contains(m.Id)).ToList();
        }
    }

    /// <summary>
    /// Turns every enabled mod off, all or none, and remembers which ones, so <see cref="TurnBackOn"/> turns exactly
    /// those on again: to see whether the game runs without mods. Files the manager did not place, and UE4SS
    /// itself, stay in the game.
    /// </summary>
    /// <returns>How many mods were turned off.</returns>
    public int TurnOffForTest()
    {
        lock (gate)
        {
            var on = mods.Where(m => m.Enabled).Select(m => m.Id).ToList();
            if (on.Count == 0)
                return 0;
            EnsureCanChange();
            Apply(new HashSet<string>(), [.. offForTest.Union(on)]);
            Log.Info($"Turned {on.Count} mod(s) off to test the game without mods.");
            return on.Count;
        }
    }

    /// <summary>
    /// Turns the mods <see cref="TurnOffForTest"/> turned off on again, all or none, and ends the test. Mods the
    /// player turned on meanwhile stay on.
    /// </summary>
    /// <returns>How many mods were turned on.</returns>
    public int TurnBackOn()
    {
        lock (gate)
        {
            if (offForTest.Count == 0)
                return 0;
            EnsureCanChange();
            var wanted = mods.Where(m => m.Enabled || offForTest.Contains(m.Id)).Select(m => m.Id).ToHashSet();
            var count = mods.Count(m => !m.Enabled && wanted.Contains(m.Id));
            Apply(wanted, []);
            Log.Info($"Turned {count} mod(s) back on after the test without mods.");
            return count;
        }
    }

    public IReadOnlyList<ModSet> Sets
    {
        get
        {
            lock (gate)
                return sets.ToList();
        }
    }

    /// <summary>
    /// Saves the mods that are on now as a set, in place of a set with the same name. While a test without mods is
    /// on, the mods it turned off count as on: they are the player's own choice.
    /// </summary>
    public ModSet SaveSet(string name)
    {
        lock (gate)
        {
            name = name.Trim();
            if (name.Length == 0)
                throw new ModException($"Give the set a name.");
            var set = new ModSet(name, [.. mods.Where(m => !m.Recommended && (m.Enabled || offForTest.Contains(m.Id))).Select(m => m.Id)]);
            var before = sets;
            var changed = sets.ToList();
            var index = changed.FindIndex(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                changed.Add(set);
            else
                changed[index] = set;
            sets = changed;
            Commit(undo: () => sets = before);
            Log.Info($"Saved the mod set \"{name}\" with {set.ModIds.Count} mod(s).");
            return set;
        }
    }

    /// <summary>
    /// Makes exactly the mods of a set enabled, all or none, and ends a test without mods: the other mods are turned
    /// off.
    /// </summary>
    /// <returns>How many mods were turned on and how many off.</returns>
    public (int On, int Off) ApplySet(string name)
    {
        lock (gate)
        {
            var set = FindSet(name);
            // A set leaves the recommended mod as it is: one saved before that mod came must not turn it off.
            var recommended = mods.Where(m => m.Recommended).ToList();
            var wanted = set.ModIds.Except(recommended.Select(m => m.Id))
                .Concat(recommended.Where(m => m.Enabled || offForTest.Contains(m.Id)).Select(m => m.Id))
                .ToHashSet();
            var on = mods.Count(m => !m.Enabled && wanted.Contains(m.Id));
            var off = mods.Count(m => m.Enabled && !wanted.Contains(m.Id));
            if (on + off > 0)
                EnsureCanChange();
            if (on + off > 0 || offForTest.Count > 0)
                Apply(wanted, []);
            Log.Info($"Applied the mod set \"{set.Name}\": {on} mod(s) turned on, {off} off.");
            return (on, off);
        }
    }

    public void DeleteSet(string name)
    {
        lock (gate)
        {
            var set = FindSet(name);
            var before = sets;
            sets = [.. sets.Where(s => !ReferenceEquals(s, set))];
            Commit(undo: () => sets = before);
            Log.Info($"Deleted the mod set \"{set.Name}\".");
        }
    }

    private ModSet FindSet(string name) =>
        sets.FirstOrDefault(s => s.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new ModException($"There is no mod set named \"{name}\".");

    /// <summary>The sets with a mod's id replaced by the id of its update, or taken out when <paramref name="newId"/> is null.</summary>
    private static List<ModSet> WithModReplaced(List<ModSet> sets, string id, string? newId) =>
        [.. sets.Select(set => !set.ModIds.Contains(id) ? set : set with
        {
            ModIds = newId is null ? [.. set.ModIds.Where(m => m != id)] : [.. set.ModIds.Select(m => m == id ? newId : m)],
        })];

    /// <summary>
    /// Makes exactly the mods with these ids enabled, all or none: the mods to turn off are checked and moved aside
    /// first, then the others are copied in, and the list is saved last. When a step fails, everything goes back.
    /// </summary>
    /// <param name="offAfter">The mods a test without mods turned off, as saved with the change.</param>
    private void Apply(IReadOnlySet<string> wanted, List<string> offAfter)
    {
        var off = mods.Where(m => m.Enabled && !wanted.Contains(m.Id)).ToList();
        var on = mods.Where(m => !m.Enabled && wanted.Contains(m.Id)).ToList();
        var enabledAfter = mods.Where(m => wanted.Contains(m.Id)).ToList();
        foreach (var mod in on)
        {
            var clash = enabledAfter.FirstOrDefault(other => other != mod
                && other.Files.Any(f => mod.Files.Any(g => !g.IsShared && IsSameFile(f, g.Target, g.FileName))));
            if (clash is not null)
                throw new ModException($"\"{mod.Name}\" uses the same files as \"{clash.Name}\". Disable that mod first.");
        }

        var removal = off.Count > 0 ? Undeploy(off) : null;
        var deployed = new List<ModRecord>();
        try
        {
            foreach (var mod in on)
            {
                Deploy(mod);
                deployed.Add(mod);
            }
        }
        catch
        {
            deployed.ForEach(RemoveDeployedFiles);
            removal?.PutBack();
            throw;
        }
        var before = offForTest;
        offForTest = offAfter;
        Commit(removal, () =>
        {
            deployed.ForEach(RemoveDeployedFiles);
            offForTest = before;
        });
    }

    /// <summary>Removes a mod from the game and from the library.</summary>
    /// <returns>The folder the player's changed settings were backed up to, or null when there were none.</returns>
    public string? Uninstall(string id)
    {
        lock (gate)
        {
            EnsureCanChange();
            var mod = Find(id);
            var removal = mod.Enabled ? Undeploy(mod) : null;
            string? backup;
            try
            {
                backup = BackUpEditedSettings(mod);
            }
            catch
            {
                removal?.PutBack();
                throw;
            }
            var position = mods.IndexOf(mod);
            mods.Remove(mod);
            var setsBefore = sets;
            sets = WithModReplaced(sets, mod.Id, null);
            Commit(removal, () =>
            {
                mods.Insert(position, mod);
                sets = setsBefore;
                if (backup is not null)
                    FileSystem.TryDeleteDirectory(backup);
            });
            FileSystem.TryDeleteDirectory(LibraryFolderOf(mod));
            Log.Info($"Uninstalled \"{mod.Name}\".");
            return backup;
        }
    }

    /// <summary>
    /// Puts back the settings files a mod shipped with, in the game and in the library. The player's
    /// version is backed up first. When a copy or saving the mod list fails, every file and record is
    /// put back as it was.
    /// </summary>
    /// <returns>The backup folder, or null when the settings were already the shipped ones.</returns>
    public string? ResetSettings(string id)
    {
        lock (gate)
        {
            EnsureCanChange();
            var mod = Find(id);
            SaveEditedSettings(mod);
            var edited = mod.Files.Where(f => f.IsEdited).ToList();
            if (edited.Count == 0)
            {
                Save();
                return null;
            }
            if (edited.Any(f => !File.Exists(DefaultsPath(mod, f))))
                throw new ModException($"The original settings of \"{mod.Name}\" were not kept: it was installed with an older version of the manager. Install the mod again to get them back.");

            var backup = BackUpEditedSettings(mod);
            // The player's version of each file and its record, put back when a copy or the save fails.
            var before = edited.Select(file => (Record: file, Library: File.ReadAllBytes(LibraryPath(mod, file)),
                Deployed: file.DeployedPath is not null && File.Exists(file.DeployedPath) ? File.ReadAllBytes(file.DeployedPath) : null,
                file.Size, file.Sha256, file.DefaultSha256)).ToList();
            void Undo()
            {
                foreach (var (record, library, deployed, size, sha256, defaultSha256) in before)
                {
                    try
                    {
                        File.WriteAllBytes(LibraryPath(mod, record), library);
                        if (deployed is not null)
                            File.WriteAllBytes(record.DeployedPath!, deployed);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Log.Error($"{record.FileName} of \"{mod.Name}\" could not be put back; the player's version is in '{backup}'.", ex);
                    }
                    record.Size = size;
                    record.Sha256 = sha256;
                    record.DefaultSha256 = defaultSha256;
                }
            }

            try
            {
                foreach (var file in edited)
                {
                    var original = DefaultsPath(mod, file);
                    File.Copy(original, LibraryPath(mod, file), overwrite: true);
                    if (file.DeployedPath is not null)
                        File.Copy(original, file.DeployedPath, overwrite: true);
                    file.Size = new FileInfo(original).Length;
                    file.Sha256 = FileSystem.Sha256(original);
                    file.DefaultSha256 = file.Sha256;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Undo();
                throw new ModException($"The settings of \"{mod.Name}\" could not be reset, so nothing was changed. A file may be in use. ({ex.Message})");
            }
            Commit(undo: Undo);
            Log.Info($"Reset the settings of \"{mod.Name}\" to the shipped ones.");
            return backup;
        }
    }

    /// <summary>Whether the player changed a settings file of the mod, in the game or (saved there before) in the library copy.</summary>
    public static bool HasEditedSettings(ModRecord mod) => EditedSettings(mod).Count > 0;

    /// <summary>
    /// Marks the mods the app recommends, each found by the repo it came from or by its folder in UE4SS's Mods folder,
    /// as for a copy installed by hand; any other mod loses the mark. With none, as when the manifest names no mod any
    /// more, no mod has it. Only the mod list changes, so the game may run.
    /// </summary>
    public void MarkRecommended(IReadOnlyCollection<(string Id, string? Folder)> wanted)
    {
        lock (gate)
        {
            var marks = new HashSet<ModRecord>();
            var changed = false;
            foreach (var (id, folder) in wanted)
            {
                var mark = mods.FirstOrDefault(m => string.Equals(m.CatalogId, id, StringComparison.OrdinalIgnoreCase))
                    ?? (folder is null ? null : mods.FirstOrDefault(m => m.Files.Any(f => f.Target == DeployTarget.Ue4ssMods
                        && f.FileName.Split('/')[0].Equals(folder, StringComparison.OrdinalIgnoreCase))));
                if (mark is null)
                    continue;
                marks.Add(mark);
                if (mark.CatalogId is null)
                {
                    mark.CatalogId = id;
                    changed = true;
                }
            }
            foreach (var mod in mods)
            {
                changed |= mod.Recommended != marks.Contains(mod);
                mod.Recommended = marks.Contains(mod);
            }
            if (!changed)
                return;
            try
            {
                Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"The recommended mod could not be noted yet: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Keeps the player's version of the files of a mod that were changed outside the manager, as a value in its
    /// main.lua: from now on they are handled as settings files (<see cref="ModFileRecord.Kept"/>), so a change no
    /// longer stops a disable, an update or an uninstall. The mod's own version is kept as the original, which
    /// <see cref="ResetSettings"/> puts back. Only the library and the mod list change, so the game may run.
    /// </summary>
    /// <returns>The files kept; none when no file was changed.</returns>
    public IReadOnlyList<string> KeepChangedFiles(string id)
    {
        lock (gate)
        {
            var mod = Find(id);
            var changed = ChangedFiles(mod);
            if (changed.Count == 0)
                return [];
            var before = changed.Select(file => (Record: file, Library: File.ReadAllBytes(LibraryPath(mod, file)), file.Size, file.Sha256)).ToList();
            void Undo()
            {
                foreach (var (record, library, size, sha256) in before)
                {
                    try
                    {
                        File.WriteAllBytes(LibraryPath(mod, record), library);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Log.Error($"The manager's copy of {record.FileName} of \"{mod.Name}\" could not be put back.", ex);
                    }
                    record.Kept = false;
                    record.DefaultSha256 = null;
                    record.Size = size;
                    record.Sha256 = sha256;
                }
            }

            try
            {
                foreach (var file in changed)
                {
                    // The manager's copy is still the mod's own version: it becomes the original to reset to.
                    var original = DefaultsPath(mod, file);
                    Directory.CreateDirectory(Path.GetDirectoryName(original)!);
                    File.Copy(LibraryPath(mod, file), original, overwrite: true);
                    file.DefaultSha256 = file.Sha256;
                    file.Kept = true;
                }
                SaveEditedSettings(mod);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Undo();
                throw new ModException($"Your version of the files of \"{mod.Name}\" could not be kept, so nothing was changed. A file may be in use. ({ex.Message})");
            }
            Commit(undo: Undo);
            Log.Info($"Kept the player's version of {string.Join(", ", changed.Select(f => f.FileName))} of \"{mod.Name}\".");
            return changed.Select(f => f.FileName).ToList();
        }
    }

    /// <summary>
    /// The files of an enabled mod that were changed outside the manager and so stop a removal, by checksum: not
    /// settings files or kept ones, the game's own files, or shared libraries. With <paramref name="checksumUpTo"/>,
    /// a bigger file is compared by size only, for a quick look.
    /// </summary>
    public static IReadOnlyList<ModFileRecord> ChangedFiles(ModRecord mod, long checksumUpTo = long.MaxValue) => mod.Enabled
        ? mod.Files
            .Where(f => !f.IsPlayerFile && !f.IsGameFile && !f.IsShared && !f.Borrowed
                && f.DeployedPath is { } path && new FileInfo(path) is { Exists: true } info
                && (info.Length != f.Size || (f.Size <= checksumUpTo && !Matches(path, f))))
            .ToList()
        : [];

    /// <summary>
    /// Moves the enabled mods out of <see cref="OtherInstallRoot"/> into this install. They are removed
    /// there all together or not at all, with the usual checks, and then deployed here. A file that is
    /// already here with the same content, as in a copy of the game folder, is taken over. When the mod
    /// list cannot be saved, the move is undone: the mods stay in the other install.
    /// </summary>
    /// <returns>The mods that could not be deployed here, because another file is in the way; they stay disabled.</returns>
    public IReadOnlyList<ModRecord> MoveHere()
    {
        lock (gate)
        {
            if (OtherInstallRoot is not { } other)
                return [];
            if (isGameRunning(Game)
                || (GameLocator.Resolve(Game.Profile, other, GameStore.Manual) is { } install && isGameRunning(install)))
                throw new ModException($"{Game.Profile.DisplayName} is running. Close the game first, then try again.");

            var moving = mods.Where(IsInOtherInstall).ToList();
            var removal = Undeploy(moving);
            var stayed = new List<ModRecord>();
            // Files that were already here with the same content; undoing the move leaves them.
            var taken = new HashSet<ModFileRecord>();
            foreach (var mod in moving)
            {
                try
                {
                    taken.UnionWith(Deploy(mod, takeIdentical: true));
                }
                catch (Exception ex) when (ex is ModException or IOException or UnauthorizedAccessException)
                {
                    Log.Warn($"\"{mod.Name}\" was removed from '{other}' but could not be enabled here: {ex.Message}");
                    stayed.Add(mod);
                }
            }

            void Undo()
            {
                foreach (var file in moving.SelectMany(m => m.Files).Where(f => f.DeployedPath is not null))
                {
                    if (!taken.Contains(file))
                    {
                        FileSystem.TryDeleteFile(file.DeployedPath!);
                        RemoveEmptyFolders(file, file.DeployedPath!, ProjectFolder);
                    }
                    file.DeployedPath = null;
                }
                foreach (var mod in moving)
                    mod.Enabled = false;
            }
            Commit(removal, Undo);
            OtherInstallRoot = FindOtherInstall();
            Log.Info($"Moved {moving.Count - stayed.Count} of {moving.Count} enabled mod(s) from '{other}' to '{Game.RootPath}'.");
            return stayed;
        }
    }

    /// <summary>
    /// Installs or updates UE4SS from its release package (a zip, 7z or folder holding
    /// dwmapi.dll and the ue4ss folder) next to the game's shipping executable, in Binaries\Win64
    /// in most games. Files of managed mods are kept, and so are settings the user changed. Files of
    /// the previous release that this one no longer has are removed, unless the user changed them.
    /// If a step fails, every file is put back.
    /// </summary>
    /// <param name="package">The release zip, 7z or folder.</param>
    /// <param name="version">The release version when known, as from the manifest; otherwise read from the file name.</param>
    public Ue4ssInstallResult InstallUe4ss(string package, string? version = null)
    {
        lock (gate)
        {
            EnsureCanChange();
            var staging = Path.Combine(paths.StagingDir, Guid.NewGuid().ToString("N"));
            var journal = new List<(string Destination, string? Backup)>();
            try
            {
                ArchiveStager.Stage(package, staging);
                var root = Ue4ssPackageRoot(staging)
                    ?? throw new ModException($"This is not a UE4SS release: ue4ss\\UE4SS.dll was not found in it.");
                var win64 = Game.BinariesPath;
                if (Game.FindShippingExecutable() is null)
                    throw new ModException($"The game's exe was not found in {win64}, so UE4SS cannot be installed. Use Report a problem, so the game profile can be corrected.");

                // Proxy DLLs such as dwmapi.dll stand in for a Windows library the game loads.
                // One that is already there while UE4SS is not belongs to another tool.
                var updating = Ue4ssRuntime.Detect(Game).Installed;
                foreach (var proxy in Directory.EnumerateFiles(root, "*.dll").Select(f => Path.GetFileName(f)))
                {
                    if (!updating && File.Exists(Path.Combine(win64, proxy)))
                        throw new ModException($"{proxy} is already in {win64}, but UE4SS is not installed: another tool uses it. Remove that tool first, or install UE4SS by hand.");
                }

                var owned = mods.SelectMany(m => m.Files)
                    .Where(f => f.DeployedPath is not null)
                    .Select(f => Path.GetFullPath(f.DeployedPath!))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                // What the previous release put into the game; null for UE4SS installed by hand or by an
                // older version of the manager, which recorded no files.
                var previous = ReadUe4ssRecord()?.Files is { } recorded
                    ? new Dictionary<string, string>(recorded, StringComparer.OrdinalIgnoreCase)
                    : null;
                var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var kept = new List<string>();
                var removed = new List<string>();
                var written = 0;
                // Listed first: the backups below go into the staging folder, which may be the package root.
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToList())
                {
                    var relative = Path.GetRelativePath(root, file);
                    var name = relative.Replace('\\', '/');
                    files[name] = FileSystem.Sha256(file);
                    var destination = Path.GetFullPath(Path.Combine(win64, relative));
                    if (owned.Contains(destination)
                        || (Ue4ssBuild.IsUserFile(file) && File.Exists(destination) && !IsAsInstalled(destination, name, previous)))
                    {
                        kept.Add(name);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    string? backup = null;
                    if (File.Exists(destination))
                    {
                        backup = Path.Combine(staging, ".replaced", relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                        File.Copy(destination, backup);
                    }
                    journal.Add((destination, backup));
                    var temp = destination + ".uemm-tmp";
                    File.Copy(file, temp, overwrite: true);
                    File.Move(temp, destination, overwrite: true);
                    written++;
                }

                // A file the previous release had and this one has not would still be read by UE4SS,
                // such as an old signature file.
                var top = Path.TrimEndingDirectorySeparator(Path.GetFullPath(win64)) + Path.DirectorySeparatorChar;
                foreach (var name in (previous?.Keys ?? Enumerable.Empty<string>()).Where(n => !files.ContainsKey(n)).Order(StringComparer.OrdinalIgnoreCase))
                {
                    var destination = Path.GetFullPath(Path.Combine(win64, name));
                    if (!destination.StartsWith(top, StringComparison.OrdinalIgnoreCase) || owned.Contains(destination) || !File.Exists(destination))
                        continue;
                    if (!IsAsInstalled(destination, name, previous))
                    {
                        kept.Add(name);
                        continue;
                    }
                    var backup = Path.Combine(staging, ".replaced", name);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(destination, backup);
                    journal.Add((destination, backup));
                    File.Delete(destination);
                    removed.Add(name);
                }

                JsonFile.Write(paths.Ue4ssRecordFile(Game.Profile.Id), new Ue4ssInstallRecord
                {
                    Version = version ?? Ue4ssManifest.VersionFromFileName(package),
                    Source = Path.GetFileName(Path.TrimEndingDirectorySeparator(package)),
                    InstalledAt = DateTimeOffset.Now,
                    Files = files,
                });
                foreach (var name in removed)
                    RemoveEmptyFolders(name, Path.Combine(win64, name));
                Log.Info($"{(updating ? "Updated" : "Installed")} UE4SS from '{package}': {written} file(s) written, "
                    + $"{kept.Count} kept, {removed.Count} old file(s) removed.");
                return new Ue4ssInstallResult(win64, written, kept, updating, removed);
            }
            catch
            {
                RollBack(journal);
                throw;
            }
            finally
            {
                FileSystem.TryDeleteDirectory(staging);
            }
        }
    }

    /// <summary>The UE4SS version the manager installed, or null when it did not install UE4SS or could not tell.</summary>
    public string? InstalledUe4ssVersion => ReadUe4ssRecord()?.Version;

    /// <summary>The record of the UE4SS release the manager installed; null when there is none or it cannot be read.</summary>
    private Ue4ssInstallRecord? ReadUe4ssRecord()
    {
        try
        {
            return JsonFile.Read<Ue4ssInstallRecord>(paths.Ue4ssRecordFile(Game.Profile.Id));
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Whether a UE4SS file is still as the previous release put it there, so the user did not change it.</summary>
    private static bool IsAsInstalled(string path, string name, Dictionary<string, string>? previous) =>
        previous is not null
        && previous.TryGetValue(name, out var sha256)
        && string.Equals(FileSystem.Sha256(path), sha256, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Deletes UE4SS's log, so the old lines are gone and the next game start begins a fresh one. It waits for the
    /// game to close, as every change in the game folder does: UE4SS writes the log while the game runs.
    /// </summary>
    /// <returns>Whether there was a log to delete.</returns>
    public bool ClearUe4ssLog()
    {
        lock (gate)
        {
            EnsureCanChange();
            var log = Ue4ssRuntime.Detect(Game).LogPath;
            if (!File.Exists(log))
                return false;
            try
            {
                File.Delete(log);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new ModException($"The UE4SS log could not be deleted: another program may be using it. Close it, then try again. ({ex.Message})");
            }
            Log.Info($"Deleted the UE4SS log '{log}'.");
            return true;
        }
    }

    /// <summary>
    /// Removes UE4SS completely, whoever installed it: the proxy DLL and the whole ue4ss
    /// folder (for UE4SS 2.x, its files and Mods folder in Win64), including UE4SS's
    /// settings and every mod inside. Managed mods that lived there are disabled; their
    /// library copies, with the player's changed settings, stay, so they can be enabled
    /// again once UE4SS is back.
    /// </summary>
    public Ue4ssRemoveResult RemoveUe4ss()
    {
        lock (gate)
        {
            EnsureCanChange();
            var win64 = Game.BinariesPath;
            var current = Path.Combine(win64, "ue4ss");
            var legacy = File.Exists(Path.Combine(win64, "UE4SS.dll"));
            if (!Directory.Exists(current) && !legacy)
                throw new ModException($"UE4SS is not installed in this game.");

            var affected = mods.Where(m => m.Enabled && m.Files.Any(f => f.Target == DeployTarget.Ue4ssMods)).ToList();
            // A mixed mod's files outside UE4SS's folder, such as its pak, are checked like any removal.
            var outside = affected.SelectMany(m => m.Files)
                .Where(f => f.Target != DeployTarget.Ue4ssMods && f.DeployedPath is not null && File.Exists(f.DeployedPath))
                .ToList();
            var changed = outside.Where(f => !Matches(f.DeployedPath!, f)).Select(f => f.FileName).ToList();
            if (changed.Count > 0)
                throw new ModException($"These mod files were changed outside the manager, so nothing was removed: {string.Join(", ", changed)}.");
            foreach (var mod in affected)
                SaveEditedSettings(mod);

            var folders = new List<string> { current };
            var files = new List<string> { Path.Combine(win64, "dwmapi.dll") };
            if (legacy)
            {
                folders.Add(Path.Combine(win64, "Mods"));
                files.AddRange(new[] { "UE4SS.dll", "UE4SS.pdb", "UE4SS-settings.ini", "UE4SS.log", "xinput1_3.dll" }
                    .Select(name => Path.Combine(win64, name)));
            }

            // UE4SS and the mixed mods' outside files are moved aside together, so nothing is left half removed.
            var items = folders.Where(Directory.Exists)
                .Concat(files.Where(File.Exists))
                .Concat(outside.Select(f => f.DeployedPath!));
            var removal = new Removal(Park(items), ProjectFolder);
            foreach (var mod in affected)
                removal.Disable(mod);
            Commit(removal);
            FileSystem.TryDeleteFile(paths.Ue4ssRecordFile(Game.Profile.Id));
            Log.Info($"Removed UE4SS from '{win64}'; {affected.Count} mod(s) disabled.");
            return new Ue4ssRemoveResult(affected.Count);
        }
    }

    /// <summary>What <see cref="ResetModding"/> would change.</summary>
    public ResetPreview PreviewReset()
    {
        lock (gate)
            return new ResetPreview(mods.Count(m => m.Enabled), mods.Count, IsUe4ssInstalled(), OtherModFiles().Count);
    }

    /// <summary>
    /// Takes the game back to its original files, so the player can start clean: every enabled mod is
    /// turned off (all or none), UE4SS is removed completely, and files the manager did not install in
    /// the mods folders (~mods, LogicMods) are moved to a backup folder; the emptied folders go. With
    /// <paramref name="deleteMods"/>, the mods also leave the manager, as when each is uninstalled.
    /// Each step is saved before the next, so a step that fails leaves a consistent state, and a second
    /// reset finishes the work.
    /// </summary>
    public ResetResult ResetModding(bool deleteMods)
    {
        lock (gate)
        {
            EnsureCanChange();
            var enabled = mods.Where(m => m.Enabled).ToList();
            if (enabled.Count > 0)
                Commit(Undeploy(enabled));

            var ue4ssRemoved = IsUe4ssInstalled();
            if (ue4ssRemoved)
                RemoveUe4ss();

            var others = OtherModFiles();
            string? backup = null;
            if (others.Count > 0)
            {
                backup = Path.Combine(paths.BackupDir(Game.Profile.Id), $"{DateTime.Now:yyyyMMdd-HHmmss} reset");
                foreach (var (file, relative) in others)
                {
                    var target = Path.Combine(backup, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    try
                    {
                        File.Move(file, target);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        throw new ModException($"{relative} could not be moved out of the game: a program may be using it. Close it, then reset again. ({ex.Message})");
                    }
                }
                Log.Info($"Moved {others.Count} file(s) the manager did not install to '{backup}'.");
            }
            // The game comes without these folders; mods made them.
            foreach (var folder in new[] { Game.ModsPath, Game.LogicModsPath })
            {
                if (Directory.Exists(folder) && !Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any())
                    FileSystem.TryDeleteDirectory(folder);
            }

            var deleted = 0;
            if (deleteMods)
            {
                foreach (var mod in mods.ToList())
                {
                    Uninstall(mod.Id);
                    deleted++;
                }
            }
            Log.Info($"Reset modding: {enabled.Count} mod(s) turned off, UE4SS {(ue4ssRemoved ? "removed" : "not installed")}, "
                + $"{others.Count} other file(s) moved aside, {deleted} mod(s) deleted.");
            return new ResetResult(enabled.Count, ue4ssRemoved, others.Count, backup, deleted);
        }
    }

    private bool IsUe4ssInstalled() =>
        Directory.Exists(Path.Combine(Game.BinariesPath, "ue4ss")) || File.Exists(Path.Combine(Game.BinariesPath, "UE4SS.dll"));

    /// <summary>
    /// Files in the mods folders that no mod deployed, each with its path below the folder's own name,
    /// such as "~mods\Other_P.pak".
    /// </summary>
    private List<(string File, string Relative)> OtherModFiles()
    {
        var owned = mods.SelectMany(m => m.Files)
            .Where(f => f.DeployedPath is not null)
            .Select(f => Path.GetFullPath(f.DeployedPath!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<(string File, string Relative)>();
        foreach (var folder in new[] { Game.ModsPath, Game.LogicModsPath })
        {
            if (!Directory.Exists(folder))
                continue;
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(Path.GetFullPath))
            {
                if (!owned.Contains(file) && seen.Add(file))
                    files.Add((file, Path.Combine(name, Path.GetRelativePath(folder, file))));
            }
        }
        return files;
    }

    /// <summary>The folder in an unpacked UE4SS release that holds ue4ss\UE4SS.dll; the shallowest wins.</summary>
    private static string? Ue4ssPackageRoot(string staging) =>
        Directory.EnumerateDirectories(staging, "*", SearchOption.AllDirectories)
            .Prepend(staging)
            .Where(folder => File.Exists(Path.Combine(folder, "ue4ss", "UE4SS.dll")))
            .OrderBy(folder => folder.Length)
            .FirstOrDefault();

    /// <summary>Undoes a failed UE4SS install: new files are deleted, replaced ones restored.</summary>
    private static void RollBack(List<(string Destination, string? Backup)> journal)
    {
        for (var i = journal.Count - 1; i >= 0; i--)
        {
            var (destination, backup) = journal[i];
            FileSystem.TryDeleteFile(destination + ".uemm-tmp");
            try
            {
                if (backup is null)
                    FileSystem.TryDeleteFile(destination);
                else
                    File.Copy(backup, destination, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error($"'{destination}' could not be restored after a failed UE4SS install.", ex);
            }
        }
    }

    /// <summary>
    /// A quick integrity check by file size. Checksums are compared only before files are removed.
    /// Settings files and kept ones are not checked: the player may change them. Neither are borrowed shared files.
    /// </summary>
    public static ModIntegrity Integrity(ModRecord mod)
    {
        if (!mod.Enabled)
            return ModIntegrity.NotDeployed;
        foreach (var file in mod.Files)
        {
            var info = file.DeployedPath is null ? null : new FileInfo(file.DeployedPath);
            if (info is null || !info.Exists)
                return ModIntegrity.FilesMissing;
            // A borrowed shared file belongs to whoever put it there, such as UE4SS, which may update it.
            // A game file is written by the game itself, so only its presence is checked.
            if (!file.IsPlayerFile && !file.Borrowed && !file.IsGameFile && info.Length != file.Size)
                return ModIntegrity.FilesChanged;
        }
        return ModIntegrity.Intact;
    }

    /// <summary>Files in the ~mods folder that no installed mod deployed, relative to that folder.</summary>
    public IReadOnlyList<string> UnmanagedFiles()
    {
        if (!Directory.Exists(Game.ModsPath))
            return [];
        HashSet<string> owned;
        lock (gate)
        {
            owned = mods.SelectMany(m => m.Files)
                .Where(f => f.DeployedPath is not null)
                .Select(f => Path.GetFullPath(f.DeployedPath!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        return Directory.EnumerateFiles(Game.ModsPath, "*", SearchOption.AllDirectories)
            .Where(f => !owned.Contains(Path.GetFullPath(f)))
            .Select(f => Path.GetRelativePath(Game.ModsPath, f))
            .ToList();
    }

    private ModRecord Find(string id) =>
        mods.FirstOrDefault(m => m.Id == id) ?? throw new ModException($"That mod is no longer installed.");

    /// <summary>
    /// Every change to the game folder stops while the game runs: it holds the mounted paks and UE4SS.
    /// Changes stop as well while enabled mods are still in another install of the game.
    /// </summary>
    private void EnsureCanChange()
    {
        if (OtherInstallRoot is not null)
            throw new ModException($"Your enabled mods are still in another folder of {Game.Profile.DisplayName}: {OtherInstallRoot}. Move them to this folder first.");
        if (isGameRunning(Game))
            throw new ModException($"{Game.Profile.DisplayName} is running. Close the game first, then try again.");
    }

    private bool IsInThisInstall(string path) =>
        Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Game.RootPath) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>An enabled mod with a deployed file that is still on disk in another install.</summary>
    private bool IsInOtherInstall(ModRecord mod) =>
        mod.Enabled && mod.Files.Any(f => f.DeployedPath is { } path && !IsInThisInstall(path) && File.Exists(path));

    /// <summary>The install folder that holds the files of <see cref="IsInOtherInstall"/> mods, or null when there are none.</summary>
    private string? FindOtherInstall()
    {
        var file = mods.Where(IsInOtherInstall)
            .SelectMany(m => m.Files)
            .Select(f => f.DeployedPath)
            .FirstOrDefault(path => path is not null && !IsInThisInstall(path) && File.Exists(path));
        if (file is null)
            return null;
        for (var folder = Path.GetDirectoryName(file); folder is not null; folder = Path.GetDirectoryName(folder))
        {
            if (GameLocator.Resolve(Game.Profile, folder, GameStore.Manual) is { } install)
                return install.RootPath;
        }
        // The folder is no game install any more; the place of the file still tells the player where to look.
        return Path.GetDirectoryName(file);
    }

    /// <summary>
    /// After the game folder moved, as with Steam's "Move install folder", the recorded paths point at
    /// a place that is gone. Files that moved along are found again in this install by name and content
    /// (size and checksum), so a foreign file of the same name is never taken; a settings file, which
    /// the player may have changed, by name. The rest count as missing.
    /// </summary>
    private void FollowMovedInstall()
    {
        Dictionary<DeployTarget, string>? folders = null;
        var followed = 0;
        foreach (var mod in mods.Where(m => m.Enabled))
        {
            var outside = mod.Files.Where(f => f.DeployedPath is { } path && !IsInThisInstall(path)).ToList();
            // A file still there is moved by MoveHere. A drive that is gone, as an unplugged one, may come back.
            if (outside.Count == 0
                || outside.Any(f => File.Exists(f.DeployedPath) || !Directory.Exists(Path.GetPathRoot(f.DeployedPath))))
                continue;
            folders ??= Enum.GetValues<DeployTarget>().ToDictionary(t => t, TargetFolder);
            foreach (var file in outside)
            {
                var here = Path.Combine(folders[file.Target], Local(file.FileName));
                file.DeployedPath = File.Exists(here) && (file.IsPlayerFile || file.IsGameFile || Matches(here, file)) ? here : null;
            }
            followed++;
        }
        if (followed == 0)
            return;
        Log.Info($"The files of {followed} enabled mod(s) are no longer where they were recorded; they are looked for in '{Game.RootPath}' now.");
        try
        {
            Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"The new places of the mod files could not be saved yet: {ex.Message}");
        }
    }

    /// <summary>
    /// Copies one file into the mod's library folder and records it. A null source writes an
    /// empty file. A <paramref name="replacement"/> is copied instead of the source: the player's
    /// own version of a settings file. <paramref name="kept"/> marks a file the player kept in the version
    /// this one replaces (<see cref="ModFileRecord.Kept"/>).
    /// </summary>
    private void AddToLibrary(ModRecord record, DeployTarget target, string relativePath, string? source, string? replacement = null,
        bool kept = false)
    {
        var file = new ModFileRecord { FileName = relativePath, Target = target, Kept = kept };
        var copy = LibraryPath(record, file);
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        if (source is null)
            File.WriteAllBytes(copy, []);
        else
            File.Copy(replacement ?? source, copy);
        file.Size = new FileInfo(copy).Length;
        file.Sha256 = FileSystem.Sha256(copy);
        if (file.IsPlayerFile)
        {
            file.DefaultSha256 = replacement is null || source is null ? file.Sha256 : FileSystem.Sha256(source);
            if (source is not null)
            {
                var original = DefaultsPath(record, file);
                Directory.CreateDirectory(Path.GetDirectoryName(original)!);
                File.Copy(source, original);
            }
        }
        record.Files.Add(file);
    }

    /// <summary>
    /// The manager's copy of a file. Packaged files sit at the top of the mod's
    /// library folder (as they always have); other targets get a subfolder each.
    /// </summary>
    private string LibraryPath(ModRecord mod, ModFileRecord file) => file.Target == DeployTarget.Mods
        ? Path.Combine(LibraryFolderOf(mod), Local(file.FileName))
        : Path.Combine(LibraryFolderOf(mod), file.Target.ToString(), Local(file.FileName));

    /// <summary>A settings file as the mod shipped it, kept for "Reset settings".</summary>
    private string DefaultsPath(ModRecord mod, ModFileRecord file) =>
        Path.Combine(LibraryFolderOf(mod), ".defaults", file.Target.ToString(), Local(file.FileName));

    /// <summary>The game's own version of a file a mod replaced, kept in the mod's library folder.</summary>
    private string ReplacedPath(ModRecord mod, ModFileRecord file) =>
        Path.Combine(LibraryFolderOf(mod), ".replaced", file.Target.ToString(), Local(file.FileName));

    /// <summary>Saves the file the game had at this place, so the mod never destroys it.</summary>
    private void KeepReplaced(ModRecord mod, ModFileRecord file, string place)
    {
        var saved = ReplacedPath(mod, file);
        Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
        File.Copy(place, saved, overwrite: true);
        file.ReplacedSha256 = FileSystem.Sha256(saved);
    }

    /// <summary>Puts the game's own file back where a mod replaced it; the saved copy stays in the library.</summary>
    /// <exception cref="IOException">The file could not be written, as when the game holds the place.</exception>
    private void RestoreReplaced(ModRecord mod, ModFileRecord file, string place)
    {
        var saved = ReplacedPath(mod, file);
        if (!File.Exists(saved))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(place)!);
        File.Copy(saved, place, overwrite: true);
    }

    /// <summary>
    /// Copies the game files of mods that the game or another tool changed since the mod placed them, such as key
    /// bindings or a ReShade.ini, into the backups folder: removing the mod takes them out of the game.
    /// </summary>
    /// <exception cref="ModException">A file could not be read, as when the game or another tool holds it: nothing has left the game then.</exception>
    private void BackUpChangedGameFiles(IEnumerable<ModFileRecord> files, IReadOnlyList<ModRecord> group)
    {
        try
        {
            foreach (var mod in group)
            {
                var changed = mod.Files
                    .Where(f => f.IsGameFile && files.Contains(f) && f.DeployedPath is not null && File.Exists(f.DeployedPath) && !Matches(f.DeployedPath, f))
                    .ToList();
                if (changed.Count == 0)
                    continue;
                var folder = Path.Combine(paths.BackupDir(Game.Profile.Id), $"{DateTime.Now:yyyyMMdd-HHmmss} {FileSystem.SafeName(mod.Name, mod.Id)}");
                foreach (var file in changed)
                {
                    var target = Path.Combine(folder, Local(file.FileName));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file.DeployedPath!, target, overwrite: true);
                }
                Log.Info($"Backed up {changed.Count} game file(s) of \"{mod.Name}\" that changed in the game to '{folder}'.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Reading a file the game or another tool holds used to end here as a raw IO error, which the window can
            // only show as "Something went wrong". Nothing has been moved yet, so this is the same refusal as a file
            // that cannot be moved: the player closes whatever holds it and tries again.
            throw new ModException($"Nothing was removed from the game, because a file is in use or protected. Close the game and try again. ({ex.Message})");
        }
    }

    /// <summary>Copies a mod's library files into the game. On failure, every copied file is removed again.</summary>
    /// <param name="takeIdentical">A file already in the game with the same content as the library copy is taken over instead of refused.</param>
    /// <returns>The files that were taken over.</returns>
    private IReadOnlyList<ModFileRecord> Deploy(ModRecord mod, bool takeIdentical = false)
    {
        var folders = Enum.GetValues<DeployTarget>().ToDictionary(t => t, TargetFolder);
        var taken = new List<ModFileRecord>();
        var sharedInUse = new List<ModFileRecord>();
        string? temp = null;
        try
        {
            foreach (var file in mod.Files)
            {
                var source = LibraryPath(mod, file);
                if (!File.Exists(source))
                    throw new ModException($"The manager's copy of {file.FileName} is missing. Reinstall \"{mod.Name}\".");
                var destination = Path.Combine(folders[file.Target], Local(file.FileName));
                if (takeIdentical && !file.IsGameFile && File.Exists(destination) && Matches(destination, file))
                {
                    file.DeployedPath = destination;
                    taken.Add(file);
                    continue;
                }
                // One copy of a shared library serves every mod that brings it. When it was there before any of
                // them, as one of UE4SS's own, it is borrowed and stays when they go.
                if (file.IsShared && File.Exists(destination))
                {
                    if (!Matches(destination, file))
                        throw new ModException($"{file.FileName} is already in UE4SS's shared folder in another version, from another mod or from UE4SS, so \"{mod.Name}\" cannot bring its own. Disable the mod that uses it first.");
                    var others = OtherUsers([mod], destination).ToList();
                    file.Borrowed = others.Count == 0 || others.Any(f => f.Borrowed);
                    file.DeployedPath = destination;
                    sharedInUse.Add(file);
                    continue;
                }
                file.Borrowed = false;
                if (file.IsGameFile)
                {
                    // A file of the game's own folders: the mod's copy takes its place, and the game's file is
                    // saved first, so the mod never destroys it.
                    if (File.Exists(destination))
                        KeepReplaced(mod, file, destination);
                }
                else if (File.Exists(destination))
                {
                    throw new ModException($"{file.FileName} is already in {TargetName(file.Target)}, but it does not belong to \"{mod.Name}\". Move or delete it first.");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                temp = destination + ".uemm-tmp";
                File.Copy(source, temp, overwrite: true);
                File.Move(temp, destination, overwrite: true);
                temp = null;
                file.DeployedPath = destination;
            }
        }
        catch
        {
            if (temp is not null)
                FileSystem.TryDeleteFile(temp);
            // Files taken over were there before, and shared files in use stay too.
            foreach (var file in taken.Concat(sharedInUse))
                file.DeployedPath = null;
            RemoveDeployedFiles(mod);
            throw;
        }
        mod.Enabled = true;
        return taken;
    }

    private Removal Undeploy(ModRecord mod) => Undeploy([mod]);

    /// <summary>
    /// Takes the deployed files of mods out of the game, all or none, and disables the mods. Every
    /// file is checked first, so a file that was changed outside the manager stops the whole
    /// operation; a settings file the player changed is saved into the library instead. Then every
    /// file is moved aside, so a file the running game holds leaves everything in place. The files
    /// are deleted once the change is saved; see <see cref="Commit"/>.
    /// </summary>
    private Removal Undeploy(IReadOnlyList<ModRecord> group)
    {
        // Shared files that other mods still use, or that were there before, stay in the game.
        var files = group.SelectMany(m => m.Files).Where(f => !Keeps(group, f)).ToList();
        // A game file is written by the game itself: its content says nothing about whether the player changed it.
        var changed = files
            .Where(f => !f.IsPlayerFile && !f.IsGameFile && f.DeployedPath is not null && File.Exists(f.DeployedPath) && !Matches(f.DeployedPath, f))
            .ToList();
        if (changed.Count > 0)
            throw new ChangedFilesException(
                $"These files were changed outside the manager, so nothing was removed: {string.Join(", ", changed.Select(f => f.FileName))}.",
                group.Where(m => m.Files.Any(changed.Contains)).Select(m => m.Id).ToList());
        foreach (var mod in group)
            SaveEditedSettings(mod);
        BackUpChangedGameFiles(files, group);

        // Several mods leaving together may share one copy of a library.
        var deployed = files.Where(f => f.DeployedPath is not null && File.Exists(f.DeployedPath)).Select(f => f.DeployedPath!)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var removal = new Removal(Park(deployed), ProjectFolder);
        // The game's own files, which the mods replaced, come back to these places.
        var replaced = group
            .SelectMany(m => m.Files.Where(f => f.IsGameFile && f.ReplacedSha256 is not null && f.DeployedPath is not null)
                .Select(f => (Mod: m, File: f, Place: f.DeployedPath!)))
            .ToList();
        foreach (var mod in group)
            removal.Disable(mod);
        foreach (var (mod, file, place) in replaced)
        {
            // The mod's library folder, with the saved copy, goes on uninstall: an original that cannot come back
            // now would be lost, so nothing changes then.
            try
            {
                RestoreReplaced(mod, file, place);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                removal.PutBack();
                throw new ModException($"The game's own {file.FileName} could not be put back, so nothing was removed. Close the game, then try again. ({ex.Message})");
            }
            removal.Restored(place);
        }
        return removal;
    }

    /// <summary>
    /// Saves the mod list: the last step of a change, so the game and the list always agree. What a
    /// removal moved aside is deleted only now. When the list cannot be saved, <paramref name="undo"/>
    /// reverses the rest of the change, and then the removal is put back.
    /// </summary>
    private void Commit(Removal? removal = null, Action? undo = null)
    {
        try
        {
            Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"The mod list could not be saved to '{StateFile}'; the change is undone.", ex);
            undo?.Invoke();
            removal?.PutBack();
            throw new ModException($"The manager could not save its list of mods, so nothing was changed. Another program, such as a virus scanner or a sync tool, may be using {StateFile}. Try again. ({ex.Message})");
        }
        removal?.Finish();
    }

    /// <summary>
    /// Files and folders a change moved aside in the game. They are deleted once the mod list is
    /// saved. When it cannot be saved, they move back and the mods are enabled again.
    /// </summary>
    private sealed class Removal(List<(string Original, string Parked)> parked, string? projectFolder)
    {
        private readonly List<(ModFileRecord File, string Path)> deployed = [];
        private readonly List<ModRecord> disabled = [];
        private readonly List<string> restored = [];

        /// <summary>Disables an enabled mod whose files are among the moved items, or inside a moved folder.</summary>
        public void Disable(ModRecord mod)
        {
            foreach (var file in mod.Files.Where(f => f.DeployedPath is not null))
            {
                deployed.Add((file, file.DeployedPath!));
                file.DeployedPath = null;
            }
            mod.Enabled = false;
            disabled.Add(mod);
        }

        /// <summary>The places where the game's own file came back in place of a mod's copy, for a change that is undone.</summary>
        public void Restored(string place) => restored.Add(place);

        /// <summary>Deletes what was moved aside, and the mod folders that are empty then.</summary>
        public void Finish()
        {
            foreach (var (_, moved) in parked)
            {
                if (Directory.Exists(moved))
                    FileSystem.TryDeleteDirectory(moved);
                else
                    FileSystem.TryDeleteFile(moved);
            }
            foreach (var (file, path) in deployed)
                RemoveEmptyFolders(file, path, projectFolder);
        }

        public void PutBack()
        {
            // What came back of the game's own has to go before the mod's file can move back.
            foreach (var place in restored)
                FileSystem.TryDeleteFile(place);
            Unpark(parked);
            foreach (var (file, path) in deployed)
                file.DeployedPath = path;
            foreach (var mod in disabled)
                mod.Enabled = true;
        }
    }

    /// <summary>
    /// Copies settings files and kept ones the player changed in the game into the mod's library copy, so the
    /// changes survive a disable, come back on enable, and can be kept on an update.
    /// </summary>
    private void SaveEditedSettings(ModRecord mod)
    {
        foreach (var file in mod.Files.Where(f => f.IsPlayerFile && f.DeployedPath is not null && File.Exists(f.DeployedPath)))
        {
            if (Matches(file.DeployedPath!, file))
                continue;
            File.Copy(file.DeployedPath!, LibraryPath(mod, file), overwrite: true);
            file.DefaultSha256 ??= file.Sha256;
            file.Size = new FileInfo(file.DeployedPath!).Length;
            file.Sha256 = FileSystem.Sha256(file.DeployedPath!);
            Log.Info($"Kept the player's changes to {file.FileName} of \"{mod.Name}\".");
        }
    }

    /// <summary>Settings files of a mod that the player changed: in the game, or (saved there before) in the library copy.</summary>
    private static List<string> EditedSettings(ModRecord mod) => mod.Files
        .Where(f => f.IsEdited
            || (f.IsPlayerFile && f.DeployedPath is not null && File.Exists(f.DeployedPath) && !Matches(f.DeployedPath, f)))
        .Select(f => f.FileName)
        .ToList();

    /// <summary>The library copy of a settings file the player changed in the mod being replaced, or null.</summary>
    private string? EditedSettingsCopy(ModRecord? old, DeployTarget target, string relativePath)
    {
        var file = old?.Files.FirstOrDefault(f => f.IsEdited && IsSameFile(f, target, relativePath));
        return file is null ? null : LibraryPath(old!, file);
    }

    /// <summary>
    /// Copies the settings files the player changed into a new folder below the backup folder.
    /// The library copy must hold the player's version: the mod is disabled, or was just undeployed.
    /// </summary>
    /// <returns>The backup folder, or null when the player changed nothing.</returns>
    private string? BackUpEditedSettings(ModRecord mod)
    {
        var edited = mod.Files.Where(f => f.IsEdited).ToList();
        if (edited.Count == 0)
            return null;
        var folder = Path.Combine(paths.BackupDir(Game.Profile.Id), $"{DateTime.Now:yyyyMMdd-HHmmss} {FileSystem.SafeName(mod.Name, mod.Id)}");
        foreach (var file in edited)
        {
            var target = Path.Combine(folder, Local(file.FileName));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(LibraryPath(mod, file), target, overwrite: true);
        }
        Log.Info($"Backed up the changed settings of \"{mod.Name}\" to '{folder}'.");
        return folder;
    }

    /// <summary>
    /// Moves files and folders aside under a temporary name. When one cannot be moved, as when the
    /// running game holds it, everything moves back and the removal stops with nothing changed.
    /// </summary>
    private static List<(string Original, string Parked)> Park(IEnumerable<string> items)
    {
        var parked = new List<(string Original, string Parked)>();
        try
        {
            foreach (var item in items)
            {
                var moved = item + RemovingSuffix;
                if (Directory.Exists(item))
                {
                    FileSystem.TryDeleteDirectory(moved);
                    Directory.Move(item, moved);
                }
                else
                {
                    File.Move(item, moved, overwrite: true);
                }
                parked.Add((item, moved));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Unpark(parked);
            throw new ModException($"Nothing was removed from the game, because a file is in use or protected. Close the game and try again. ({ex.Message})");
        }
        return parked;
    }

    /// <summary>Moves files and folders moved aside by <see cref="Park"/> back, the last one first.</summary>
    private static void Unpark(List<(string Original, string Parked)> parked)
    {
        for (var i = parked.Count - 1; i >= 0; i--)
        {
            try
            {
                if (Directory.Exists(parked[i].Parked))
                    Directory.Move(parked[i].Parked, parked[i].Original);
                else
                    File.Move(parked[i].Parked, parked[i].Original);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error($"'{parked[i].Original}' could not be put back.", ex);
            }
        }
    }

    /// <summary>Rollback helper: removes files this operation deployed, without checks, except shared files that stay.</summary>
    private void RemoveDeployedFiles(ModRecord mod)
    {
        foreach (var file in mod.Files.Where(f => f.DeployedPath is not null))
        {
            if (!Keeps([mod], file))
            {
                FileSystem.TryDeleteFile(file.DeployedPath!);
                if (file.IsGameFile)
                {
                    try
                    {
                        RestoreReplaced(mod, file, file.DeployedPath!);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Log.Warn($"The game's own {file.FileName} could not be put back: {ex.Message}");
                    }
                }
                RemoveEmptyFolders(file, file.DeployedPath!, ProjectFolder);
            }
            file.DeployedPath = null;
        }
        mod.Enabled = false;
    }

    /// <summary>
    /// Removes the folders a mod's file brought along, once they are empty. A game file's folders belong to the game,
    /// and stay. Only the folders below a folder where mods keep loose files go: they are the mod's, and a script loader
    /// may stop at an empty folder there.
    /// </summary>
    private static void RemoveEmptyFolders(ModFileRecord file, string deployedPath, string? projectFolder)
    {
        var name = !file.IsGameFile ? file.FileName
            : file.Target == DeployTarget.InstallRoot ? PackageScanner.BelowModFolder(file.FileName, projectFolder)
            : null;
        if (name is not null)
            RemoveEmptyFolders(name, deployedPath);
    }

    /// <summary>
    /// Removes the folders a file brought along, such as "Mod\Scripts" and "Mod" for
    /// "Mod/Scripts/main.lua", once they are empty. The target folder itself stays.
    /// </summary>
    /// <param name="name">The file's path below the target folder, with '/'.</param>
    /// <param name="deployedPath">Where the file was deployed.</param>
    private static void RemoveEmptyFolders(string name, string deployedPath)
    {
        var depth = name.Count(c => c == '/');
        var folder = Path.GetDirectoryName(deployedPath);
        for (var i = 0; i < depth && folder is not null; i++, folder = Path.GetDirectoryName(folder))
        {
            try
            {
                if (!Directory.Exists(folder) || Directory.EnumerateFileSystemEntries(folder).Any())
                    return;
                Directory.Delete(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Folder '{folder}' could not be removed: {ex.Message}");
                return;
            }
        }
    }

    /// <summary>Mods Menu's start file, below the project folder. A mod for it has none.</summary>
    private const string ModsMenuStart = "Content/JavaScript/PBZModsMenu/bootstrap.js";

    /// <summary>A file only PBZModLoader's own download has, below the project folder. Left in the game, it says nothing.</summary>
    private const string ModLoaderLicenses = "PBZModLoader/LICENSES.txt";

    private const string ModLoaderName = "pbzmodloader";

    /// <summary>The start file of a PBZModLoader before 2.0, below the project folder. Its DLL names this path, in UTF-16.</summary>
    private const string OldModLoaderStart = "Content/JavaScript/PBZModLoader/bootstrap.js";

    private static readonly byte[] OldModLoaderMark = System.Text.Encoding.Unicode.GetBytes(OldModLoaderStart);

    /// <summary>What a PBZModLoader before 2.0 keeps below the project folder, as the loader's own manifest lists it. The new one needs none of it.</summary>
    private static readonly string[] OldModLoaderPlaces =
        ["Content/JavaScript/PBZModLoader", "PBZModLoader/compiler", "PBZModLoader/evidence", "PBZModLoader/TOOLCHAIN-LICENSE.txt"];

    private string? ProjectFolder => Game.ProjectFolder ?? Game.Profile.ProjectFolder;

    private bool IsInProject(string path) =>
        (Game.ProjectFolder ?? Game.Profile.ProjectFolder) is { Length: > 0 } project && File.Exists(Path.Combine(Game.RootPath, project, path));

    /// <summary>Whether these parts of a download are Mods Menu itself, and not a mod for it.</summary>
    public static bool IsModsMenu(IEnumerable<PackageGroup> parts) =>
        parts.Any(p => p.RelativePaths.Any(path => path.EndsWith("/" + ModsMenuStart, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Whether a download is PBZModLoader itself, and not a mod for it.</summary>
    public static bool IsModLoader(InstallPreview preview, string? catalogId) =>
        catalogId?.Contains(ModLoaderName, StringComparison.OrdinalIgnoreCase) == true
        || preview.IgnoredFiles.Any(f => f.EndsWith("/" + ModLoaderLicenses, StringComparison.OrdinalIgnoreCase));

    /// <summary>Mods Menu is in the list, even when turned off, or in the game by hand.</summary>
    public bool IsModsMenuInstalled
    {
        get
        {
            lock (gate)
                return mods.Any(m => m.Files.Any(f => f.FileName.EndsWith("/" + ModsMenuStart, StringComparison.OrdinalIgnoreCase))) || IsInProject(ModsMenuStart);
        }
    }

    /// <summary>PBZModLoader is in the list, even when turned off, or its DLL is in the game by hand. A mod for it has no DLL.</summary>
    public bool IsModLoaderInstalled
    {
        get
        {
            lock (gate)
                return mods.Any(IsModLoaderRecord) || HasModLoaderEntry || IsInProject(OldModLoaderStart);
        }
    }

    private static bool IsModLoaderRecord(ModRecord mod) =>
        mod.Files.Any(f => f.FileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        && (mod.CatalogId?.Contains(ModLoaderName, StringComparison.OrdinalIgnoreCase) == true
            || mod.SourceFileName?.Contains(ModLoaderName, StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>The first versions of the two script loaders that run together.</summary>
    private static readonly Version MenuWithLoader = new(0, 9, 0);

    private static readonly Version LoaderWithMenu = new(2, 1, 0);

    /// <summary>Mods Menu's entry DLL, which PBZModLoader can have too, as one of its other entries.</summary>
    private const string ModsMenuEntry = "hid.dll";

    /// <summary>The version of Mods Menu in the list, as the player or its download named it; null when not known.</summary>
    private string? InstalledModsMenuVersion
    {
        get
        {
            lock (gate)
                return mods.FirstOrDefault(m => m.Files.Any(f => f.FileName.EndsWith("/" + ModsMenuStart, StringComparison.OrdinalIgnoreCase)))?.Version;
        }
    }

    private string? InstalledModLoaderVersion
    {
        get
        {
            lock (gate)
                return mods.FirstOrDefault(IsModLoaderRecord)?.Version;
        }
    }

    /// <summary>Both script loaders are in the game, and a known version of one of them is too old to run beside the other.</summary>
    public bool HasOldScriptLoader =>
        IsModsMenuInstalled && IsModLoaderInstalled
        && (IsOlder(InstalledModsMenuVersion, MenuWithLoader) || IsOlder(InstalledModLoaderVersion, LoaderWithMenu));

    /// <summary>Whether a version is known and older than the one named, such as "0.8.5-beta" than 0.9.0.</summary>
    internal static bool IsOlder(string? version, Version than)
    {
        var match = version is null ? null : System.Text.RegularExpressions.Regex.Match(version.Trim().TrimStart('v', 'V'), @"^\d+(\.\d+){0,3}");
        if (match is not { Success: true })
            return false;
        // "0.9" is 0.9.0: a part that is not written counts as 0.
        var parts = match.Value.Split('.').Concat(["0", "0"]).Take(Math.Max(3, match.Value.Split('.').Length));
        return Version.TryParse(string.Join('.', parts), out var parsed) && parsed < than;
    }

    /// <summary>The names PBZModLoader's entry DLL can have, next to the game's exe.</summary>
    private static readonly string[] ModLoaderEntries = ["version.dll", "hid.dll", "winmm.dll", "dsound.dll", "dxgi.dll"];

    /// <summary>The largest file looked into: the loader's DLL is far smaller.</summary>
    private const long ModLoaderEntryLimit = 64 * 1024 * 1024;

    /// <summary>
    /// PBZModLoader's DLL is next to the exe, under one of its names. Only the DLL tells: its log and its mods folder stay
    /// when it is removed, and other tools use the same names. The DLL is read as data, for the text its log lines start
    /// with or, before 2.0, the path of its start file.
    /// </summary>
    private bool HasModLoaderEntry => ModLoaderEntries.Any(name => IsModLoaderEntry(Path.Combine(Game.BinariesPath, name)));

    private static bool IsModLoaderEntry(string place)
    {
        try
        {
            var file = new FileInfo(place);
            if (!file.Exists || file.Length > ModLoaderEntryLimit)
                return false;
            var content = File.ReadAllBytes(place).AsSpan();
            return content.IndexOf("[PBZMOD]"u8) >= 0 || content.IndexOf(OldModLoaderMark) >= 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"'{place}' could not be read: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// What a PBZModLoader installed by hand keeps in the game: its entry DLL, which the manager's copy must replace as
    /// only one may be in the game, and the old loader's other files. Null with none of them, or when they are in the mod
    /// list, as the list then must take them out.
    /// </summary>
    public OldModLoader? FindOldModLoader()
    {
        if ((Game.ProjectFolder ?? Game.Profile.ProjectFolder) is not { Length: > 0 } project)
            return null;
        lock (gate)
        {
            var old = OldModLoaderPlaces.Select(place => Path.Combine(Game.RootPath, project, Local(place)))
                .Where(place => File.Exists(place) || Directory.Exists(place))
                .ToList();
            var listed = mods.SelectMany(m => m.Files).Select(f => f.DeployedPath).OfType<string>().ToList();
            if (old.Any(place => listed.Any(file => string.Equals(file, place, StringComparison.OrdinalIgnoreCase)
                    || file.StartsWith(place + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))))
                return null;
            var entries = ModLoaderEntries.Select(name => Path.Combine(Game.BinariesPath, name))
                .Where(place => IsModLoaderEntry(place) && !listed.Contains(place, StringComparer.OrdinalIgnoreCase));
            var places = entries.Concat(old).Select(place => Path.GetRelativePath(Game.RootPath, place)).ToList();
            if (places.Count == 0)
                return null;
            var folder = Path.Combine(Game.RootPath, project, "PBZModLoader", "mods");
            var sourceOnly = !Directory.Exists(folder) ? []
                : Directory.EnumerateDirectories(folder)
                    .Where(mod => File.Exists(Path.Combine(mod, "main.ts")) && !File.Exists(Path.Combine(mod, "main.js")))
                    .Select(mod => Path.GetFileName(mod))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            return new OldModLoader(places, sourceOnly);
        }
    }

    /// <summary>
    /// Moves those files out of the game into a backup folder, all or none, after the player said so. They are the only
    /// files the manager moves that it did not place: mods and mods.ini stay.
    /// </summary>
    /// <returns>The backup folder.</returns>
    public string RemoveOldModLoader(OldModLoader old)
    {
        lock (gate)
        {
            EnsureCanChange();
            var backup = Path.Combine(paths.BackupDir(Game.Profile.Id), $"{DateTime.Now:yyyyMMdd-HHmmss} PBZModLoader");
            var moved = new List<(string From, string To)>();
            try
            {
                foreach (var place in old.Places.Select(relative => Path.Combine(Game.RootPath, relative)))
                {
                    var files = Directory.Exists(place) ? Directory.EnumerateFiles(place, "*", SearchOption.AllDirectories).ToList() : [place];
                    foreach (var file in files)
                    {
                        var target = Path.Combine(backup, Path.GetRelativePath(Game.RootPath, file));
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Move(file, target);
                        moved.Add((file, target));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                foreach (var (from, to) in Enumerable.Reverse(moved))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(from)!);
                    File.Move(to, from);
                }
                FileSystem.TryDeleteDirectory(backup);
                throw new ModException($"The old files of PBZModLoader could not be moved, so nothing was changed. A file may be in use. ({ex.Message})");
            }
            foreach (var place in old.Places.Select(relative => Path.Combine(Game.RootPath, relative)).Where(Directory.Exists))
                FileSystem.TryDeleteDirectory(place);
            Log.Info($"Moved {moved.Count} file(s) of a PBZModLoader installed by hand to '{backup}'.");
            return backup;
        }
    }

    /// <summary>
    /// The part of a download's choice to tick at first: the one in the game already, or a script mod's copy for the
    /// script loader that is installed, or with neither for the one the app recommends, so the review says what to do.
    /// Null when the player must decide.
    /// </summary>
    public PackageGroup? PreferredChoice(InstallPreview preview)
    {
        if (preview.Packages.FirstOrDefault(p => p.IsAlternate && IsInstalled(p)) is { } made)
            return made;
        // With both loaders in the game, the copy for PBZModLoader: only one copy may be in the game.
        var (menu, loader) = (IsModsMenuInstalled, IsModLoaderInstalled);
        return preview.Packages.FirstOrDefault(p => p.IsAlternate && p.Variant == (menu && !loader ? PackageScanner.ForModsMenu : PackageScanner.ForModLoader));
    }

    /// <summary>Whether a mod in the list has this part's files: a choice of a download that the player made before.</summary>
    public bool IsInstalled(PackageGroup part)
    {
        lock (gate)
            return mods.Any(m => part.RelativePaths.Any(path => m.Files.Any(f => IsSameFile(f, part.Target, path))));
    }

    /// <summary>A file's path below its target folder, when the files that go to ~mods take a folder of their own there.</summary>
    public static string InFolder(string? ownFolder, DeployTarget target, string path) =>
        ownFolder is not null && target == DeployTarget.Mods ? $"{ownFolder}/{path}" : path;

    /// <summary>
    /// The installed mods with a file of the same name as one that these parts place in ~mods: a new version of one of
    /// them, or another mod whose author chose the same name, such as another style of an outfit. Only the player can
    /// tell, so a download by hand asks. A download from the catalog knows which mod it updates.
    /// </summary>
    public IReadOnlyList<SameNameMod> SameNameMods(IReadOnlyCollection<PackageGroup> selected, string? catalogId = null, string? siteId = null)
    {
        var names = selected.Where(p => p.Target == DeployTarget.Mods).SelectMany(p => p.RelativePaths).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (catalogId is not null || (names.Count == 0 && siteId is null))
            return [];
        lock (gate)
        {
            var found = new List<SameNameMod>();
            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mod in mods)
            {
                var file = mod.Files.FirstOrDefault(f => f.Target == DeployTarget.Mods && names.Contains(Path.GetFileName(f.FileName)));
                if (file is null)
                    continue;
                var slash = file.FileName.LastIndexOf('/');
                var folder = slash < 0 ? null : file.FileName[..slash];
                found.Add(new SameNameMod(mod, folder, IsSameMod(mod, selected.SelectMany(p => p.RelativePaths.Select(path => (p.Target, InFolder(folder, p.Target, path)))))));
                taken.Add(mod.Id);
            }
            // The same Nexus mod number means the same mod, even when a pak became a script and the file names
            // changed: the download is that mod's update. One is enough, and the one whose files the download
            // already replaces is the better target.
            if (siteId is not null)
            {
                var update = mods.Where(m => ModNaming.SiteModId(m.SourceFileName) == siteId && !taken.Contains(m.Id))
                    .OrderByDescending(m => selected.Any(p => p.RelativePaths.Any(path => m.Files.Any(f => IsSameFile(f, p.Target, path)))))
                    .FirstOrDefault();
                if (update is not null)
                    found.Add(new SameNameMod(update, null, true));
            }
            return found;
        }
    }

    /// <summary>A folder below ~mods for a mod kept beside others with the same file names: its name, with a number when that is taken.</summary>
    public string NewModFolder(string name)
    {
        var safe = FileSystem.SafeName(name, "Mod");
        var place = safe;
        lock (gate)
        {
            for (var number = 2;
                 Directory.Exists(Path.Combine(Game.ModsPath, place))
                 || mods.Any(m => m.Files.Any(f => f.Target == DeployTarget.Mods && f.FileName.StartsWith(place + "/", StringComparison.OrdinalIgnoreCase)));
                 number++)
                place = $"{safe} {number}";
        }
        return place;
    }

    /// <summary>
    /// Whether a mod is an older copy of a download, and not another mod that shares a file with it, as two loaders
    /// share version.dll: at least half of its files are in the download. An update must not remove another mod.
    /// </summary>
    private static bool IsSameMod(ModRecord mod, IEnumerable<(DeployTarget Target, string Path)> entries)
    {
        var list = entries.ToList();
        return mod.Files.Count(f => list.Any(e => IsSameFile(f, e.Target, e.Path))) * 2 >= mod.Files.Count;
    }

    /// <summary>The variant the selection installs, such as "x2"; null when the download offered no variants.</summary>
    private static string? VariantOf(IEnumerable<PackageGroup> selected)
    {
        var variants = selected.Select(p => p.Variant).OfType<string>().ToList();
        return variants.Count == 0 ? null : string.Join(", ", variants);
    }

    private static bool IsSameFile(ModFileRecord file, DeployTarget target, string relativePath) =>
        file.Target == target && string.Equals(file.FileName, relativePath, StringComparison.OrdinalIgnoreCase);

    private static string Local(string relativePath) => relativePath.Replace('/', Path.DirectorySeparatorChar);

    /// <summary>The folder's name for a message, such as "~mods": the same in every language.</summary>
    private string TargetName(DeployTarget target) => target switch
    {
        DeployTarget.Ue4ssMods => Path.Combine("ue4ss", "Mods"),
        DeployTarget.InstallRoot => "the game folder",
        _ => Path.GetFileName(Path.TrimEndingDirectorySeparator(TargetFolder(target))),
    };

    private static bool Matches(string path, ModFileRecord file) =>
        new FileInfo(path).Length == file.Size
        && string.Equals(FileSystem.Sha256(path), file.Sha256, StringComparison.OrdinalIgnoreCase);

    private static bool SameContent(string path, string other) =>
        new FileInfo(path).Length == new FileInfo(other).Length
        && string.Equals(FileSystem.Sha256(path), FileSystem.Sha256(other), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A shared file that stays in the game when these mods leave it: another enabled mod uses it too, or it was
    /// there before (<see cref="ModFileRecord.Borrowed"/>).
    /// </summary>
    private bool Keeps(IReadOnlyCollection<ModRecord> leaving, ModFileRecord file) =>
        file.IsShared && file.DeployedPath is { } path
        && (file.Borrowed || OtherUsers(leaving, path).Any());

    /// <summary>The files of other enabled mods deployed at this place: a shared library they bring too.</summary>
    private IEnumerable<ModFileRecord> OtherUsers(IReadOnlyCollection<ModRecord> except, string deployedPath) => mods
        .Where(m => m.Enabled && !except.Contains(m))
        .SelectMany(m => m.Files)
        .Where(f => f.DeployedPath is not null && string.Equals(f.DeployedPath, deployedPath, StringComparison.OrdinalIgnoreCase));

    private void Save() => JsonFile.Write(StateFile, new ModState { Mods = mods, OffForTest = offForTest, Sets = sets });
}
