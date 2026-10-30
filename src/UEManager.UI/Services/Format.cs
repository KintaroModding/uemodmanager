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

    public static string Count(int count, string singular, string plural) =>
        $"{count} {L.T(count == 1 ? singular : plural)}";

    /// <summary>
    /// Wide letter spacing for labels, which WPF text cannot do by itself.
    /// Letters are joined with narrow no-break spaces so a word never wraps inside.
    /// </summary>
    public static string Tracked(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        if (L.Current.Language == "zh-CN")
            return text;
        var words = text.ToUpper(CultureInfo.CurrentCulture).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join("  ", words.Select(word => string.Join(' ', word.ToCharArray())));
    }
}
