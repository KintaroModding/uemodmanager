using System.Reflection;
using System.Text;
using System.Text.Json;
using UEManager.Cli.Commands;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Cli;

/// <summary>
/// Entry point of the headless prototype: command dispatch, option handling and the one place where a failure becomes
/// stderr and a non-zero exit code.
/// </summary>
internal static class Program
{
    public const string ToolName = "uemm-cli";

    /// <summary>Exit code 2: the command needs --yes; nothing was changed.</summary>
    public const int ConfirmationRequired = 2;

    public static string ToolVersion { get; } = VersionOf(typeof(Program).Assembly);

    /// <summary>The source revision the build names, when the SDK put one into the informational version.</summary>
    public static string? Commit { get; } = CommitOf(typeof(Program).Assembly);

    private const string DefaultDataDir = @"C:\Games\UEMM\Data";

    private static int Main(string[] args)
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
                "updates" => UpdatesCommand.Run(arguments),
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

    /// <summary>The data folder the commands use when --data-dir is not given.</summary>
    public static string? DefaultDataFolder => Directory.Exists(DefaultDataDir) ? DefaultDataDir : null;

    private const string HelpText = """
        uemm-cli - a headless prototype for UE MM

        Usage:
          uemm status                          App version, game, data folder and mod count
          uemm list                            Installed mods
          uemm verify                          Compare deployed files with the recorded SHA-256
          uemm plan <archive|folder>           Read-only install plan as JSON (Inspect + Check)
          uemm install <archive|folder> --yes  Install the planned parts; without --yes it prints the plan and exits 2
          uemm enable <mod>                    Enable an installed mod
          uemm disable <mod>                   Disable an installed mod
          uemm uninstall <mod> --yes           Uninstall a mod; without --yes it prints what would be removed and exits 2
          uemm updates                         Read the update records the app wrote for the game

        Options:
          --data-dir <folder>   The manager's data folder; default: Data beside the executable, then C:\Games\UEMM\Data
          --game <id>           The game profile id; default: the game in settings.json
          --game-root <folder>  Use this install folder instead of the one in settings.json
          --json                Machine-readable JSON output with stable fields
          --all-parts           Select every part of the download, not only the default selection
          --yes                 Confirm a change; install and uninstall need it
          -h, --help            Show this help
          --version             Show the CLI version

        Exit codes:
          0  success
          1  error (stderr says what)
          2  confirmation required; nothing was changed

        The game is never started. Read-only commands (status, list, verify, updates) do not write to the data folder
        or to the game; plan only unpacks the download into the data folder's staging folder and removes it again.

        """ + "\n";
}
