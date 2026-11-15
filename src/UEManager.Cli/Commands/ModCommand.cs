using UEManager.Core.Mods;

namespace UEManager.Cli.Commands;

/// <summary>`--cli enable|disable|uninstall`: the Core's own operations, reached by the mod's name instead of the UI.</summary>
internal static class ModCommand
{
    public static int RunToggle(CliArguments arguments, bool enable)
    {
        arguments.Allow(["--json"], ["--data-dir", "--game", "--game-root"]);
        var query = arguments.Single("an installed mod's name or id");
        var context = CliContext.Open(arguments, needInstall: true, write: true);
        var manager = context.OpenManager();
        var mod = ModResolver.Resolve(manager.Mods, query);
        var before = mod.Enabled;
        if (before != enable)
            manager.SetEnabled(mod.Id, enable);

        var after = manager.Mods.First(record => record.Id == mod.Id);
        var deployed = after.Files.Count(file => file.DeployedPath is not null);
        var payload = new TogglePayload(1, enable ? "enable" : "disable", context.GameId, new ToggleModDto(mod.Id, mod.Name,
            before, enable, before != enable, deployed, ModManager.Integrity(after).ToString()));

        if (arguments.Json)
        {
            Output.Json(payload);
            return 0;
        }
        if (!payload.Mod.Changed)
            Output.Line($"\"{mod.Name}\" is already {(enable ? "enabled" : "disabled")}.");
        else if (enable)
            Output.Line($"Enabled \"{mod.Name}\": {deployed} file(s) in the game now.");
        else
            Output.Line($"Disabled \"{mod.Name}\": its files were taken out of the game.");
        return 0;
    }

    public static int RunUninstall(CliArguments arguments)
    {
        arguments.Allow(["--json", "--yes", "--remove-leftovers"], ["--data-dir", "--game", "--game-root"]);
        var query = arguments.Single("an installed mod's name or id");
        var context = CliContext.Open(arguments, needInstall: true, write: arguments.Yes);
        var manager = context.OpenManager();
        var mod = ModResolver.Resolve(manager.Mods, query);
        var view = new UninstallModDto(mod.Id, mod.Name, mod.Version, mod.Enabled, mod.Files.Count, mod.TotalSize,
            manager.LibraryFolderOf(mod), [.. mod.Files.Select(Mapping.File)]);
        var leftovers = Leftovers(manager.LeftoversOf(mod.Id));

        if (!arguments.Yes)
        {
            var preview = new UninstallPayload(1, "uninstall", context.GameId, false, null, leftovers, null, manager.Mods.Count, view);
            if (arguments.Json)
                Output.Json(preview);
            else
                WriteUninstallPreview(preview, arguments.RemoveLeftovers);
            Console.Error.WriteLine($"{Program.ToolName}: nothing was uninstalled. Run the same command with --yes to uninstall it.");
            return Program.ConfirmationRequired;
        }

        var result = manager.Uninstall(mod.Id, arguments.RemoveLeftovers);
        var payload = new UninstallPayload(1, "uninstall", context.GameId, true, result.SettingsBackup, Leftovers(result.Leftovers),
            result.LeftoverBackup, manager.Mods.Count, view);
        if (arguments.Json)
        {
            Output.Json(payload);
            return 0;
        }
        Output.Line($"Uninstalled \"{mod.Name}\": {view.FileCount} file(s) left the game and the library.");
        Output.Line(result.SettingsBackup is null
            ? "No changed settings needed a backup."
            : $"The player's changed settings were backed up to {result.SettingsBackup}.");
        WriteLeftovers(payload);
        return 0;
    }

    private static List<LeftoverDto> Leftovers(IReadOnlyList<ModLeftover> leftovers) =>
        [.. leftovers.Select(leftover => new LeftoverDto(leftover.Path, leftover.RelativePath, leftover.Size, leftover.IsLog))];

    private static void WriteLeftovers(UninstallPayload payload)
    {
        if (payload.Leftovers.Count == 0)
            return;
        var files = payload.Leftovers.Sum(leftover => leftover.SizeBytes);
        var logs = payload.Leftovers.Count(leftover => leftover.IsLog);
        if (payload.LeftoverBackup is { } backup)
        {
            Output.Line($"Moved the {payload.Leftovers.Count} file(s) it left behind ({Output.Size(files)}, {logs} log(s)) to {backup}.");
            return;
        }
        Output.Line($"Kept the {payload.Leftovers.Count} file(s) it left behind ({Output.Size(files)}, {logs} log(s)): "
            + $"{payload.Leftovers[0].RelativePath}" + (payload.Leftovers.Count > 1 ? " and more" : "") + ".");
        Output.Line("Run the same command with --remove-leftovers to move them into the backups.");
    }

    private static void WriteUninstallPreview(UninstallPayload payload, bool removeLeftovers)
    {
        var mod = payload.Mod;
        Output.Line($"Would uninstall \"{mod.Name}\"{(mod.Version is null ? "" : $" ({mod.Version})")}: "
            + $"{mod.FileCount} file(s), {Output.Size(mod.TotalSize)}, {(mod.Enabled ? "enabled" : "disabled")}.");
        foreach (var target in mod.Files.Select(file => file.Target).Distinct(StringComparer.Ordinal))
            Output.Line($"  {target}: {mod.Files.Count(file => file.Target == target)} file(s)");
        Output.Line("Settings files the player changed are backed up first, as the Core does.");
        if (payload.Leftovers.Count == 0)
            return;
        var files = payload.Leftovers.Sum(leftover => leftover.SizeBytes);
        Output.Line($"It also left {payload.Leftovers.Count} file(s) of its own in the game ({Output.Size(files)}):");
        foreach (var leftover in payload.Leftovers.Take(5))
            Output.Line($"  {leftover.RelativePath}" + (leftover.IsLog ? "  (log)" : ""));
        if (payload.Leftovers.Count > 5)
            Output.Line($"  and {payload.Leftovers.Count - 5} more");
        Output.Line(removeLeftovers
            ? "They will be moved into the manager's backups."
            : "They stay in the game; --remove-leftovers moves them into the manager's backups.");
    }
}