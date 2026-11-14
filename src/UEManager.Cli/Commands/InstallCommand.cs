using UEManager.Core.Mods;

namespace UEManager.Cli.Commands;

/// <summary>
/// `uemm install`: the plan, then the Core's own Install for the parts the plan selected. Without --yes it prints the
/// plan and exits 2, so a script can review the plan before it changes anything.
/// </summary>
internal static class InstallCommand
{
    public static int Run(CliArguments arguments)
    {
        arguments.Allow(["--json", "--yes", "--all-parts"], ["--data-dir", "--game", "--game-root"]);
        var source = arguments.Single("a download (a .zip, .7z, .rar, .pak, .utoc, .ucas file, or a folder)");
        var context = CliContext.Open(arguments, needInstall: true, write: arguments.Yes);
        using var planned = PlannedInstall.Build(context, source, arguments.AllParts);
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
            mod = manager.Install(planned.Preview, planned.Selected, plan.SuggestedName, plan.SuggestedVersion,
                replace: plan.Replaces is not null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new CliError("install-failed", $"The install failed: {error.Message}");
        }

        var payload = new InstallPayload(1, "install", plan.Source, Mapping.Mod(mod), plan.Replaces, plan.Warnings);
        if (arguments.Json)
        {
            Output.Json(payload);
            return 0;
        }
        Output.Line($"Installed \"{mod.Name}\"{(mod.Version is null ? "" : $" ({mod.Version})")}: {mod.Files.Count} file(s), {mod.TypeLabel}.");
        if (plan.Replaces is { } replaced)
            Output.Line($"Replaced \"{replaced.Name}\"{(replaced.Version is null ? "" : $" ({replaced.Version})")}.");
        foreach (var warning in plan.Warnings)
            Output.Line($"Warning: {warning}");
        return 0;
    }
}
