namespace UEManager.Cli.Commands;

/// <summary>`uemm plan`: Inspect and Check the download and print the plan as JSON. Nothing in the game is changed.</summary>
internal static class PlanCommand
{
    public static int Run(CliArguments arguments)
    {
        arguments.Allow(["--json", "--all-parts"], ["--data-dir", "--game", "--game-root"]);
        var source = arguments.Single("a download (a .zip, .7z, .rar, .pak, .utoc, .ucas file, or a folder)");
        var context = CliContext.Open(arguments, needInstall: true, write: false);
        using var planned = PlannedInstall.Build(context, source, arguments.AllParts);
        // The plan is JSON by design, so --json changes nothing here.
        Output.Json(planned.Plan);
        return 0;
    }
}
