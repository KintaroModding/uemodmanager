namespace UEManager.Cli;

/// <summary>
/// The command line split into a command, its positional arguments and its options. Hand rolled: the CLI takes no
/// third-party package, and the grammar is small enough to keep in one place.
/// </summary>
internal sealed class CliArguments
{
    /// <summary>Options that take a value: "--data-dir C:\Games\UEMM\Data" or "--data-dir=C:\Games\UEMM\Data".</summary>
    private static readonly string[] ValueOptions =
    [
        "--data-dir", "--game", "--game-root", "--staging", "--part", "--save-folder", "--note", "--keep", "--package",
        "--ue4ss-version", "--ue4ss-manifest-url",
    ];

    /// <summary>Value options that may be given more than once, such as "--part 1 --part Extras".</summary>
    private static readonly string[] RepeatableOptions = ["--part"];

    private static readonly string[] FlagOptions =
        ["--json", "--yes", "--all-parts", "--keep-both", "--check", "--help", "--version", "--on", "--off", "--clear", "--remove-leftovers"];

    public string Command { get; private set; } = "";

    public List<string> Positionals { get; } = [];

    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Values of a repeatable option, in the order they were given.</summary>
    public Dictionary<string, List<string>> Lists { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Flags { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Json => Flags.Contains("--json");

    public bool Help => Flags.Contains("--help");

    public bool Version => Flags.Contains("--version");

    public bool Yes => Flags.Contains("--yes");

    public bool AllParts => Flags.Contains("--all-parts");

    /// <summary>uninstall: move the files the mod left in the game, such as its logs, into the manager's backups.</summary>
    public bool RemoveLeftovers => Flags.Contains("--remove-leftovers");

    /// <summary>Keep an installed mod that the download would replace, and install the download beside it.</summary>
    public bool KeepBoth => Flags.Contains("--keep-both");

    public bool Check => Flags.Contains("--check");

    public string? DataDir => Values.GetValueOrDefault("--data-dir");

    public string? Game => Values.GetValueOrDefault("--game");

    public string? GameRoot => Values.GetValueOrDefault("--game-root");

    /// <summary>
    /// Where to unpack the download (plan, install) or download UE4SS's release (ue4ss install, update) instead of the
    /// data folder's staging folder; null for the default.
    /// </summary>
    public string? Staging => Values.GetValueOrDefault("--staging");

    /// <summary>The parts named by --part, in the order given; empty for the plan's default selection.</summary>
    public IReadOnlyList<string> Parts => Lists.TryGetValue("--part", out var parts) ? parts : [];

    /// <summary>backup create: the save folder to use instead of the one in settings.json and the profile.</summary>
    public string? SaveFolder => Values.GetValueOrDefault("--save-folder");

    /// <summary>backup create: the player's note for the new backup.</summary>
    public string? Note => Values.GetValueOrDefault("--note");

    /// <summary>backup create: how many normal backups to keep, as text until it is checked.</summary>
    public string? Keep => Values.GetValueOrDefault("--keep");

    /// <summary>backup personal: make the backup a personal one.</summary>
    public bool On => Flags.Contains("--on");

    /// <summary>backup personal: move the backup back to the normal ones.</summary>
    public bool Off => Flags.Contains("--off");

    /// <summary>backup note: remove the note instead of setting one.</summary>
    public bool Clear => Flags.Contains("--clear");

    /// <summary>The release package UE4SS is installed from, when it is a file on this PC instead of the manifest's.</summary>
    public string? Package => Values.GetValueOrDefault("--package");

    /// <summary>
    /// ue4ss install: the release version to record for --package whose file name names none, else the manifest's to
    /// require. Not --version, which is the CLI's own version wherever it stands.
    /// </summary>
    public string? Ue4ssVersion => Values.GetValueOrDefault("--ue4ss-version");

    /// <summary>A test copy of the UE4SS manifest, read instead of the game profile's addresses, as the app's own option of that name.</summary>
    public string? Ue4ssManifestUrl => Values.GetValueOrDefault("--ue4ss-manifest-url");

    /// <exception cref="CliError">An option is unknown or lacks its value.</exception>
    public static CliArguments Parse(string[] args)
    {
        var parsed = new CliArguments();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var equals = arg.IndexOf('=');
                var name = equals < 0 ? arg : arg[..equals];
                var inline = equals < 0 ? null : arg[(equals + 1)..];
                if (ValueOptions.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    var value = inline;
                    if (value is null)
                    {
                        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                            throw new CliError("invalid-arguments", $"Option '{name}' needs a value.",
                                details: [$"Example: {name} {ValueExample(name)}"]);
                        value = args[++i];
                    }
                    if (RepeatableOptions.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        if (!parsed.Lists.TryGetValue(name, out var list))
                            parsed.Lists[name] = list = [];
                        list.Add(value);
                    }
                    else
                    {
                        parsed.Values[name] = value;
                    }
                }
                else if (FlagOptions.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    parsed.Flags.Add(name);
                }
                else
                {
                    throw new CliError("invalid-arguments", $"Unknown option '{name}'.", details: ["Run --help for the options."]);
                }
            }
            else if (arg is "-h")
            {
                parsed.Flags.Add("--help");
            }
            else if (arg.StartsWith('-') && arg.Length > 1)
            {
                throw new CliError("invalid-arguments", $"Unknown option '{arg}'.", details: ["Run --help for the options."]);
            }
            else if (parsed.Command.Length == 0)
            {
                parsed.Command = arg;
            }
            else
            {
                parsed.Positionals.Add(arg);
            }
        }
        return parsed;
    }

