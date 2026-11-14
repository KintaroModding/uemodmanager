using UEManager.Core.Mods;
using UEManager.Core.Updates;

namespace UEManager.Cli.Commands;

/// <summary>
/// `uemm updates`: the mod page versions the app recorded for the game, joined with the installed mods. It reads the
/// record only; it never asks the mod site.
/// </summary>
internal static class UpdatesCommand
{
    public static int Run(CliArguments arguments)
    {
        arguments.Allow(["--json"], ["--data-dir", "--game", "--game-root"]);
        arguments.NoPositionals();
        var context = CliContext.Open(arguments, needInstall: false, write: false);
        var state = ModStateFile.Read(context);
        var (files, raw) = UpdatesReader.Read(context);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var records = new List<UpdateRecordDto>();
        foreach (var record in raw)
        {
            // The app writes one record per mod page; a second file (an older name) may repeat it.
            if (!seen.Add(record.ModId))
                continue;
            var installed = state.Mods.FirstOrDefault(mod => Matches(mod, record));
            var joined = installed is null ? null
                : new UpdateInstalledDto(installed.Id, installed.Name, installed.Version, installed.Enabled);
            var updateState = installed is null ? "not-installed"
                : ModSiteVersions.Compare(installed.Version, record.Version) switch
                {
                    ModSiteState.UpdateAvailable => "update-available",
                    ModSiteState.UpToDate => "up-to-date",
                    _ => "unknown",
                };
            records.Add(new UpdateRecordDto(record.RecordFile, record.ModId, record.Name, record.Version, record.UpdatedAt,
                record.CheckedAt, joined, updateState));
        }

        var summary = new UpdatesSummaryDto(records.Count,
            records.Count(record => record.State == "update-available"),
            records.Count(record => record.State == "up-to-date"),
            records.Count(record => record.State == "not-installed"),
            records.Count(record => record.State == "unknown"));
        var payload = new UpdatesPayload(1, "updates", context.GameId, files, UpdatesReader.LastChecked(raw), records, summary,
            files.Count == 0 ? $"No update records were found for '{context.GameId}'." : null);

        if (arguments.Json)
        {
            Output.Json(payload);
            return 0;
        }
        if (files.Count == 0)
        {
            Output.Line(payload.Message ?? "No update records were found.");
            return 0;
        }
        Output.Line($"Update records for {context.Profile.DisplayName} ({context.GameId}): {records.Count} record(s), "
            + $"checked {Output.Time(payload.CheckedAt)}");
        foreach (var file in files)
            Output.Line($"  {file}");
        foreach (var record in records)
        {
            var installed = record.Installed is { } mod
                ? $"{mod.Version ?? "no version"} installed"
                : "not installed";
            Output.Line($"[{record.State,-16}] {record.Name}: page {record.PageVersion ?? "unknown"}, {installed}");
        }
        return 0;
    }

    /// <summary>The installed mod a page record belongs to: the mod site's number in its download name, else its name.</summary>
    private static bool Matches(ModRecord mod, RawUpdateRecord record)
    {
        if (ModNaming.SiteModId(mod.SourceFileName) is { } siteId && siteId.Equals(record.ModId, StringComparison.Ordinal))
            return true;
        return mod.Name.Equals(record.Name, StringComparison.OrdinalIgnoreCase);
    }
}
