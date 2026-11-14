using System.Security.Cryptography;
using UEManager.Core.Mods;

namespace UEManager.Cli;

/// <summary>
/// `uemm verify`: every deployed file of every mod is re-hashed and compared with the SHA-256 the manager recorded.
/// Settings files the player may edit, the game's own files and borrowed shared libraries are reported apart, as the
/// Core treats their changes as expected; everything else that differs is a problem.
/// </summary>
internal static class VerifyCommand
{
    public static int Run(CliArguments arguments)
    {
        arguments.Allow(["--json"], ["--data-dir", "--game", "--game-root"]);
        arguments.NoPositionals();
        var context = CliContext.Open(arguments, needInstall: false, write: false);
        var state = ModStateFile.Read(context);

        var mods = new List<VerifyModDto>();
        var problems = new List<VerifyFileDto>();
        var (checkedCount, ok, changed, missing, unreadable, expected) = (0, 0, 0, 0, 0, 0);

        foreach (var mod in state.Mods)
        {
            var files = new List<VerifyFileDto>();
            foreach (var file in mod.Files)
            {
                var result = Check(mod, file);
                files.Add(result);
                switch (result.Status)
                {
                    case "ok":
                        checkedCount++;
                        ok++;
                        break;
                    case "changed-expected":
                        expected++;
                        break;
                    case "changed":
                        checkedCount++;
                        changed++;
                        problems.Add(result);
                        break;
                    case "missing":
                        checkedCount++;
                        missing++;
                        problems.Add(result);
                        break;
                    case "unreadable":
                        checkedCount++;
                        unreadable++;
                        problems.Add(result);
                        break;
                }
            }
            mods.Add(new VerifyModDto(mod.Id, mod.Name, mod.Enabled, IntegrityOf(mod, files), files.Count, files));
        }

        var summary = new VerifySummaryDto(mods.Count, checkedCount, ok, changed, missing, unreadable, expected, problems.Count);
        var payload = new VerifyPayload(1, "verify", context.DataDir, context.GameId, summary, mods, problems, []);

        if (arguments.Json)
            Output.Json(payload);
        else
            WriteText(payload);

        if (problems.Count > 0)
        {
            return Program.Fail(new CliError("verify-failed",
                $"Verification found {problems.Count} file(s) that do not match the manager's records."), arguments.Json);
        }
        return 0;
    }

    private static VerifyFileDto Check(ModRecord mod, ModFileRecord file)
    {
        var playerMayChange = file.IsPlayerFile || file.Borrowed || file.IsGameFile;
        if (!mod.Enabled || file.DeployedPath is null)
            return Result(mod, file, "not-deployed", null, null, playerMayChange, null);

        var path = file.DeployedPath;
        FileInfo info;
        try
        {
            info = new FileInfo(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Result(mod, file, "unreadable", null, null, playerMayChange, error.Message);
        }
        if (!info.Exists)
            return Result(mod, file, "missing", null, null, playerMayChange, null);
        try
        {
            var actual = Sha256(path);
            if (string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
                return Result(mod, file, "ok", actual, info.Length, playerMayChange, null);
            return Result(mod, file, playerMayChange ? "changed-expected" : "changed", actual, info.Length, playerMayChange, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Result(mod, file, "unreadable", null, info.Length, playerMayChange, error.Message);
        }
    }

    private static VerifyFileDto Result(ModRecord mod, ModFileRecord file, string status, string? actual, long? actualSize,
        bool expectedChange, string? error) => new(mod.Id, mod.Name, file.FileName, file.Target.ToString(), file.DeployedPath,
        file.Sha256, status, actual, actualSize, expectedChange, error);

    /// <summary>The mod as a whole: problems in its files, else intact, else not deployed.</summary>
    private static string IntegrityOf(ModRecord mod, IReadOnlyList<VerifyFileDto> files)
    {
        if (!mod.Enabled)
            return "not-deployed";
        if (files.Any(file => file.Status is "missing" or "changed" or "unreadable"))
            return "problems";
        if (files.Any(file => file.Status == "changed-expected"))
            return "intact-expected-changes";
        return "intact";
    }

    private static void WriteText(VerifyPayload payload)
    {
        foreach (var mod in payload.Mods)
        {
            var state = mod.Enabled ? "on " : "off";
            Output.Line($"[{state}] {mod.Name} ({mod.Id}): {mod.Status}");
            foreach (var file in mod.Files.Where(file => file.Status is not "ok" and not "not-deployed"))
            {
                var detail = file.Status == "changed-expected" ? "changed (expected for a settings, game or borrowed file)"
                    : file.Error is null ? file.Status
                    : $"{file.Status}: {file.Error}";
                Output.Line($"    {file.Status,-17} {file.Target}\\{file.FileName}  {detail}");
            }
        }
        var summary = payload.Summary;
        Output.Line();
        Output.Line($"{summary.Mods} mod(s), {summary.FilesChecked} deployed file(s) checked: {summary.FilesOk} match, "
            + $"{summary.FilesChanged} changed, {summary.FilesMissing} missing, {summary.FilesUnreadable} unreadable, "
            + $"{summary.ExpectedChanges} expected change(s).");
        if (payload.Problems.Count == 0)
            Output.Line("Every file matches the manager's record.");
        else
            Output.Line($"{payload.Problems.Count} problem(s) to look at:");
        foreach (var problem in payload.Problems)
            Output.Line($"  {problem.ModName}: {problem.Target}\\{problem.FileName} is {problem.Status} ({problem.DeployedPath})");
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
