namespace UEManager.Cli;

/// <summary>`uemm list`: every installed mod with its version, state, files, targets and source.</summary>
internal static class ListCommand
{
    public static int Run(CliArguments arguments)
    {
        arguments.Allow(["--json"], ["--data-dir", "--game", "--game-root"]);
        arguments.NoPositionals();
        var context = CliContext.Open(arguments, needInstall: false, write: false);
        var state = ModStateFile.Read(context);
        var mods = state.Mods.Select(Mapping.Mod).ToList();
        var payload = new ListPayload(1, "list", context.DataDir, context.GameId, mods.Count, mods);

        if (arguments.Json)
        {
            Output.Json(payload);
            return 0;
        }

        Output.Line($"{mods.Count} mod(s) in {context.Profile.DisplayName} ({context.GameId})");
        if (mods.Count > 0)
        {
            var width = mods.Max(mod => mod.Name.Length);
            foreach (var mod in mods)
            {
                var stateText = mod.Enabled ? "on " : "off";
                Output.Line($"[{stateText}] {mod.Name.PadRight(width)}  {mod.Version ?? "no version",-10}  {mod.FileCount} file(s), "
                    + $"{Output.Size(mod.TotalSize),-9}  {mod.Type,-12}  {mod.Integrity,-13}  {mod.SourceFileName ?? "(no download name)"}");
            }
        }
        return 0;
    }
}
