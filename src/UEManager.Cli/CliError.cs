namespace UEManager.Cli;

/// <summary>A problem the CLI reports on stderr, with the exit code that belongs to it.</summary>
internal sealed class CliError(string code, string message, int exitCode = 1, IReadOnlyList<string>? details = null) : Exception(message)
{
    /// <summary>A stable identifier for scripts, such as "mod-not-found".</summary>
    public string Code { get; } = code;

    public int ExitCode { get; } = exitCode;

    /// <summary>Further lines for the player, such as the candidates of an ambiguous mod name.</summary>
    public IReadOnlyList<string> Details { get; } = details ?? [];
}
