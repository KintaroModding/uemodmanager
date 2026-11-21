using UEManager.Core.Mods;

namespace UEManager.Cli.Commands;

/// <summary>
/// `--cli install`: the plan, then the Core's own Install for the parts the plan selected. Without --yes it prints the
/// plan and exits 2, so a script can review the plan before it changes anything.
/// </summary>
internal static class InstallCommand
{
    public static int Run(CliArguments arguments)
    {
        arguments.Allow(["--json", "--yes", "--all-parts", "--keep-both", "--share-files"],
            ["--data-dir", "--game", "--game-root", "--staging", "--part"]);
        var source = arguments.Single("a download (a .zip, .7z, .rar, .pak, .utoc, .ucas file, or a folder)");
        var context = CliContext.Open(arguments, needInstall: true, write: arguments.Yes);
        using var planned = PlannedInstall.Build(context, arguments, source);
        var plan = planned.Plan;

        if (!arguments.Yes)
        {
            // "The same as plan, with exit code 2": the plan stays the one thing on stdout.
            Output.Json(plan);
            Console.Error.WriteLine($"{Program.ToolName}: nothing was installed. Run the same command with --yes to install "
                + $"{plan.Selection.SelectedCount} of {plan.Selection.PartCount} part(s).");
            return Program.ConfirmationRequired;
        }

        if (!plan.CanInstall)
        {
            throw new CliError("install-blocked", $"The download cannot be installed into {context.Profile.DisplayName}.",
                details: plan.Problems);
        }

        var manager = planned.Manager;
        ModRecord mod;
        try
        {
            // Kept beside an installed mod: no replace, and the plan's folder below ~mods.
            mod = manager.Install(planned.Preview, planned.Selected, plan.SuggestedName, plan.SuggestedVersion,
                replace: plan.Replaces is not null, ownFolder: planned.OwnFolder, shareFiles: planned.ShareFiles);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new CliError("install-failed", $"The install failed: {error.Message}");
        }

        var payload = new InstallPayload(1, "install", plan.Source, Mapping.Mod(mod), plan.Replaces, plan.KeepBoth, plan.OwnFolder,
            plan.Warnings);
        if (arguments.Json)
        {
            Output.Json(payload);
            return 0;
        }
        Output.Line($"Installed \"{mod.Name}\"{(mod.Version is null ? "" : $" ({mod.Version})")}: {mod.Files.Count} file(s), {mod.TypeLabel}.");
        if (plan.Replaces is { } replaced)
            Output.Line($"Replaced \"{replaced.Name}\"{(replaced.Version is null ? "" : $" ({replaced.Version})")}.");
        if (planned.KeepBoth)
            Output.Line(planned.Selected.Any(part => part.Target == DeployTarget.Mods)
                ? $"Kept the installed mod; the new one is in ~mods/{planned.OwnFolder}."
                : "Kept the installed mod; the new one is another mod beside it.");
        if (plan.SharesWith is { } sharing)
            Output.Line($"Shares {sharing.Files} file(s) with {string.Join(", ", sharing.Mods.Select(name => $"\"{name}\""))}.");
        foreach (var warning in plan.Warnings)
            Output.Line($"Warning: {warning}");
        return 0;
    }
}
