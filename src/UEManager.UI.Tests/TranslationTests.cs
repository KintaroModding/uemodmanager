using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>
/// The language files against the source: every text the app translates has a translation in every language,
/// each translation keeps the values of its text, and no file holds a text the app no longer uses. So a changed
/// English text fails here, naming the files to update, instead of showing in English to the player.
/// </summary>
public class TranslationTests
{
    private static readonly Lazy<SourceTexts> Source = new(SourceTexts.Read);

    [Fact]
    public void Every_language_has_a_translation_of_every_text_and_of_no_other()
    {
        var source = Source.Value;
        var catalogOnly = source.Catalog.Except(source.Main).ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var code in LanguageCodes())
        {
            var main = Read(MainFile(code), problems);
            var catalog = File.Exists(CatalogFile(code)) ? Read(CatalogFile(code), problems) : [];
            if (!main.ContainsKey(Localizer.NameKey))
                problems.Add($"{code}.json: no \"{Localizer.NameKey}\", the language's own name.");
            problems.AddRange(source.Main.Where(t => !main.ContainsKey(t)).Select(t => $"{code}.json: no translation of {Show(t)}"));
            problems.AddRange(catalogOnly.Where(t => !catalog.ContainsKey(t)).Select(t => $"Catalog {code}.json: no translation of {Show(t)}"));
            problems.AddRange(main.Keys.Where(t => t != Localizer.NameKey && !source.Main.Contains(t)).Select(t => $"{code}.json: not used any more: {Show(t)}"));
            problems.AddRange(catalog.Keys.Where(t => !catalogOnly.Contains(t)).Select(t => $"Catalog {code}.json: not used by the catalog: {Show(t)}"));
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Every_translation_keeps_the_values_of_its_text()
    {
        var problems = new List<string>();
        foreach (var file in LanguageCodes().SelectMany(code => new[] { MainFile(code), CatalogFile(code) }).Where(File.Exists))
        {
            foreach (var (english, text) in Read(file, problems).Where(entry => entry.Key != Localizer.NameKey))
            {
                if (!Placeholders(english).SequenceEqual(Placeholders(text)))
                    problems.Add($"{Path.GetFileName(file)}: the values differ in {Show(english)}");
                else if (!IsFormat(english) || !IsFormat(text))
                    problems.Add($"{Path.GetFileName(file)}: braces that are no value in {Show(english)}");
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>The scanner itself: one text of each kind it reads, so it cannot pass by finding nothing.</summary>
    [Theory]
    [InlineData("Settings")]                                    // {ui:Loc 'Settings'} in XAML
    [InlineData("Configuration")]                               // {ui:Loc 'Configuration', Tracked=True}
    [InlineData("Uninstall mod")]                               // L.T("…")
    [InlineData("Starting {0}…")]                               // L.F($"…")
    [InlineData("Game start of {0:g}:  {1},  {2}.")]            // L.F($"…") with a format and holes holding calls
    [InlineData("{0} mods turned off")]                         // L.Plural(n, "…", "…")
    [InlineData("That mod is no longer installed.")]            // new ModException($"…") in Core
    [InlineData("Select at least one part to install.")]        // problems.Add($"…") in Core
    [InlineData("{0} has no .pak file. Unreal Engine 5 games usually need it next to the .utoc and .ucas files.")]
    [InlineData("Phantom Blade Zero")]                          // a game profile's displayName
    public void The_scanner_reads_every_kind_of_text(string text) => Assert.Contains(text, Source.Value.Main);

    private static IEnumerable<string> LanguageCodes() =>
        Directory.EnumerateFiles(Path.Combine(SourceTexts.Root, "UEManager.UI", "Localization"), "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Select(code => code!);

    private static string MainFile(string code) => Path.Combine(SourceTexts.Root, "UEManager.UI", "Localization", code + ".json");

    private static string CatalogFile(string code) => Path.Combine(SourceTexts.Root, "UEManager.UI", "Catalog", "Localization", code + ".json");

    /// <summary>A language file's entries; a text that is in it twice is a problem, as only one of them would count.</summary>
    private static Dictionary<string, string> Read(string file, List<string> problems)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(File.ReadAllText(file));
        foreach (var entry in document.RootElement.EnumerateObject())
        {
            if (!entries.TryAdd(entry.Name, entry.Value.GetString() ?? ""))
                problems.Add($"{Path.GetFileName(file)}: twice: {Show(entry.Name)}");
        }
        return entries;
    }

    private static readonly Regex Placeholder = new(@"(?<!\{)\{\d+(?:,-?\d+)?(?::[^{}]*)?\}");

    private static string[] Placeholders(string text) =>
        Placeholder.Matches(text).Select(m => m.Value).Order(StringComparer.Ordinal).ToArray();

    private static bool IsFormat(string text)
    {
        try
        {
            _ = System.Text.CompositeFormat.Parse(text);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Show(string text) => JsonSerializer.Serialize(text, new JsonSerializerOptions
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}

/// <summary>
/// The texts the app translates, read from the source as the code writes them: L.T("…"), L.F($"…") and
/// L.Plural(n, "…", "…") in the UI; new ModException($"…"), problems.Add($"…") and warnings.Add($"…") in Core;
/// {ui:Loc '…'} in the XAML; and the names, taglines and UE4SS notes of the game profiles. An interpolated
/// string counts as its format, "Starting {0}…", as the app looks it up. Texts used only in a Catalog folder
/// belong to the catalog's language files.
/// </summary>
internal sealed class SourceTexts
{
    public HashSet<string> Main { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Catalog { get; } = new(StringComparer.Ordinal);

    /// <summary>The src folder of the repository.</summary>
    public static string Root { get; } = FindRoot();

    public static SourceTexts Read()
    {
        var texts = new SourceTexts();
        texts.ReadCode(Path.Combine(Root, "UEManager.UI"), ["L.T(", "L.F(", "L.Plural("]);
        texts.ReadCode(Path.Combine(Root, "UEManager.Core"), ["ModException(", "problems.Add(", "warnings.Add("]);
        texts.ReadXaml(Path.Combine(Root, "UEManager.UI"));
        texts.ReadProfiles(Path.Combine(Root, "UEManager.Core", "Games", "Profiles"));
        return texts;
    }

    private void Add(string text, string file)
    {
        var inCatalog = file.Split(Path.DirectorySeparatorChar).Contains("Catalog");
        (inCatalog ? Catalog : Main).Add(text);
    }

    private static IEnumerable<string> Files(string folder, string pattern) =>
        Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    private void ReadCode(string folder, string[] markers)
    {
        foreach (var file in Files(folder, "*.cs"))
        {
            var code = File.ReadAllText(file);
            foreach (var marker in markers)
            {
                for (var at = code.IndexOf(marker, StringComparison.Ordinal); at >= 0; at = code.IndexOf(marker, at + 1, StringComparison.Ordinal))
                {
                    if (at > 0 && (char.IsLetterOrDigit(code[at - 1]) || code[at - 1] == '_'))
                        continue;
                    var i = at + marker.Length;
                    if (marker == "L.Plural(")
                    {
                        SkipArgument(code, ref i);
                        if (Literal(code, ref i) is { } one)
                            Add(one, file);
                        SkipArgument(code, ref i);
                    }
                    if (Literal(code, ref i) is { } text)
                        Add(text, file);
                }
            }
        }
    }

    private static readonly Regex Loc = new(@"\{ui:Loc (?:'((?:[^'\\]|\\.)*)'|([^,}']+))");

    private void ReadXaml(string folder)
    {
        foreach (var file in Files(folder, "*.xaml"))
        {
            // The XML entities first, as the XAML reader does; then the markup extension's escapes.
            var xaml = WebUtility.HtmlDecode(File.ReadAllText(file));
            foreach (Match match in Loc.Matches(xaml))
            {
                var text = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value.Trim();
                Add(Regex.Replace(text, @"\\(.)", "$1"), file);
            }
        }
    }

    private void ReadProfiles(string folder)
    {
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            using var profile = JsonDocument.Parse(File.ReadAllText(file), options);
            foreach (var key in new[] { "displayName", "tagline", "ue4ssNote" })
            {
                if (profile.RootElement.TryGetProperty(key, out var value) && value.GetString() is { Length: > 0 } text)
                    Add(text, file);
            }
        }
    }

    /// <summary>
    /// The C# string literal at <paramref name="i"/>, as the app sees it at run time: an interpolated one as its
    /// format, with the holes numbered as FormattableString numbers them. Null when no literal starts there.
    /// </summary>
    private static string? Literal(string code, ref int i)
    {
        while (char.IsWhiteSpace(code[i]))
            i++;
        var interpolated = code[i] == '$' && code[i + 1] == '"';
        if (!interpolated && code[i] != '"')
            return null;
        i += interpolated ? 2 : 1;
        var text = new StringBuilder();
        var holes = 0;
        while (true)
        {
            var c = code[i];
            if (c == '"')
            {
                i++;
                return text.ToString();
            }
            if (c == '\\')
            {
                text.Append(Escape(code, ref i));
            }
            else if (interpolated && c is '{' or '}' && code[i + 1] == c)
            {
                text.Append(c, 2);
                i += 2;
            }
            else if (interpolated && c == '{')
            {
                i++;
                text.Append('{').Append(holes++).Append(SkipHole(code, ref i)).Append('}');
            }
            else
            {
                text.Append(c);
                i++;
            }
        }
    }

    private static string Escape(string code, ref int i)
    {
        var c = code[i + 1];
        i += 2;
        switch (c)
        {
            case 'n': return "\n";
            case 'r': return "\r";
            case 't': return "\t";
            case '0': return "\0";
            case 'u':
                i += 4;
                return ((char)Convert.ToInt32(code[(i - 4)..i], 16)).ToString();
            default: return c.ToString();
        }
    }

    /// <summary>Skips a hole's expression; returns its ",alignment" and ":format", and leaves i after the closing brace.</summary>
    private static string SkipHole(string code, ref int i)
    {
        var depth = 0;
        int? alignment = null;
        while (true)
        {
            var c = code[i];
            if (c == '"' || (c == '$' && code[i + 1] == '"'))
            {
                Literal(code, ref i);
                continue;
            }
            if (c == '\'')
            {
                i = code.IndexOf('\'', code[i + 1] == '\\' ? i + 3 : i + 2) + 1;
                continue;
            }
            if (depth == 0 && c == ',' && alignment is null)
                alignment = i;
            if (depth == 0 && c == ':' && code[i + 1] != ':')
            {
                var end = code.IndexOf('}', i);
                var part = code[(alignment ?? i)..end];
                i = end + 1;
                return part;
            }
            if (c is '(' or '[' or '{')
                depth++;
            else if (c is ')' or ']')
                depth--;
            else if (c == '}' && depth-- == 0)
            {
                var part = alignment is { } start ? code[start..i] : "";
                i++;
                return part;
            }
            i++;
        }
    }

    /// <summary>Skips to after the comma that ends a call's argument.</summary>
    private static void SkipArgument(string code, ref int i)
    {
        var depth = 0;
        while (true)
        {
            var c = code[i];
            if (c == '"' || (c == '$' && code[i + 1] == '"'))
            {
                Literal(code, ref i);
                continue;
            }
            if (c is '(' or '[' or '{')
                depth++;
            else if (c is ')' or ']' or '}')
                depth--;
            else if (c == ',' && depth == 0)
            {
                i++;
                return;
            }
            i++;
        }
    }

    private static string FindRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "UEManager.slnx")))
                return folder.FullName;
        }
        throw new DirectoryNotFoundException("The src folder with UEManager.slnx was not found above the test's folder.");
    }
}
