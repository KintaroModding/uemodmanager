using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UEManager.Core;
using UEManager.Core.Games;
using UEManager.Core.Mods;

namespace UEManager.UI.Services;

/// <summary>What the manager knows about the game and the mods, taken on the UI thread before the report is written.</summary>
internal sealed record HelpFacts(
    GameProfile Profile,
    GameInstall? Game,
    Ue4ssRuntime? Ue4ss,
    string? ManagedUe4ssVersion,
    IReadOnlyList<ModRecord> Mods,
    IReadOnlyList<string> Unmanaged,
    string? OtherInstall,
    string LogsDir,
    IReadOnlyList<string>? Ue4ssDiffering = null);

/// <summary>
/// A text for the Discord help channel: the player's own words, then the facts a helper asks for first.
/// The Windows user name is taken out.
/// </summary>
internal static partial class HelpReport
{
    /// <summary>Log lines per log: the last errors and warnings tell the most.</summary>
    private const int LogLines = 12;

    private const int MaxLineLength = 300;

    private const int MaxUnmanagedNames = 10;

    /// <summary>Everything but the player's words. Reads the logs, so it runs off the UI thread.</summary>
    public static string Collect(HelpFacts facts)
    {
        var text = new StringBuilder();
        text.AppendLine($"App: {AppInfo.Name} {AppInfo.Version}, Windows {Environment.OSVersion.Version}");
        var game = facts.Game;
        text.AppendLine(game is null
            ? $"Game: {facts.Profile.DisplayName}, not found"
            : $"Game: {facts.Profile.DisplayName}, {StoreText(game)}");
        if (game is not null)
        {
            text.AppendLine($"Install folder: {game.RootPath}");
            text.AppendLine($"Game exe: {game.FindShippingExecutable() ?? $"not found in {game.BinariesPath}"}");
        }
        text.AppendLine(facts.Ue4ss switch
        {
            null => "UE4SS: -",
            { Installed: false } => "UE4SS: not installed",
            var ue4ss => $"UE4SS: installed, {ue4ss.Version ?? "version unknown"}"
                + (facts.ManagedUe4ssVersion is { } managed ? $" (installed by the manager: {managed})" : " (installed by hand)"),
        });
        // Compared with the official builds the UE4SS manifest lists; unknown without it.
        if (facts.Ue4ssDiffering is { } differing)
            text.AppendLine(differing.Count == 0
                ? "UE4SS build: official"
                : $"UE4SS build: not official, these files differ: {string.Join(", ", differing)}");

        text.AppendLine();
        text.AppendLine($"Mods: {facts.Mods.Count} installed, {facts.Mods.Count(m => m.Enabled)} enabled");
        foreach (var mod in facts.Mods)
            text.AppendLine($"  {(mod.Enabled ? "[on] " : "[off]")} {ModLine(mod)}");
        if (facts.Unmanaged.Count > 0)
            text.AppendLine($"Files in the mods folder the manager did not install: {facts.Unmanaged.Count}: "
                + string.Join(", ", facts.Unmanaged.Take(MaxUnmanagedNames)) + (facts.Unmanaged.Count > MaxUnmanagedNames ? ", ..." : ""));
        if (facts.OtherInstall is not null)
            text.AppendLine($"Enabled mods are still in another install of the game: {facts.OtherInstall}");
        if (facts.Ue4ss is { Installed: false } && facts.Mods.Any(m => m.Enabled && m.NeedsUe4ss))
            text.AppendLine("Enabled Lua, C++ or Blueprint mods need UE4SS, which is not installed.");

        if (facts.Ue4ss is { Installed: true } ue4ssRuntime)
        {
            text.AppendLine();
            AppendUe4ssLog(text, ue4ssRuntime);
        }
        text.AppendLine();
        AppendAppLog(text, facts.LogsDir);
        return text.ToString().TrimEnd();
    }

    /// <summary>The whole report: the player's words first, in a code block so Discord keeps the layout.</summary>
    public static string Compose(string problem, string facts)
    {
        // Three backticks of the player's own would end the code block early.
        var words = string.IsNullOrWhiteSpace(problem) ? "(not described)" : problem.Trim().Replace("```", "'''");
        return Privacy.HideUser(string.Join(Environment.NewLine,
            "```text", $"{AppInfo.Name} help report", "", "What happened:", words, "", facts, "```"));
    }

    private static string StoreText(GameInstall game) => game.Store switch
    {
        GameStore.Steam => $"Steam, build {game.BuildId ?? "unknown"}",
        GameStore.Epic => $"Epic Games, version {game.BuildId ?? "unknown"}",
        _ => "folder chosen by hand",
    };

    private static string ModLine(ModRecord mod)
    {
        var details = new[] { mod.Version is null ? null : $"version {mod.Version}", mod.TypeLabel, mod.Variant is null ? null : $"variant {mod.Variant}" }
            .Where(d => d is not null);
        var line = $"{mod.Name} ({string.Join(", ", details)})";
        return ModManager.Integrity(mod) switch
        {
            ModIntegrity.FilesMissing => line + ": FILES MISSING",
            ModIntegrity.FilesChanged => line + ": FILES CHANGED",
            _ => line,
        };
    }

    private static void AppendUe4ssLog(StringBuilder text, Ue4ssRuntime ue4ss)
    {
        Ue4ssLog? log;
        try
        {
            log = Ue4ssLog.Read(ue4ss.LogPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            text.AppendLine($"UE4SS log: could not be read ({ex.Message})");
            return;
        }
        if (log is null)
        {
            text.AppendLine("UE4SS log: none yet");
            return;
        }
        var mods = log.ModsNamedInErrors(ue4ss.ModFolders());
        text.AppendLine($"UE4SS log of {log.WrittenAt:yyyy-MM-dd HH:mm}: {log.ErrorCount} error(s), {log.WarningCount} warning(s)"
            + (mods.Count > 0 ? $"; errors name: {string.Join(", ", mods)}" : ""));
        foreach (var line in log.Lines.Where(l => l.Level != Ue4ssLogLevel.Info).TakeLast(LogLines))
            text.AppendLine("  " + Shorten(line.Text));
    }

    /// <summary>The last warnings and errors of the manager's newest log.</summary>
    private static void AppendAppLog(StringBuilder text, string logsDir)
    {
        var newest = Directory.Exists(logsDir)
            ? Directory.EnumerateFiles(logsDir, "app-*.log").OrderByDescending(f => f, StringComparer.Ordinal).FirstOrDefault()
            : null;
        if (newest is null)
        {
            text.AppendLine("App log: none");
            return;
        }
        List<string> problems;
        try
        {
            using var stream = new FileStream(newest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            problems = [];
            while (reader.ReadLine() is { } line)
            {
                if (AppLogProblem().IsMatch(line))
                    problems.Add(line);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            text.AppendLine($"App log: could not be read ({ex.Message})");
            return;
        }
        text.AppendLine(problems.Count == 0
            ? $"App log ({Path.GetFileName(newest)}): no warnings or errors"
            : $"App log ({Path.GetFileName(newest)}), last warnings and errors:");
        foreach (var line in problems.TakeLast(LogLines))
            text.AppendLine("  " + Shorten(line));
    }

    private static string Shorten(string line) => line.Length <= MaxLineLength ? line : line[..MaxLineLength] + " ...";

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} (WARN|ERROR)")]
    private static partial Regex AppLogProblem();
}
