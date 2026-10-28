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

    /// <summary>A dotted version at the end of a name, such as "DashTest_0.2" or "Sharp Blades v1.3.0".</summary>
    [GeneratedRegex(@"^(?<name>.+?)[ _-]+[vV]?(?<version>\d+(?:\.\d+)+)$")]
    private static partial Regex TrailingVersion();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
