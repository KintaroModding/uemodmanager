using System.Diagnostics;
using System.Globalization;
using UEManager.Core.Mods;

namespace UEManager.Cli;

/// <summary>`uemm status`: the app version, the game, the data folder and how many mods are installed.</summary>
internal static class StatusCommand
{
    public static int Run(CliArguments arguments)
    {
        arguments.Allow(["--json"], ["--data-dir", "--game", "--game-root"]);
        arguments.NoPositionals();
        var context = CliContext.Open(arguments, needInstall: false, write: false);
        var warnings = new List<string>();

        var stateFile = ModStateFile.PathOf(context);
        var readable = true;
        var total = 0;
        var enabled = 0;
        try
        {
            var state = ModStateFile.Read(context);
            total = state.Mods.Count;
            enabled = state.Mods.Count(mod => mod.Enabled);
        }
        catch (CliError error)
        {
            readable = false;
            warnings.Add(error.Message);
        }
        var mods = new ModsCountDto(readable, total, enabled, total - enabled, stateFile, File.Exists(stateFile),
            context.Paths.LibraryDir(context.GameId));

        var app = FindApp(context);
        var (recordFiles, records) = UpdatesReader.Read(context);
        var payload = new StatusPayload(1, "status", new ToolInfo(Program.ToolName, Program.ToolVersion, Program.Commit),
            context.DataDir, context.Paths.SettingsFile, File.Exists(context.Paths.SettingsFile), app,
            Mapping.Game(context.Profile, context.Game, checkRunning: true), mods,
            new UpdateRecordsInfo(recordFiles, records.Count, UpdatesReader.LastChecked(records)), warnings);

        if (arguments.Json)
        {
            Output.Json(payload);
            return 0;
        }

        Output.Line($"{Program.ToolName} {Program.ToolVersion}");
        Output.Line($"Data folder: {payload.DataDir}");
        Output.Line(app is null ? "App: not found next to the data folder" : $"App: {app.Executable} ({app.Version ?? "unknown version"})");
        var game = payload.Game;
        if (!game.Found)
        {
            Output.Line($"Game: {game.DisplayName} ({game.Id}), install not found");
            Output.Line(@"  Point the app at the game once, or pass --game-root <folder>.");
        }
        else
        {
            Output.Line($"Game: {game.DisplayName} ({game.Id}) at {game.RootPath} [{game.Store}]");
            Output.Line($"  Paks: {game.PaksPath}");
            Output.Line($"  Mods: {game.ModsPath}");
            Output.Line($"  UE4SS: {(game.Ue4ss!.Installed ? $"installed {game.Ue4ss.Version ?? "(version unknown)"}" : "not installed")}, mods at {game.Ue4ss.ModsPath}");
            Output.Line($"  Game running: {(game.Running == true ? "yes" : "no")}");
        }
        Output.Line($"Mods: {mods.Total} installed ({mods.Enabled} enabled, {mods.Disabled} disabled)");
        Output.Line(payload.UpdateRecords.Count == 0
            ? "Update records: none"
            : $"Update records: {payload.UpdateRecords.Count}, checked {Output.Time(payload.UpdateRecords.CheckedAt)}");
        foreach (var warning in warnings)
            Output.Line($"Warning: {warning}");
        return 0;
    }

    /// <summary>The app executable beside the data folder, or in this program's folder.</summary>
    private static AppDto? FindApp(CliContext context)
    {
        var dataParent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(context.DataDir));
        var candidates = new[]
        {
            dataParent is null ? null : Path.Combine(dataParent, "UEModManager.exe"),
            Path.Combine(AppContext.BaseDirectory, "UEModManager.exe"),
        };
        var executable = candidates.FirstOrDefault(path => path is not null && File.Exists(path));
        if (executable is null)
            return null;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(executable);
            var version = (info.ProductVersion ?? info.FileVersion)?.Split('+')[0];
            return new AppDto(executable, string.IsNullOrWhiteSpace(version) ? null : version);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new AppDto(executable, null);
        }
    }
}
