using System.Globalization;
using UEManager.Core.Mods;

namespace UEManager.Cli;

/// <summary>
/// Builds the read-only install plan that `plan` prints and `install` follows: the same Inspect and Check the app's
/// install review runs, with the parts it would tick first. Nothing here changes the game.
/// </summary>
internal sealed class PlannedInstall : IDisposable
{
    private PlannedInstall(ModManager manager, InstallPreview preview, IReadOnlyList<PackageGroup> selected, InstallCheck check,
        bool keepBoth, string? ownFolder, bool shareFiles, PlanPayload plan)
    {
        Manager = manager;
        Preview = preview;
        Selected = selected;
        Check = check;
        KeepBoth = keepBoth;
        OwnFolder = ownFolder;
        ShareFiles = shareFiles;
        Plan = plan;
    }

    /// <summary>The manager that read and checked the download; <see cref="InstallCommand"/> installs with it.</summary>
    public ModManager Manager { get; }

    public InstallPreview Preview { get; }

    public IReadOnlyList<PackageGroup> Selected { get; }

    public InstallCheck Check { get; }

    /// <summary>The download is installed as another mod beside the installed one with its file names or mod page.</summary>
    public bool KeepBoth { get; }

    /// <summary>--share-files: kept beside the installed mods that have some of its files, sharing those.</summary>
    public bool ShareFiles { get; }

    /// <summary>
    /// The folder below ~mods for the download's files that go there: a kept mod's folder of its own, or the folder of
    /// the mod it updates; null puts them directly in ~mods. It goes to <see cref="ModManager.Install"/> as
    /// <c>ownFolder</c>.
    /// </summary>
    public string? OwnFolder { get; }

    public PlanPayload Plan { get; }

    public void Dispose() => Preview.Dispose();

    /// <exception cref="CliError">The selection is invalid, such as an unknown --part.</exception>
    /// <exception cref="ModException">The download cannot be read.</exception>
    public static PlannedInstall Build(CliContext context, CliArguments arguments, string source)
    {
        if (arguments.AllParts && arguments.Parts.Count > 0)
            throw new CliError("invalid-arguments", "Options '--all-parts' and '--part' cannot be used together.",
                details: ["Use --all-parts for every part, or --part for the parts you name."]);
        var manager = context.OpenManager();
        var preview = manager.Inspect(source, StagingFolder(arguments.Staging, manager.Game.RootPath));
        try
        {
            // The parts the app's install review ticks first, by the same rules in Core; --part takes exactly those named.
            var selected = arguments.Parts.Count > 0
                ? Parts(preview, arguments.Parts).ToList()
                : arguments.AllParts ? preview.Packages.ToList() : manager.FirstSelection(preview).ToList();
            if (arguments.ShareFiles && arguments.KeepBoth)
                throw new CliError("invalid-arguments", "Options '--share-files' and '--keep-both' cannot be used together.",
                    details: ["--share-files keeps the installed mod and shares the files that are the same in both; --keep-both puts the download into a folder of its own."]);
            var check = manager.Check(preview, selected);
            // An update keeps the variant that is installed now, as the review does. A part the player named stays.
            if (arguments.Parts.Count == 0 && !arguments.AllParts && KeepInstalledVariant(preview, selected, check))
                check = manager.Check(preview, selected);
            // The player says the mods work together, as the review's box: keep both, sharing the files that are the
            // same in both. No folder of its own, and no update.
            if (arguments.ShareFiles)
            {
                check = manager.Check(preview, selected, shareFiles: true);
                return new PlannedInstall(manager, preview, selected, check, false, null, true,
                    BuildPlan(context, manager, preview, selected, check, arguments.AllParts, false, null, true));
            }
            // Installed mods with the download's file names or mod page: the review's first choice, by the same rule in
            // Core, so a file from the same mod page is kept beside the mod and does not remove it. --keep-both keeps
            // the installed mod in any case: the download goes into a folder of its own below ~mods, as the review's
            // "Keep both" does (see InstallDialogViewModel.OwnFolder).
            var places = manager.PlacesFor(selected, null, ModNaming.SiteModId(preview.SourceFileName));
            var keepBoth = places.Any && (arguments.KeepBoth || places.SamePage);
            var ownFolder = keepBoth ? manager.NewModFolder(preview.SuggestedName) : places.FirstUpdate?.Folder;
            if (ownFolder is not null)
                check = manager.Check(preview, selected, null, ownFolder);
            // The download still takes the place of an installed mod: another choice of the same mod, or files outside
            // ~mods that a folder of its own cannot keep apart. Only one can be in the game.
            if (arguments.KeepBoth && check.Replaces is { } replaced)
                throw new CliError("keep-both-impossible",
                    $"Option '--keep-both' cannot keep \"{replaced.Name}\": this download takes its place, and the two cannot be in the game together.",
                    details: ["Install without --keep-both to update it, or uninstall it first."]);
            return new PlannedInstall(manager, preview, selected, check, keepBoth, ownFolder, false,
                BuildPlan(context, manager, preview, selected, check, arguments.AllParts, keepBoth, ownFolder, false));
        }
        catch
        {
            preview.Dispose();
            throw;
        }
    }

