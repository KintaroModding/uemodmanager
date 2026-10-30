using System.Globalization;

namespace UEManager.UI.Services;

internal static class Format
{
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => string.Format(CultureInfo.CurrentCulture, "{0:0.#} KB", bytes / 1024.0),
        < 1024L * 1024 * 1024 => string.Format(CultureInfo.CurrentCulture, "{0:0.#} MB", bytes / (1024.0 * 1024)),
        _ => string.Format(CultureInfo.CurrentCulture, "{0:0.##} GB", bytes / (1024.0 * 1024 * 1024)),
    };

    /// <summary>A mod's type for the player: Blueprint is translated; PAK, IoStore and UE4SS are names.</summary>
    public static string Kind(string typeLabel) => typeLabel == "Blueprint" ? L.T("Blueprint") : typeLabel;

    /// <summary>
    /// Wide letter spacing for labels, which WPF text cannot do by itself.
    /// Letters are joined with narrow no-break spaces so a word never wraps inside.
    /// Chinese and Japanese text, which has no capitals and no spaces between words, stays as it is.
    /// </summary>
    public static string Tracked(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        if (text.Any(c => c >= '⺀'))
            return text;
        var words = text.ToUpper(CultureInfo.CurrentCulture).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join("  ", words.Select(word => string.Join(' ', word.ToCharArray())));
    }
}
