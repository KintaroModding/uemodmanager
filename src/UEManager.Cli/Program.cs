using System.Reflection;
using System.Text;
using System.Text.Json;
using UEManager.Cli.Commands;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Cli;

/// <summary>
/// The command line tool's commands (by CooperZhuang, PR #25), reached through <see cref="CommandLine"/> from the app's
/// exe: command dispatch, option handling and the one place where a failure becomes stderr and a non-zero exit code.
/// </summary>
internal static class Program
{
    public const string ToolName = "UEModManager --cli";

    /// <summary>Exit code 2: the command needs --yes; nothing was changed.</summary>
    public const int ConfirmationRequired = 2;

    public static string ToolVersion { get; } = VersionOf(typeof(Program).Assembly);

    /// <summary>The source revision the build names, when the SDK put one into the informational version.</summary>
    public static string? Commit { get; } = CommitOf(typeof(Program).Assembly);

    /// <summary>The entry point; internal, so the tests run commands in their own process.</summary>
    internal static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        finally
        {
            CliContext.Release();
        }
    }

    private static int Run(string[] args)
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch (IOException)
        {
            // A redirected stream may refuse the encoding; the default then stands.
        }

        var jsonRequested = args.Any(a => a.Equals("--json", StringComparison.OrdinalIgnoreCase));
        try
        {
            if (args.Length == 0 || args.Any(a => a is "-h" or "--help"))
            {
                Console.Out.Write(HelpText);
                return 0;
            }
            var arguments = CliArguments.Parse(args);
            if (arguments.Version)
            {
                Console.Out.WriteLine($"{ToolName} {ToolVersion}");
                return 0;
            }
            return arguments.Command switch
            {
                "status" => StatusCommand.Run(arguments),
                "list" => ListCommand.Run(arguments),
                "verify" => VerifyCommand.Run(arguments),
                "plan" => PlanCommand.Run(arguments),
                "install" => InstallCommand.Run(arguments),
                "enable" => ModCommand.RunToggle(arguments, enable: true),
                "disable" => ModCommand.RunToggle(arguments, enable: false),
                "uninstall" => ModCommand.RunUninstall(arguments),
                "backup" => BackupCommand.Run(arguments),
                "ue4ss" => Ue4ssCommand.Run(arguments),
                _ => Fail(new CliError("invalid-arguments", $"Unknown command '{arguments.Command}'.",
                    details: ["Run --help for the commands."]), jsonRequested),
            };
        }
        catch (CliError error)
        {
            return Fail(error, jsonRequested);
        }
        catch (ModException error)
        {
            // The Core's own, actionable messages: the same English text the app puts in its log.
            return Fail(new CliError("core-error", error.Message), jsonRequested);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return Fail(new CliError("io-error", error.Message), jsonRequested);
        }
        catch (Exception error)
        {
            return Fail(new CliError("internal-error", $"{error.GetType().Name}: {error.Message}"), jsonRequested);
        }
    }

    /// <summary>Writes the failure to stderr: plain text, or one JSON object when --json was asked for.</summary>
    internal static int Fail(CliError error, bool json)
    {
        if (json)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new ErrorPayload(1, new ErrorInfo(error.Code, error.Message,
                error.Details.Count == 0 ? null : error.Details)), JsonFile.Options));
        }
        else
        {
            Console.Error.WriteLine($"{ToolName}: {error.Message}");
            foreach (var detail in error.Details)
                Console.Error.WriteLine($"  {detail}");
        }
        return error.ExitCode;
    }

    private static string VersionOf(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    private static string? CommitOf(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var plus = informational?.IndexOf('+') ?? -1;
        return plus < 0 ? null : informational![(plus + 1)..];
    }

    private const string HelpText = """
        UE MM's command line: UEModManager.exe --cli <command> [options]

        Commands:
          status                          App version, game, data folder and mod count
          list                            Installed mods
          verify                          Compare deployed files with the recorded SHA-256
          plan <archive|folder>           Read-only install plan as JSON (Inspect + Check)
          install <archive|folder> --yes  Install the planned parts; without --yes it prints the plan and exits 2
          enable <mod>                    Enable an installed mod
          disable <mod>                   Disable an installed mod
          uninstall <mod> --yes [--remove-leftovers]
                                          Uninstall a mod; --remove-leftovers moves the files it left (logs) to the backups
          backup list                     Save backups with their notes, normal and personal
          backup create [--note <text>] [--keep <n>]
                                          Back up the game's save folder now
          backup restore <name> --yes     Put a save backup back; without --yes it prints what it would do and exits 2
          backup personal <name> --on|--off
                                          Keep a save backup for good (--on), or move it back (--off)
          backup note <name> <text>|--clear
                                          Set or clear a save backup's note
          backup delete <name> --yes      Delete a save backup; without --yes it prints what it would delete and exits 2
          ue4ss status [--check]          The UE4SS in the game: installed, version, source, and with --check any update
          ue4ss install [--package <file>] [--ue4ss-version <tag>]
                                          Install UE4SS from the manifest's release, or from a local package
          ue4ss update                    Install the manifest's UE4SS release when it differs; never an older one
          ue4ss remove --yes              Remove UE4SS; without --yes it prints what would be removed and exits 2

        Options:
          --data-dir <folder>   The manager's data folder; default: UEMM_DATA_DIR, then Data beside UEModManager.exe
          --game <id>           The game profile id; default: the game in settings.json
          --game-root <folder>  Use this install folder instead of the one in settings.json
          --save-folder <folder>
                                The save folder for backup create; default: settings.json, then the profile
          --note <text>         The note on a new save backup
          --keep <n>            Normal save backups to keep after a new one: 2 to 20, even; default: settings.json
          --on, --off           backup personal: keep the backup for good, or move it back
          --clear               backup note: remove the note
          --json                Machine-readable JSON output with stable fields
          --all-parts           Select every part of the download, not only the default selection
          --part <index|name>   Select only this part; repeat for more. Cannot be used with --all-parts
          --keep-both           Keep an installed mod the download would replace: install it as another mod
          --staging <folder>    Unpack downloads here, outside the game, instead of the data folder's staging folder
          --remove-leftovers    uninstall: move the files the mod left in the game into the manager's backups
          --yes                 Confirm a change; install, uninstall, ue4ss remove, backup restore and backup delete need it
          --check               With ue4ss status: read the manifest too, to tell whether a newer release exists
          --package <file>      With ue4ss install: a UE4SS release zip, 7z file or folder, instead of the manifest's
          --ue4ss-version <tag> With ue4ss install: the release version to record for --package, or the manifest's to require
          --ue4ss-manifest-url <url>
                                Read a test copy of the UE4SS manifest instead of the game profile's addresses
          -h, --help            Show this help
          --version             Show the CLI version

        Exit codes:
          0  success
          1  error (stderr says what)
          2  confirmation required; nothing was changed

        The game is never started. Read-only commands (status, list, verify, backup list, ue4ss status) do not write to
        the data folder or to the game; plan only unpacks the download into the data folder's staging folder and removes
        it again, or into the --staging folder. plan, install, enable, disable, uninstall, ue4ss install, ue4ss update,
        ue4ss remove and the backup commands that change the data folder refuse while UE MM is open with the same data
        folder: close the app first. ue4ss status works beside the open app.

        ue4ss install and update read the release manifest of the game profile, check the download's SHA-256, and install
        through the same Core code as the app; a failed download or install leaves no package behind. --package installs a
        release from this PC, which also works offline.

        UEModManager.exe is a window program, so a terminal does not wait for it: the prompt can come back before the
        output. Pipe the output (UEModManager.exe --cli list | more) or, in cmd, use start /wait. Scripts that wait for
        the process and read its output get the output and the exit code as usual.

        """ + "\n";
}