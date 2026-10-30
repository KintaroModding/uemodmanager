using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core.Mods;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>One row in the mod list. Rows are rebuilt after every change, so they hold no state of their own.</summary>
public sealed partial class ModItemViewModel : LocalizedObservableObject
{
    private readonly MainViewModel owner;
    private readonly ModRecord record;
    private readonly string libraryFolder;

    public ModItemViewModel(MainViewModel owner, ModRecord record, string libraryFolder)
    {
        this.owner = owner;
        this.record = record;
        this.libraryFolder = libraryFolder;
        Integrity = ModManager.Integrity(record);
    }

    public string Id => record.Id;

    public string Name => record.Name;

    public bool Enabled => record.Enabled;

    public string EnabledText => Enabled ? L.T("Enabled") : L.T("Disabled");

    public string Detail => string.Join("  ·  ", new[]
    {
        record.Version is null ? null : L.F($"Version {record.Version}"),
        record.Variant is null ? null : L.F($"Variant {record.Variant}"),
        Format.Count(record.Files.Count, "file", "files"),
        Format.Size(record.TotalSize),
    }.Where(part => part is not null));

    /// <summary>A Lua, C++ or Blueprint mod, which only runs with UE4SS.</summary>
    public bool NeedsUe4ss => record.NeedsUe4ss;

    /// <summary>The mod has a settings file the player may change, such as Scripts/config.lua.</summary>
    public bool HasSettings => record.Files.Any(f => f.IsSettingsFile);

    /// <summary>The settings files in the game; none while the mod is disabled.</summary>
    public IEnumerable<string> DeployedSettingsFiles =>
        record.Files.Where(f => f.IsSettingsFile && f.DeployedPath is not null).Select(f => f.DeployedPath!);

    public string EditSettingsHint => Enabled
        ? L.F($"Edit this mod's settings: {string.Join(", ", record.Files.Where(f => f.IsSettingsFile).Select(f => SettingsFiles.Name(f.FileName)))}. ")
          + L.T("Most mods read them when the game starts.")
        : L.T("Enable the mod to edit its settings");

    public string KindText => L.T(record.TypeLabel);

    public DateTimeOffset InstalledAt => record.InstalledAt;

    public string InstalledText => record.InstalledAt.LocalDateTime.ToString(L.T("d MMM yyyy"), CultureInfo.CurrentCulture);

    public string InstalledFullText => record.InstalledAt.LocalDateTime.ToString("f", CultureInfo.CurrentCulture);

    public string FilesText => string.Join(Environment.NewLine, record.Files.Select(f => f.FileName));

    public ModIntegrity Integrity { get; }

    /// <summary>Empty for a disabled mod: the Status column already says so.</summary>
    public string IntegrityText => Integrity switch
    {
        ModIntegrity.Intact => L.T("Deployed"),
        ModIntegrity.FilesMissing => L.T("Files missing"),
        ModIntegrity.FilesChanged => L.T("Files changed"),
        _ => "",
    };

    public string IntegrityTone => Integrity switch
    {
        ModIntegrity.Intact => "good",
        ModIntegrity.FilesMissing => "warn",
        _ => "bad",
    };

    public string IntegrityGlyph => Integrity switch
    {
        ModIntegrity.Intact => "\uE73E",
        ModIntegrity.NotDeployed => "",
        _ => "\uE7BA",
    };

    public string? IntegrityHint => Integrity switch
    {
        ModIntegrity.Intact => FilesText,
        ModIntegrity.FilesMissing => L.T("Some deployed files are gone. Disable and enable the mod to put them back."),
        ModIntegrity.FilesChanged => L.T("A deployed file was changed outside the manager. The manager will not remove it."),
        _ => null,
    };

    public bool Matches(string search) =>
        string.IsNullOrWhiteSpace(search)
        || Name.Contains(search, StringComparison.OrdinalIgnoreCase)
        || (record.Version?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
        || record.TypeLabel.Contains(search, StringComparison.OrdinalIgnoreCase)
        || KindText.Contains(search, StringComparison.OrdinalIgnoreCase)
        || record.Files.Any(f => f.FileName.Contains(search, StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private Task Toggle() => owner.SetEnabledAsync(this, !Enabled);

    [RelayCommand]
    private Task Uninstall() => owner.UninstallAsync(this);

    [RelayCommand(CanExecute = nameof(Enabled))]
    private void EditSettings() => owner.EditSettings(this);

    [RelayCommand]
    private Task ResetSettings() => owner.ResetSettingsAsync(this);

    [RelayCommand]
    private void OpenFolder() => owner.OpenPath(libraryFolder);
}
