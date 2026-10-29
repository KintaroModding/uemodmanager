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
        var nexus = NexusFileName().Match(stem);
        if (nexus.Success)
        {
            stem = nexus.Groups["name"].Value;
            version = nexus.Groups["version"].Value.Replace('-', '.');
        }
        else if (NexusDatedFileName().Match(stem) is { Success: true } dated)
        {
            // The name often repeats a version of its own, such as "PhantomTweaks 1.0.0"; the download's version counts.
            stem = dated.Groups["name"].Value;
            if (TrailingVersion().Match(stem) is { Success: true } inName)
                stem = inName.Groups["name"].Value;
            version = dated.Groups["version"].Value;
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
