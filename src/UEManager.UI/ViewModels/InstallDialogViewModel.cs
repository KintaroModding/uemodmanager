using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core.Mods;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <param name="KeepSettings">On an update, keep the settings files the player changed instead of the new defaults.</param>
public sealed record InstallChoice(IReadOnlyList<PackageGroup> Packages, string Name, string? Version, bool Replace,
    string? CatalogId = null, bool KeepSettings = true);

/// <summary>A download from the catalog: the mod's id there, and the name and version the catalog gives.</summary>
public sealed record CatalogSource(string Id, string Name, string Version);

/// <summary>The review step between reading a download and installing it.</summary>
public sealed partial class InstallDialogViewModel : LocalizedObservableObject
{
    private readonly ModManager manager;
    private readonly InstallPreview preview;
    private readonly TaskCompletionSource<InstallChoice?> result = new();
    private readonly Action showUe4ssGuide;
    private readonly CatalogSource? catalog;
    private InstallCheck? check;
    private bool selecting;

    public InstallDialogViewModel(ModManager manager, InstallPreview preview, Action showUe4ssGuide, CatalogSource? catalog = null)
    {
        this.manager = manager;
        this.preview = preview;
        this.showUe4ssGuide = showUe4ssGuide;
        this.catalog = catalog;
        name = catalog?.Name ?? preview.SuggestedName;
        version = catalog?.Version ?? preview.SuggestedVersion ?? "";
        Packages = preview.Packages.Select(p => new PackageOptionViewModel(p, OnOptionChanged)).ToList();

        // Archives often ship variants of one package in separate folders. Those
        // deploy to the same place, so only the first of each is selected to begin with.
        selecting = true;
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in Packages)
        {
            var names = option.Package.RelativePaths.Select(p => $"{option.Package.Target}|{p}").ToList();
            option.IsSelected = !names.Any(taken.Contains);
            if (option.IsSelected)
                taken.UnionWith(names);
        }
        selecting = false;
        Recheck();

        // An update keeps the variant that is installed now.
        if (check?.Replaces?.Variant is { } installed
            && Packages.FirstOrDefault(p => p.IsVariant && string.Equals(p.Package.Variant, installed, StringComparison.OrdinalIgnoreCase)) is { IsSelected: false } same)
            same.IsSelected = true;
    }

    public Task<InstallChoice?> Result => result.Task;

    public string SourceFileName => preview.SourceFileName;

    public string Summary
    {
        get
        {
            var parts = new List<string> { L.Plural(Packages.Count, "{0} part found", "{0} parts found") };
            if (preview.IgnoredFileCount > 0)
                parts.Add(L.Plural(preview.IgnoredFileCount, "{0} other file ignored", "{0} other files ignored"));
            if (Packages.Count > 1)
                parts.Add(Packages.All(p => p.IsVariant) ? L.T("choose one variant") : L.T("choose what to install"));
            return string.Join("  ·  ", parts);
        }
    }

    public IReadOnlyList<PackageOptionViewModel> Packages { get; }

    public bool HasPackages => Packages.Count > 0;

    public ObservableCollection<string> Problems { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private string version;

    [ObservableProperty]
    private string? replaceText;

    [ObservableProperty]
    private string confirmText = L.T("Install");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool canInstall;

    /// <summary>Lua, DLL or Blueprint mods are selected and UE4SS is missing, so the guide is offered.</summary>
    [ObservableProperty]
    private bool needsUe4ss;

    /// <summary>The update would replace settings the player changed, so the player chooses.</summary>
    [ObservableProperty]
    private string? editedSettingsText;

    [ObservableProperty]
    private bool keepSettings = true;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private void Install() => result.TrySetResult(new InstallChoice(
        Packages.Where(p => p.IsSelected).Select(p => p.Package).ToList(),
        Name,
        string.IsNullOrWhiteSpace(Version) ? null : Version,
        check?.Replaces is not null,
        catalog?.Id,
        KeepSettings));

    [RelayCommand]
    private void Cancel() => result.TrySetResult(null);

    [RelayCommand]
    private void ShowUe4ssGuide() => showUe4ssGuide();

    /// <summary>Variants are one choice: selecting one clears the others.</summary>
    private void OnOptionChanged(PackageOptionViewModel option)
    {
        if (selecting)
            return;
        selecting = true;
        try
        {
            if (option.IsSelected && option.IsVariant)
            {
                foreach (var other in Packages.Where(p => p != option && p.Package.VariantOf == option.Package.VariantOf))
                    other.IsSelected = false;
            }
        }
        finally
        {
            selecting = false;
        }
        Recheck();
    }

    private void Recheck()
    {
        check = manager.Check(preview, Packages.Where(p => p.IsSelected).Select(p => p.Package).ToList(), catalog?.Id);
        Problems.Clear();
        foreach (var problem in check.Problems)
            Problems.Add(L.F(problem));
        Warnings.Clear();
        foreach (var warning in check.Warnings.Select(L.F).Distinct())
            Warnings.Add(warning);

        var old = check.Replaces;
        ReplaceText = old switch
        {
            null => null,
            { Version: null } => L.F($"This updates the installed mod \"{old.Name}\". Its files are replaced, and put back if the update fails."),
            _ => L.F($"This updates the installed mod \"{old.Name}\" (version {old.Version}). Its files are replaced, and put back if the update fails."),
        };
        EditedSettingsText = old is null || !check.HasEditedSettings
            ? null
            : L.F($"You changed {string.Join(", ", check.EditedSettings!.Select(SettingsFiles.Name))} of this mod. Keep your settings, or clear the box to use the new version's defaults. A copy of yours is saved in the backup folder either way.");
        ConfirmText = old is null ? L.T("Install") : L.T("Update");
        CanInstall = check.CanInstall;
        NeedsUe4ss = check.NeedsUe4ss;
    }
}

public sealed partial class PackageOptionViewModel(PackageGroup package, Action<PackageOptionViewModel> changed) : LocalizedObservableObject
{
    public PackageGroup Package => package;

    /// <summary>A variant shows what tells it apart, such as "x2"; other packages their folder and name.</summary>
    public string Label => package.Variant ?? package.Label;

    /// <summary>One of several variants: shown as a single choice.</summary>
    public bool IsVariant => package.VariantOf is not null;

    public string KindText => Format.Kind(package.TypeLabel);

    /// <summary>The files as they will be placed; a UE4SS mod's are shown inside its own folder, its shared libraries in full.</summary>
    public string FilesText => string.Join("   ", package.Kind is PackageKind.Ue4ss or PackageKind.Ue4ssFolder
        ? package.RelativePaths.Select(p => p.StartsWith(package.Name + "/", StringComparison.OrdinalIgnoreCase) ? p[(package.Name.Length + 1)..] : p)
        : package.RelativePaths);

    public string SizeText => Format.Size(package.Size);

    [ObservableProperty]
    private bool isSelected;

    partial void OnIsSelectedChanged(bool value) => changed(this);
}