    /// <summary>The --staging folder as a full path; null keeps the data folder's staging folder. ue4ss install and update use it too.</summary>
    /// <exception cref="CliError">The option names no folder, or one inside the game.</exception>
    internal static string? StagingFolder(string? given, string gameRoot)
    {
        if (given is null)
            return null;
        if (string.IsNullOrWhiteSpace(given))
            throw new CliError("invalid-arguments", "Option '--staging' needs a folder.");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(given));
        if (File.Exists(full))
            throw new CliError("invalid-arguments", $"Option '--staging' needs a folder, but '{full}' is a file.");
        // The game would load an unpacked pak there, and the check would take it for a copy the player put in by hand.
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot));
        if (full.Equals(root, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new CliError("invalid-arguments", $"Option '--staging' needs a folder outside the game, but '{full}' is inside {root}.");
        return full;
    }

    /// <summary>
    /// The parts named by --part, by their index in the plan (1-based), their folder/name, or else their name or
    /// variant, such as "x2".
    /// </summary>
    /// <exception cref="CliError">A name matches no part or several; the details list the available parts.</exception>
    private static IReadOnlyList<PackageGroup> Parts(InstallPreview preview, IReadOnlyList<string> requested)
    {
        var selected = new List<PackageGroup>();
        foreach (var request in requested)
        {
            var spec = request.Trim();
            if (spec.Length == 0)
                throw new CliError("invalid-arguments", "Option '--part' needs a part's index or name.",
                    details: AvailableParts(preview));
            var isIndex = int.TryParse(spec, NumberStyles.None, CultureInfo.InvariantCulture, out var index);
            List<PackageGroup> matches = isIndex
                ? index >= 1 && index <= preview.Packages.Count ? [preview.Packages[index - 1]] : []
                : [.. preview.Packages.Where(p => p.Label.Equals(spec, StringComparison.OrdinalIgnoreCase))];
            // A folder/name names one part; a name or a variant may be shared, such as Mod_P in two variant folders.
            if (matches.Count == 0 && !isIndex)
                matches = [.. preview.Packages.Where(p => p.Name.Equals(spec, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.Variant, spec, StringComparison.OrdinalIgnoreCase))];
            if (matches.Count == 0)
                throw new CliError("invalid-arguments", $"No part matches '{spec}'.", details: AvailableParts(preview));
            if (matches.Count > 1)
                throw new CliError("invalid-arguments", $"More than one part matches '{spec}'. Name it by its index or its folder/name.",
                    details: AvailableParts(preview));
            var part = matches[0];
            if (!selected.Contains(part))
                selected.Add(part);
        }
        return selected;
    }

    /// <summary>The parts a --part name may mean, for the error message: their index in the plan and where they go.</summary>
    private static string[] AvailableParts(InstallPreview preview) =>
    [
        "Available parts:",
        .. preview.Packages.Select((part, index) => $"  {index + 1}: {part.Label} ({part.Target})"),
    ];

    /// <summary>
    /// Selects the variant of the mod the download updates (see <see cref="ModManager.InstalledVariant"/>), in place of
    /// the other variants of its choice, so an update keeps the variant the player chose.
    /// </summary>
    private static bool KeepInstalledVariant(InstallPreview preview, List<PackageGroup> selected, InstallCheck check)
    {
        if (ModManager.InstalledVariant(preview, check) is not { } same || selected.Contains(same))
            return false;
        selected.RemoveAll(part => part != same && part.VariantOf == same.VariantOf);
        selected.Add(same);
        return true;
    }

    private static PlanPayload BuildPlan(CliContext context, ModManager manager, InstallPreview preview,
        IReadOnlyList<PackageGroup> selected, InstallCheck check, bool allParts, bool keepBoth, string? ownFolder, bool shareFiles)
    {
        var parts = preview.Packages.Select((part, index) => new PlanPartDto(index + 1, part.Name, part.Folder, part.Label,
            part.Kind.ToString(), part.Target.ToString(), part.TypeLabel, part.Size, part.Files.Count, selected.Contains(part),
            part.IsUncertain, part.IsAlternate, part.Variant, part.VariantOf, part.NeedsLoader,
            [.. part.Problems.Select(Output.Text)], [.. part.Warnings.Select(Output.Text)], part.RelativePaths)).ToList();

        var entries = new List<PlanEntryDto>();
        foreach (var part in selected)
        {
            for (var i = 0; i < part.Files.Count; i++)
            {
                var target = part.Target;
                // Files for ~mods go into the kept mod's own folder or the updated mod's folder, as they will be installed.
                var relative = ModManager.InFolder(ownFolder, target, part.RelativePaths[i]);
                var path = Path.Combine(manager.TargetFolder(target), relative);
                entries.Add(new PlanEntryDto(target.ToString(), relative, path, File.Exists(path), Owner(manager, target, relative)));
            }
        }
        entries.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

        var existing = entries.Where(entry => entry.Exists).ToList();
        var gameFiles = existing.Where(entry => entry.Target is nameof(DeployTarget.InstallRoot) or nameof(DeployTarget.ExeFolder)).ToList();
        var replaces = check.Replaces is { } old
            ? new PlanReplacedDto(old.Id, old.Name, old.Version, old.Enabled, old.Variant)
            : null;

        return new PlanPayload(1, "plan", preview.Source, preview.SourceFileName, preview.SuggestedName, preview.SuggestedVersion,
            context.GameId, Mapping.Game(context.Profile, context.Game, checkRunning: false),
            new PlanSelectionDto(allParts, preview.Packages.Count, selected.Count,
                [.. selected.Select(part => part.Label)]),
            parts, entries, existing, gameFiles, replaces, keepBoth, ownFolder, check.EditedSettings ?? [],
            check.NeedsUe4ss, check.CanInstall,
            shareFiles, Sharing(check.ShareOffer), Sharing(check.Sharing),
            [.. check.Problems.Select(Output.Text)], [.. check.Warnings.Select(Output.Text)],
            new PlanIgnoredDto(preview.IgnoredFileCount, preview.LeftOutFiles.Count, preview.IgnoredFiles, preview.LeftOutFiles,
                preview.NestedArchiveCount, preview.ContainsUe4ssRuntime));
    }

    private static PlanSharingDto? Sharing(FileSharing? sharing) =>
        sharing is null ? null : new PlanSharingDto([.. sharing.Mods.Select(mod => mod.Name)], sharing.Files);

    /// <summary>The installed mod whose file is at this place, if any; null for the game's own file or none.</summary>
    private static string? Owner(ModManager manager, DeployTarget target, string relativePath) =>
        manager.Mods.FirstOrDefault(mod => mod.Files.Any(file => file.Target == target
            && file.FileName.Equals(relativePath, StringComparison.OrdinalIgnoreCase)))?.Name;
}
