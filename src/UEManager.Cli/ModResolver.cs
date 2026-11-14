using UEManager.Core.Mods;

namespace UEManager.Cli;

/// <summary>A mod name on the command line to the installed mod it means.</summary>
internal static class ModResolver
{
    /// <summary>
    /// By id, else by whole name (case-insensitive), else by the one name that holds the text. The command line takes a
    /// name, so a player does not have to copy an id; a fragment that matches several mods asks for a fuller name.
    /// </summary>
    /// <exception cref="CliError">No mod matches, or more than one does.</exception>
    public static ModRecord Resolve(IReadOnlyList<ModRecord> mods, string query)
    {
        var byIdentity = mods.Where(m => m.Id.Equals(query, StringComparison.OrdinalIgnoreCase)
            || m.Name.Equals(query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byIdentity.Count == 1)
            return byIdentity[0];
        var byFragment = mods.Where(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byFragment.Count == 1)
            return byFragment[0];
        if (byFragment.Count == 0)
        {
            throw new CliError("mod-not-found", $"No installed mod matches '{query}'.",
                details: mods.Count == 0 ? ["No mod is installed for this game."] : [.. mods.Select(m => $"{m.Name} ({m.Id})")]);
        }
        throw new CliError("ambiguous-mod", $"'{query}' matches {byFragment.Count} installed mods.",
            details: [.. byFragment.Select(m => $"{m.Name} ({m.Id})")]);
    }
}