    /// <summary>The single positional argument a command needs, such as the download or the mod's name.</summary>
    /// <exception cref="CliError">There are too few or too many.</exception>
    public string Single(string what)
    {
        if (Positionals.Count == 1)
            return Positionals[0];
        throw Positionals.Count == 0
            ? new CliError("invalid-arguments", $"This command needs {what}.", details: ["Run --help for the usage."])
            : new CliError("invalid-arguments", $"This command takes one {what}, but {Positionals.Count} were given.",
                details: [.. Positionals.Select(p => $"  {p}")]);
    }

    /// <summary>Rejects extra words for a command that takes none, so a mistyped command is not silently ignored.</summary>
    /// <exception cref="CliError">Arguments were given.</exception>
    public void NoPositionals()
    {
        if (Positionals.Count == 0)
            return;
        throw new CliError("invalid-arguments", $"'{Command}' takes no arguments, but {Positionals.Count} were given.",
            details: [.. Positionals.Select(p => $"  {p}")]);
    }

    /// <summary>Rejects options that belong to another command, so a typo is not silently accepted.</summary>
    /// <exception cref="CliError">One of the given options is not allowed here.</exception>
    public void Allow(string[] flags, string[] values)
    {
        foreach (var flag in Flags.Where(f => !f.Equals("--help", StringComparison.OrdinalIgnoreCase) && !flags.Contains(f, StringComparer.OrdinalIgnoreCase)))
            throw new CliError("invalid-arguments", $"Option '{flag}' does not belong to '{Command}'.", details: ["Run --help for the usage."]);
        foreach (var value in Values.Keys.Concat(Lists.Keys)
                     .Where(v => !values.Contains(v, StringComparer.OrdinalIgnoreCase)))
            throw new CliError("invalid-arguments", $"Option '{value}' does not belong to '{Command}'.", details: ["Run --help for the usage."]);
    }

    /// <summary>What an option's value looks like, for the message when it is missing.</summary>
    private static string ValueExample(string option) => option.ToLowerInvariant() switch
    {
        "--part" => "<index|name>",
        "--game" => "<id>",
        "--note" => "<text>",
        "--keep" => "<number>",
        "--package" => "<file>",
        "--ue4ss-version" => "<tag>",
        "--ue4ss-manifest-url" => "<url>",
        _ => "<folder>",
    };
}
