using System.Globalization;

namespace UEManager.Core.Mods;

/// <summary>Mod versions such as "1.0" or "1.10", compared number by number.</summary>
public static class ModVersion
{
    /// <summary>"1.10" for the release tag "v1.10"; other tags stay as they are.</summary>
    public static string FromTag(string tag)
    {
        var trimmed = tag.Trim();
        return trimmed.Length > 1 && (trimmed[0] == 'v' || trimmed[0] == 'V') && char.IsAsciiDigit(trimmed[1]) ? trimmed[1..] : trimmed;
    }

    /// <summary>
    /// Whether <paramref name="latest"/> is an update for <paramref name="installed"/>: newer, when both
    /// are dotted numbers, otherwise simply different. An unknown installed version is offered the update.
    /// </summary>
    public static bool IsUpdate(string? installed, string latest)
    {
        if (string.IsNullOrWhiteSpace(installed))
            return true;
        var current = FromTag(installed);
        var offered = FromTag(latest);
        if (Numbers(current) is not { } a || Numbers(offered) is not { } b)
            return !string.Equals(current, offered, StringComparison.OrdinalIgnoreCase);
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y)
                return y > x;
        }
        return false;
    }

    private static long[]? Numbers(string version)
    {
        var parts = version.Split('.');
        var numbers = new long[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!long.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return null;
        }
        return numbers;
    }
}
