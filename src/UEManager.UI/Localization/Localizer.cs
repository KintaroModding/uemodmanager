using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using UEManager.Core.Mods;

namespace UEManager.UI;

/// <summary>A language the app can show: its code, its own name and its English name, such as "zh-CN", "简体中文" and "Simplified Chinese".</summary>
public sealed record LanguageOption(string Code, string Name, string EnglishName)
{
    public override string ToString() => Name;
}

/// <summary>
/// The app's texts in the chosen language. The English text in the code is the key: every other
/// language is one file, Localization\&lt;code&gt;.json, that maps each English text to its own
/// ("$name" and "$englishName" hold the language's names). A text without a translation shows in
/// English. Only what the player sees is translated: logs and help reports stay English.
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    public const string English = "en";

    /// <summary>The key of a language's own name in its file.</summary>
    public const string NameKey = "$name";

    /// <summary>The key of a language's English name in its file.</summary>
    public const string EnglishNameKey = "$englishName";

    private const string ResourcePrefix = "UEManager.UI.Localization.";

    private readonly Dictionary<string, Dictionary<string, string>> translations = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The languages of Windows: its display language, its format settings and the language it was installed in.</summary>
    private readonly CultureInfo[] windows = [CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture, CultureInfo.InstalledUICulture];

    private Dictionary<string, string>? texts;

    public static Localizer Current { get; } = new(typeof(Localizer).Assembly);

    internal Localizer(Assembly assembly)
    {
        // One resource per language, such as UEManager.UI.Localization.zh-CN.json.
        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            translations[resource[ResourcePrefix.Length..^".json".Length]] = new Dictionary<string, string>(
                JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [], StringComparer.Ordinal);
        }
        Languages =
        [
            new LanguageOption(English, "English", "English"),
            .. translations
                .Where(language => language.Value.ContainsKey(NameKey))
                .OrderBy(language => language.Key, StringComparer.OrdinalIgnoreCase)
                .Select(language => new LanguageOption(language.Key, language.Value[NameKey],
                    language.Value.GetValueOrDefault(EnglishNameKey, language.Value[NameKey]))),
        ];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>English first, then every language the app has a file for.</summary>
    public IReadOnlyList<LanguageOption> Languages { get; }

    /// <summary>The code of the language shown, such as "en" or "zh-CN".</summary>
    public string Language { get; private set; } = English;

    /// <summary>
    /// The language for a saved choice: that language when the app has it. Without a choice, or for one the app
    /// no longer has, English; the app then offers the language of Windows (see <see cref="Offer"/>).
    /// </summary>
    public string Resolve(string? code) =>
        code is not null && Languages.FirstOrDefault(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) is { } chosen
            ? chosen.Code
            : English;

    /// <summary>
    /// The language to offer a player who chose none yet: the one closest to the language of Windows, when the
    /// app has one besides English. A player may run English Windows with Chinese format settings, so each of
    /// the Windows languages counts. Null when English is the closest.
    /// </summary>
    public LanguageOption? Offer() => Offer(windows);

    /// <inheritdoc cref="Offer()"/>
    public LanguageOption? Offer(IEnumerable<CultureInfo> cultures) =>
        cultures.Select(Closest)
            .Where(code => code != English)
            .Select(code => Languages.First(l => l.Code == code))
            .FirstOrDefault();

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

    /// <summary>An English text in another language than the one shown, such as in the question that offers it.</summary>
    public static string In(string language, string english) =>
        Current.translations.TryGetValue(language, out var texts) && texts.TryGetValue(english, out var text) ? text : english;

    /// <summary>
    /// A text in a language and after it in English, for a player who reads either, as when the app offers that
    /// language. {0}, if used, is the language's name: its own name first, its English name after.
    /// </summary>
    public static string Both(LanguageOption language, string english, string separator = "\n") =>
        string.Format(CultureInfo.CurrentCulture, In(language.Code, english), language.Name)
        + separator + string.Format(CultureInfo.CurrentCulture, english, language.EnglishName);

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
