using System.Text.RegularExpressions;

namespace UEManager.Core.Mods;

/// <summary>Suggests a readable mod name and version from the download's file name.</summary>
public static partial class ModNaming
{
    public static (string Name, string? Version) FromSource(string source)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(source);
        var stem = Directory.Exists(trimmed) ? Path.GetFileName(trimmed) : Path.GetFileNameWithoutExtension(trimmed);

        string? version = null;
        var site = SiteMatch(stem);
        if (site is { Dated: false } nexus)
        {
            stem = nexus.Match.Groups["name"].Value;
            version = nexus.Match.Groups["version"].Value.Replace('-', '.');
        }
        else if (site is { Dated: true } dated)
        {
            // The name often repeats a version of its own, such as "PhantomTweaks 1.0.0"; the download's version counts.
            stem = dated.Match.Groups["name"].Value;
            if (TrailingVersion().Match(stem) is { Success: true } inName)
                stem = inName.Groups["name"].Value;
            version = dated.Match.Groups["version"].Value;
        }
        else if (TrailingVersion().Match(stem) is { Success: true } trailing)
        {
            stem = trailing.Groups["name"].Value;
            version = trailing.Groups["version"].Value;
        }

        // "Example_P" is Unreal's patch-package naming, not part of the mod's name.
        if (stem.EndsWith("_P", StringComparison.Ordinal))
            stem = stem[..^2];
        var name = Whitespace().Replace(stem.Replace('_', ' '), " ").Trim();
        return (name.Length > 0 ? name : "Unnamed mod", version);
    }

    /// <summary>
    /// The file's own name on the mod site, from a download that kept the site's file name, such as "PBZMapMarkers
    /// 1.8.4 Fog Teleport" in "PBZMapMarkers 1.8.4 Fog Teleport 134 1.8.4 2026-11-13T06-56Z R1oU2qvzl.zip"; null for
    /// any other name. The update check uses it to find the file the player has among the page's files.
    /// </summary>
    public static string? SiteFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(fileName));
        foreach (var stem in new[] { name, Path.GetFileNameWithoutExtension(name) })
        {
            if (SiteMatch(stem) is { } site)
                return site.Match.Groups["name"].Value;
        }
        return null;
    }

    /// <summary>
    /// Whether <paramref name="name"/> is one the app made from the download's file name, as the install review suggests
    /// it now or as older versions did from the whole name; false for a name the player gave the mod. An update keeps a
    /// player's name, and takes the new download's name for any other.
    /// </summary>
    public static bool IsNameFromSource(string name, string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;
        var trimmed = Path.TrimEndingDirectorySeparator(source);
        var stem = Path.GetFileNameWithoutExtension(trimmed);
        var whole = Whitespace().Replace(stem.Replace('_', ' '), " ").Trim();
        return string.Equals(name, FromSource(source).Name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, whole, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, Path.GetFileName(trimmed), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The mod's number on the mod site, from a download that kept the site's file name, such as 61 in
    /// "Travel Anywhere 61 1.0.1 2026-10-29T13-42Z LbtEo64mS.zip"; null for any other name. It needs no
    /// network: the number is part of the name. A folder unpacked under that name has it too.
    /// </summary>
    public static string? SiteModId(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(fileName));
        // A folder has no extension, and a dotted version in its name would look like one: try the name as it is first.
        foreach (var stem in new[] { name, Path.GetFileNameWithoutExtension(name) })
        {
            if (SiteMatch(stem) is { } site)
                return site.Match.Groups["id"].Value;
        }
        return null;
    }

    /// <summary>
    /// One of the mod site's two name patterns on <paramref name="stem"/>, as it is or without the copy number a
    /// browser adds to a second download of the same file, such as "(1)" or " (2)". Other names keep such a number.
    /// </summary>
    private static (Match Match, bool Dated)? SiteMatch(string stem)
    {
        string[] candidates = CopyNumber().Match(stem) is { Success: true } copy ? [stem, copy.Groups["stem"].Value] : [stem];
        foreach (var candidate in candidates)
        {
            if (NexusFileName().Match(candidate) is { Success: true } plain)
                return (plain, false);
            if (NexusDatedFileName().Match(candidate) is { Success: true } dated)
                return (dated, true);
        }
        return null;
    }

    /// <summary>A browser's copy number at the end of a name, such as "YQ3oJLMF9(1)" or "LbtEo64mS (2)".</summary>
    [GeneratedRegex(@"^(?<stem>.+?)\s*\(\d{1,3}\)$")]
    private static partial Regex CopyNumber();

    /// <summary>Nexus Mods names downloads "Name-ModId-Version-Timestamp", such as "Sharp Blades-12-1-2-1730000000".</summary>
    [GeneratedRegex(@"^(?<name>.+?)-(?<id>\d+)-(?<version>\d+(?:-\d+)*)-(?<stamp>\d{9,})$")]
    private static partial Regex NexusFileName();

    /// <summary>
    /// Newer Nexus Mods downloads: "Name ModId Version Time Key", with spaces or underscores, such as
    /// "Travel Anywhere 61 1.0.1 2026-10-29T13-42Z LbtEo64mS".
    /// </summary>
    [GeneratedRegex(@"^(?<name>.+?)[ _]+(?<id>\d+)[ _]+(?<version>[^ _]+)[ _]+\d{4}-\d{2}-\d{2}T\d{2}-\d{2}Z[ _]+[A-Za-z0-9]+$")]
    private static partial Regex NexusDatedFileName();

    /// <summary>A dotted version at the end of a name, such as "DashTest_0.2" or "Sharp Blades v1.3.0".</summary>
    [GeneratedRegex(@"^(?<name>.+?)[ _-]+[vV]?(?<version>\d+(?:\.\d+)+)$")]
    private static partial Regex TrailingVersion();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
