namespace UEManager.Cli;

/// <summary>
/// The command line split into a command, its positional arguments and its options. Hand rolled: the CLI takes no
/// third-party package, and the grammar is small enough to keep in one place.
/// </summary>
internal sealed class CliArguments
{
    /// <summary>Options that take a value: "--data-dir C:\Games\UEMM\Data" or "--data-dir=C:\Games\UEMM\Data".</summary>
    private static readonly string[] ValueOptions = ["--data-dir", "--game", "--game-root"];

    private static readonly string[] FlagOptions = ["--json", "--yes", "--all-parts", "--help", "--version"];

    public string Command { get; private set; } = "";

    public List<string> Positionals { get; } = [];

    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Flags { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Json => Flags.Contains("--json");

    public bool Help => Flags.Contains("--help");

    public bool Version => Flags.Contains("--version");

    public bool Yes => Flags.Contains("--yes");

    public bool AllParts => Flags.Contains("--all-parts");

    public string? DataDir => Values.GetValueOrDefault("--data-dir");

    public string? Game => Values.GetValueOrDefault("--game");

    public string? GameRoot => Values.GetValueOrDefault("--game-root");

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
                            throw new CliError("invalid-arguments", $"Option '{name}' needs a value.", details: [$"Example: {name} <folder>"]);
                        value = args[++i];
                    }
                    parsed.Values[name] = value;
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
        foreach (var value in Values.Keys.Where(v => !values.Contains(v, StringComparer.OrdinalIgnoreCase)))
            throw new CliError("invalid-arguments", $"Option '{value}' does not belong to '{Command}'.", details: ["Run --help for the usage."]);
    }
}
