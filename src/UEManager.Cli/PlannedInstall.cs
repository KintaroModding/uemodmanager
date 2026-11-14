using UEManager.Core.Mods;

namespace UEManager.Cli;

/// <summary>
/// Builds the read-only install plan that `plan` prints and `install` follows: the same Inspect and Check the app's
/// install review runs, with the parts it would tick first. Nothing here changes the game.
/// </summary>
internal sealed class PlannedInstall : IDisposable
{
    private PlannedInstall(ModManager manager, InstallPreview preview, IReadOnlyList<PackageGroup> selected, InstallCheck check,
        PlanPayload plan)
    {
        Manager = manager;
        Preview = preview;
        Selected = selected;
        Check = check;
        Plan = plan;
    }

    /// <summary>The manager that read and checked the download; <see cref="InstallCommand"/> installs with it.</summary>
    public ModManager Manager { get; }

    public InstallPreview Preview { get; }

    public IReadOnlyList<PackageGroup> Selected { get; }

    public InstallCheck Check { get; }

    public PlanPayload Plan { get; }

    public void Dispose() => Preview.Dispose();

    /// <exception cref="ModException">The download cannot be read.</exception>
    public static PlannedInstall Build(CliContext context, string source, bool allParts)
    {
        var manager = context.OpenManager();
        var preview = manager.Inspect(source);
        try
        {
            var selected = allParts ? preview.Packages.ToList() : DefaultSelection(manager, preview);
            var check = manager.Check(preview, selected);
            // The review keeps the variant that is installed now, as its card for that variant says "installed".
            if (!allParts && KeepInstalledVariant(preview, selected, check))
                check = manager.Check(preview, selected);
            return new PlannedInstall(manager, preview, selected, check, BuildPlan(context, manager, preview, selected, check, allParts));
        }
        catch
        {
            preview.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The parts the review ticks first: everything certain, one of each choice of alternates, and the first of the
    /// variants that would otherwise land in the same place. A part for the exe folder that does not look like a loader
    /// waits for the player, as it does in the app.
    /// </summary>
    private static List<PackageGroup> DefaultSelection(ModManager manager, InstallPreview preview)
    {
        var preferred = manager.PreferredChoice(preview);
        var selected = new List<PackageGroup>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in preview.Packages)
        {
            var places = part.RelativePaths.Select(path => $"{part.Target}|{path}").ToList();
            if (part.IsUncertain || (part.IsAlternate && part != preferred) || places.Any(taken.Contains))
                continue;
            selected.Add(part);
            taken.UnionWith(places);
        }
        foreach (var alternate in selected.Where(part => part.IsAlternate).ToList())
        {
            if (alternate.VariantOf is not { } group)
                continue;
            selected.RemoveAll(part => part != alternate && part.VariantOf == group);
        }
        return selected;
    }

    /// <summary>Selects the variant of the mod the download updates, so an update keeps the variant the player chose.</summary>
    private static bool KeepInstalledVariant(InstallPreview preview, List<PackageGroup> selected, InstallCheck check)
    {
        if (check.Replaces?.Variant is not { } installed)
            return false;
        var same = preview.Packages.FirstOrDefault(part => part.Variant is { } variant
            && variant.Equals(installed, StringComparison.OrdinalIgnoreCase) && !selected.Contains(part));
        if (same is null)
            return false;
        if (same.VariantOf is { } group)
            selected.RemoveAll(part => part != same && part.VariantOf == group);
        selected.Add(same);
        return true;
    }

    private static PlanPayload BuildPlan(CliContext context, ModManager manager, InstallPreview preview,
        IReadOnlyList<PackageGroup> selected, InstallCheck check, bool allParts)
    {
        var parts = preview.Packages.Select((part, index) => new PlanPartDto(index, part.Name, part.Folder, part.Label,
            part.Kind.ToString(), part.Target.ToString(), part.TypeLabel, part.Size, part.Files.Count, selected.Contains(part),
            part.IsUncertain, part.IsAlternate, part.Variant, part.VariantOf, part.NeedsLoader,
            [.. part.Problems.Select(Output.Text)], [.. part.Warnings.Select(Output.Text)], part.RelativePaths)).ToList();

        var entries = new List<PlanEntryDto>();
        foreach (var part in selected)
        {
            for (var i = 0; i < part.Files.Count; i++)
            {
                var target = part.Target;
                var relative = part.RelativePaths[i];
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
            parts, entries, existing, gameFiles, replaces, check.EditedSettings ?? [], check.NeedsUe4ss, check.CanInstall,
            [.. check.Problems.Select(Output.Text)], [.. check.Warnings.Select(Output.Text)],
            new PlanIgnoredDto(preview.IgnoredFileCount, preview.LeftOutFiles.Count, preview.IgnoredFiles, preview.LeftOutFiles,
                preview.NestedArchiveCount, preview.ContainsUe4ssRuntime));
    }

    /// <summary>The installed mod whose file is at this place, if any; null for the game's own file or none.</summary>
    private static string? Owner(ModManager manager, DeployTarget target, string relativePath) =>
        manager.Mods.FirstOrDefault(mod => mod.Files.Any(file => file.Target == target
            && file.FileName.Equals(relativePath, StringComparison.OrdinalIgnoreCase)))?.Name;
}
