using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace UEManager.Core.Localization;

/// <summary>Shared UI and operation messages. Unknown text falls back to the original English.</summary>
public sealed class Localizer : INotifyPropertyChanged
{
    public static Localizer Current { get; } = new();

    private readonly Dictionary<string, Translation> byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Translation> byEnglish = new(StringComparer.Ordinal);
    private readonly string systemLanguage = CultureInfo.CurrentUICulture.Name;

    private Localizer()
    {
        var assembly = typeof(Localizer).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".Localization.Strings.json", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            foreach (var (key, value) in JsonSerializer.Deserialize<Dictionary<string, Translation>>(stream)!)
            {
                byKey.Add(key, value);
                byEnglish.Add(value.English, value);
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Language { get; private set; } = "en";

    public string this[string key] => Translate(key);

    /// <summary>First run follows the OS language. Saved unsupported values fall back to English.</summary>
    public static string ResolveLanguage(string? language) =>
        (language ?? Current.systemLanguage).StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en";

    /// <summary>Called on the UI thread before notifying existing views and view models.</summary>
    public void SetLanguage(string? language)
    {
        var resolved = ResolveLanguage(language);
        var culture = CultureInfo.GetCultureInfo(resolved == "en" ? "en-US" : resolved);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        if (Language == resolved)
            return;
        Language = resolved;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    private string Translate(string text)
    {
        if (!byKey.TryGetValue(text, out var translation) && !byEnglish.TryGetValue(text, out translation))
            return text;
        return Language == "zh-CN" ? translation.Chinese : translation.English;
    }

    public static string T(string english) => Current.Translate(english);

    /// <summary>Translate the format before inserting values, leaving paths, names and user data intact.</summary>
    public static string F(FormattableString message) =>
        string.Format(CultureInfo.CurrentCulture, T(message.Format), message.GetArguments());

    private sealed record Translation(
        [property: System.Text.Json.Serialization.JsonPropertyName("en")] string English,
        [property: System.Text.Json.Serialization.JsonPropertyName("zh-CN")] string Chinese);
}
