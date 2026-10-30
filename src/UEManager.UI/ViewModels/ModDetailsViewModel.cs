using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core.Mods;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>A file of a mod in its details: where it goes in the game, how big it is, and its state there.</summary>
/// <param name="Tone">"good", "warn" or "bad" for the colour of the state; empty while the mod is off.</param>
public sealed record ModFileItem(string Place, string FullPath, string Size, string State, string Tone);

/// <summary>
/// Everything about one installed mod: where it came from, when it was installed, and each file it placed in the
/// game with its state, as the player or a helper needs it to check a mod by hand.
/// </summary>
public sealed partial class ModDetailsViewModel(MainViewModel owner, ModRecord mod, IReadOnlyList<ModFileItem> files)
    : LocalizedObservableObject
{
    public string Name => mod.Name;

    public string Summary => string.Join("  ·  ", new[]
    {
        Format.Kind(mod.TypeLabel),
        mod.Version is null ? null : L.F($"Version {mod.Version}"),
        mod.Variant is null ? null : L.F($"Variant {mod.Variant}"),
        mod.Enabled ? L.T("Enabled") : L.T("Disabled"),
    }.Where(part => part is not null));

    public string InstalledText => mod.InstalledAt.LocalDateTime.ToString("f", CultureInfo.CurrentCulture);

    /// <summary>The download or folder it was installed from, as the player knows it.</summary>
    public string SourceText => mod.SourceFileName ?? "—";

    public string? CatalogId => mod.CatalogId;

    public string SizeText => $"{Format.Size(mod.TotalSize)}  ·  {L.Plural(files.Count, "{0} file", "{0} files")}";

    public IReadOnlyList<ModFileItem> Files => files;

    [RelayCommand]
    private void Close() => owner.CloseModDetails();

    /// <summary>
    /// The state of a mod's file in the game, by size and with the tones of <see cref="ModManager.Integrity"/>:
    /// a quick look, not a checksum.
    /// </summary>
    internal static (string State, string Tone) StateOf(ModRecord mod, ModFileRecord file)
    {
        if (!mod.Enabled)
            return (L.T("Not in the game"), "");
        var info = file.DeployedPath is null ? null : new FileInfo(file.DeployedPath);
        if (info is null || !info.Exists)
            return (L.T("Missing"), "warn");
        if (file.IsSettingsFile)
            return (L.T("In the game: settings you may change"), "good");
        return info.Length == file.Size ? (L.T("In the game"), "good") : (L.T("Changed outside the manager"), "bad");
    }
}
