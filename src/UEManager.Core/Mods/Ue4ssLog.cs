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
/// A point in the log of one game start: its first line, which tells one game start from the next, and
/// how many lines it had then.
/// </summary>
public sealed record Ue4ssLogMark(string FirstLine, int LineCount);

/// <summary>
/// The end of UE4SS.log, which UE4SS writes anew at every game start. Lines that speak of an error or a
/// warning are marked, and the mod folders an error names are picked out.
/// </summary>
/// <param name="End">Where this read ended, to read only the lines after it later.</param>
/// <param name="AfterMark">The lines are the ones after a mark of the same game start, not the whole log.</param>
public sealed partial record Ue4ssLog(string Path, IReadOnlyList<Ue4ssLogLine> Lines, bool Truncated, DateTime WrittenAt,
    Ue4ssLogMark End, bool AfterMark = false)
{
    /// <summary>A UE4SS log grows long with chatty mods; the end tells what went wrong.</summary>
    public const int MaxLines = 2000;

    public int ErrorCount => Lines.Count(l => l.Level == Ue4ssLogLevel.Error);

    public int WarningCount => Lines.Count(l => l.Level == Ue4ssLogLevel.Warning);

    /// <summary>
    /// Reads the log, or returns null when there is none yet, as before the first game start with UE4SS.
    /// With a mark of the same game start, only the lines written after it; a new game start reads all.
    /// </summary>
    public static Ue4ssLog? Read(string path, Ue4ssLogMark? after = null)
    {
        if (!File.Exists(path))
            return null;
        var lines = new Queue<string>();
        var truncated = false;
        string? first = null;
        var count = 0;
        var unfinished = false;
        // The game keeps the log open while it runs.
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream))
        {
            while (reader.ReadLine() is { } line)
            {
                first ??= line;
                count++;
                if (after is not null && first == after.FirstLine && count <= after.LineCount)
                    continue;
                lines.Enqueue(line);
                if (lines.Count > MaxLines)
                {
                    lines.Dequeue();
                    truncated = true;
                }
            }
            // A last line without its line break is still being written, so a mark leaves it out: it shows again,
            // whole, after the mark.
            if (count > 0 && stream.Length > 0)
            {
                stream.Seek(-1, SeekOrigin.End);
                if (stream.ReadByte() != '\n')
                    unfinished = true;
            }
        }
        return new Ue4ssLog(path, lines.Select(l => new Ue4ssLogLine(l, LevelOf(l))).ToList(), truncated, File.GetLastWriteTime(path),
            new Ue4ssLogMark(first ?? "", unfinished ? count - 1 : count), after is not null && first == after.FirstLine);
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
