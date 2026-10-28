using System.Text.RegularExpressions;

namespace UEManager.Core.Mods;

public enum Ue4ssLogLevel
{
    Info,
    Warning,
    Error,
}

public sealed record Ue4ssLogLine(string Text, Ue4ssLogLevel Level);

/// <summary>
/// The end of UE4SS.log, which UE4SS writes anew at every game start. Lines that speak of an error or a
/// warning are marked, and the mod folders an error names are picked out.
/// </summary>
public sealed partial record Ue4ssLog(string Path, IReadOnlyList<Ue4ssLogLine> Lines, bool Truncated, DateTime WrittenAt)
{
    /// <summary>A UE4SS log grows long with chatty mods; the end tells what went wrong.</summary>
    public const int MaxLines = 2000;

    public int ErrorCount => Lines.Count(l => l.Level == Ue4ssLogLevel.Error);

    public int WarningCount => Lines.Count(l => l.Level == Ue4ssLogLevel.Warning);

    /// <summary>Reads the log, or returns null when there is none yet, as before the first game start with UE4SS.</summary>
    public static Ue4ssLog? Read(string path)
    {
        if (!File.Exists(path))
            return null;
        var lines = new Queue<string>();
        var truncated = false;
        // The game keeps the log open while it runs.
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream))
        {
            while (reader.ReadLine() is { } line)
            {
                lines.Enqueue(line);
                if (lines.Count > MaxLines)
                {
                    lines.Dequeue();
                    truncated = true;
                }
            }
        }
        return new Ue4ssLog(path, lines.Select(l => new Ue4ssLogLine(l, LevelOf(l))).ToList(), truncated, File.GetLastWriteTime(path));
    }

    public static Ue4ssLogLevel LevelOf(string line) =>
        ErrorWords().IsMatch(line) ? Ue4ssLogLevel.Error
        : WarningWords().IsMatch(line) ? Ue4ssLogLevel.Warning
        : Ue4ssLogLevel.Info;

    /// <summary>The mod folders, out of <paramref name="modFolders"/>, that error lines name.</summary>
    public IReadOnlyList<string> ModsNamedInErrors(IEnumerable<string> modFolders) => modFolders
        .Where(mod => Lines.Any(l => l.Level == Ue4ssLogLevel.Error
            && Regex.IsMatch(l.Text, $@"(?<![\w-]){Regex.Escape(mod)}(?![\w-])", RegexOptions.IgnoreCase)))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToList();

    [GeneratedRegex(@"\b(error|errors|exception|fatal|failed|failure)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorWords();

    [GeneratedRegex(@"\b(warn|warning|warnings)\b", RegexOptions.IgnoreCase)]
    private static partial Regex WarningWords();
}
