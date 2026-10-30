using System.Text.RegularExpressions;

namespace UEManager.UI.Services;

/// <summary>A piece of a line of release notes: plain, bold, code, or a link, which can also be bold.</summary>
/// <param name="Url">The http or https address of a link; null for other text.</param>
public sealed record NotesSpan(string Text, bool Bold = false, bool Code = false, string? Url = null);

/// <summary>A line of release notes.</summary>
/// <param name="Level">0 for a paragraph, 1 for a list item, 2 and up for an item in a list inside it.</param>
/// <param name="Gap">An empty line came before it, so it starts a new part.</param>
public sealed record NotesLine(int Level, bool Gap, IReadOnlyList<NotesSpan> Spans);

/// <summary>
/// Reads release notes as GitHub shows them, for the parts the notes use: lines, lists ("- " or "* ", two spaces
/// more for each level), headings ("#" lines show bold), **bold**, `code` and [links](https://...). Anything
/// else stays as it is written. A link to an address that is not http or https shows as text only.
/// </summary>
public static partial class NotesText
{
    public static IReadOnlyList<NotesLine> Parse(string? notes)
    {
        var lines = new List<NotesLine>();
        var gap = false;
        foreach (var raw in (notes ?? "").Replace("\r", "").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                gap = lines.Count > 0;
                continue;
            }
            var indent = raw.Length - raw.TrimStart(' ').Length;
            var text = raw.Trim();
            var level = 0;
            if (text.Length > 2 && text[0] is '-' or '*' && text[1] == ' ')
            {
                level = 1 + indent / 2;
                text = text[2..].TrimStart();
            }
            else if (Heading().Match(text) is { Success: true } heading)
            {
                lines.Add(new NotesLine(0, gap, [new NotesSpan(heading.Groups["text"].Value, Bold: true)]));
                gap = false;
                continue;
            }
            lines.Add(new NotesLine(level, gap, Spans(text, bold: false)));
            gap = false;
        }
        return lines;
    }

    /// <summary>The pieces of a line; inside bold text, code and links are still found.</summary>
    private static List<NotesSpan> Spans(string text, bool bold)
    {
        var spans = new List<NotesSpan>();
        var plain = 0;
        void Plain(int end)
        {
            if (end > plain)
                spans.Add(new NotesSpan(text[plain..end], bold));
        }

        var i = 0;
        while (i < text.Length)
        {
            if (!bold && text.AsSpan(i).StartsWith("**") && text.IndexOf("**", i + 2, StringComparison.Ordinal) is var close and > 0
                && close > i + 2)
            {
                Plain(i);
                spans.AddRange(Spans(text[(i + 2)..close], bold: true));
                i = plain = close + 2;
            }
            else if (text[i] == '`' && text.IndexOf('`', i + 1) is var end and > 0 && end > i + 1)
            {
                Plain(i);
                spans.Add(new NotesSpan(text[(i + 1)..end], bold, Code: true));
                i = plain = end + 1;
            }
            else if (text[i] == '[' && Link().Match(text, i) is { Success: true } link && link.Index == i)
            {
                Plain(i);
                var url = link.Groups["url"].Value;
                var web = Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
                spans.Add(new NotesSpan(link.Groups["text"].Value, bold, Url: web ? url : null));
                i = plain = i + link.Length;
            }
            else
            {
                i++;
            }
        }
        Plain(text.Length);
        return spans;
    }

    [GeneratedRegex(@"^#{1,6}\s+(?<text>.+?)\s*#*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"\G\[(?<text>[^\]]+)\]\((?<url>[^)\s]+)\)")]
    private static partial Regex Link();
}
