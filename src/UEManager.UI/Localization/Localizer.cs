using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using UEManager.Core.Mods;

namespace UEManager.UI;

/// <summary>A language the app can show, by its code and its own name, such as "zh-CN" and "简体中文".</summary>
public sealed record LanguageOption(string Code, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// The app's texts in the chosen language. The English text in the code is the key: every other
/// language is one file, Localization\&lt;code&gt;.json, that maps each English text to its own
/// ("$name" holds the language's own name), and the catalog has its texts in
/// Catalog\Localization\&lt;code&gt;.json. A text without a translation shows in English. Only what the
/// player sees is translated: logs and help reports stay English.
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    public const string English = "en";

    /// <summary>The key of a language's own name in its file.</summary>
    public const string NameKey = "$name";

    private const string ResourcePrefix = "UEManager.UI.Localization.";

    private readonly Dictionary<string, Dictionary<string, string>> translations = new(StringComparer.OrdinalIgnoreCase);
    private readonly CultureInfo system = CultureInfo.CurrentUICulture;
    private Dictionary<string, string>? texts;

    public static Localizer Current { get; } = new(typeof(Localizer).Assembly);

    internal Localizer(Assembly assembly)
    {
        // UEManager.UI.Localization.zh-CN.json, or UEManager.UI.Localization.Catalog.zh-CN.json for the catalog.
        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)))
        {
            var code = resource[..^".json".Length].Split('.')[^1];
            using var stream = assembly.GetManifestResourceStream(resource)!;
            if (!translations.TryGetValue(code, out var table))
                translations[code] = table = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (english, text) in JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [])
                table[english] = text;
        }
        Languages =
        [
            new LanguageOption(English, "English"),
            .. translations
                .Where(language => language.Value.ContainsKey(NameKey))
                .OrderBy(language => language.Key, StringComparer.OrdinalIgnoreCase)
                .Select(language => new LanguageOption(language.Key, language.Value[NameKey])),
        ];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>English first, then every language the app has a file for.</summary>
    public IReadOnlyList<LanguageOption> Languages { get; }

    /// <summary>The code of the language shown, such as "en" or "zh-CN".</summary>
    public string Language { get; private set; } = English;

    /// <summary>
    /// The language for a saved choice: that language when the app has it. Without a choice, or for one the app
    /// no longer has, the Windows display language when the app has it (or one close to it), else English.
    /// </summary>
    public string Resolve(string? code)
    {
        if (code is not null && Languages.FirstOrDefault(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) is { } chosen)
            return chosen.Code;
        return Closest(system);
    }

    /// <summary>
    /// The app's language closest to a Windows language: the same one, else one of the same script, else one
    /// of the same language. Chinese (Taiwan) gets Simplified Chinese, for example, and German (Austria) German.
    /// </summary>
    public string Closest(CultureInfo culture)
    {
        for (var wanted = culture; wanted.Name.Length > 0; wanted = wanted.Parent)
        {
            if (Languages.FirstOrDefault(l => Lineage(l.Code).Contains(wanted.Name, StringComparer.OrdinalIgnoreCase)) is { } match)
                return match.Code;
        }
        return English;
    }

    /// <summary>Shows the app in a language, as <see cref="Resolve"/> picks it; open views follow at once.</summary>
    public void SetLanguage(string? code)
    {
        var language = Resolve(code);
        texts = language == English ? null : translations[language];
        CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(language);
        if (Language == language)
            return;
        Language = language;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
    }

    /// <summary>An English text in the chosen language.</summary>
    public static string T(string english) =>
        Current.texts is { } texts && texts.TryGetValue(english, out var text) ? text : english;

    /// <summary>
    /// A text with values in it, such as $"Installing {name}…": the text is translated, the values (names,
    /// paths, numbers) are put in as they are.
    /// </summary>
    public static string F(FormattableString message) =>
        string.Format(CultureInfo.CurrentCulture, T(message.Format), message.GetArguments());

    /// <summary>
    /// A text with a number in it: <paramref name="one"/> for 1, else <paramref name="many"/>, such as
    /// "{0} mod" and "{0} mods". {0} is the number; {1} and on are the further values.
    /// </summary>
    public static string Plural(int count, string one, string many, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, T(count == 1 ? one : many), [count, .. values]);

    /// <summary>
    /// Sentences one after another, the empty ones left out: a space between them, except after a full-width
    /// mark such as 。, as Chinese and Japanese write them.
    /// </summary>
    public static string Join(params string?[] sentences)
    {
        var text = new System.Text.StringBuilder();
        foreach (var sentence in sentences.Where(s => !string.IsNullOrEmpty(s)))
        {
            if (text.Length > 0 && text[^1] is not ('。' or '！' or '？' or '）' or '」'))
                text.Append(' ');
            text.Append(sentence);
        }
        return text.ToString();
    }

    /// <summary>What went wrong, for the player: a <see cref="ModException"/> in the chosen language.</summary>
    public static string Message(Exception exception) =>
        exception is ModException { Text: var text } ? F(text) : exception.Message;

    /// <summary>A culture and the ones it belongs to, such as zh-CN, zh-Hans and zh.</summary>
    private static IEnumerable<string> Lineage(string code)
    {
        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(code);
        }
        catch (CultureNotFoundException)
        {
            return [code];
        }
        var names = new List<string>();
        for (; culture.Name.Length > 0; culture = culture.Parent)
            names.Add(culture.Name);
        return names;
    }
}
