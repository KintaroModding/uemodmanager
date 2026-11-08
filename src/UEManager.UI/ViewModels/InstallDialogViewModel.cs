using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core.Mods;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <param name="KeepSettings">On an update, keep the settings files the player changed instead of the new defaults.</param>
/// <param name="Tags">The tags as the player typed them; null keeps the ones of the mod it updates.</param>
/// <param name="OwnFolder">The folder below ~mods for the files that go there; null puts them directly in ~mods.</param>
public sealed record InstallChoice(IReadOnlyList<PackageGroup> Packages, string Name, string? Version, bool Replace,
    string? CatalogId = null, bool KeepSettings = true, string? Tags = null, string? OwnFolder = null);

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
    private bool fillingTags;
    private bool tagsTyped;
    private bool placing;
    private string placesKey = "";

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
        // Files for the exe folder that do not look like a loader wait for the player, as do choices of which only one
        // may be in the game, unless the game tells which one it is.
        var preferred = manager.PreferredChoice(preview);
        selecting = true;
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in Packages)
        {
            var names = option.Package.RelativePaths.Select(p => $"{option.Package.Target}|{p}").ToList();
            option.IsSelected = !option.Package.IsUncertain && (!option.Package.IsAlternate || option.Package == preferred) && !names.Any(taken.Contains);
            if (option.IsSelected)
                taken.UnionWith(names);
        }
        foreach (var made in Packages.Where(p => p.IsSelected && p.Package.IsAlternate).ToList())
        {
            foreach (var other in Packages.Where(p => p != made && p.Package.VariantOf == made.Package.VariantOf))
                other.IsSelected = false;
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

    /// <summary>The download's number on the mod site, from its file name; null when the name has none.</summary>
    private string? SiteId => ModNaming.SiteModId(preview.SourceFileName);

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

    /// <summary>A kept mod's folder takes its name.</summary>
    partial void OnNameChanged(string value)
    {
        if (SelectedPlace is { KeepsBoth: true })
            Recheck();
    }

    /// <summary>What to do with a download that has the same file names as an installed mod; empty for any other.</summary>
    public ObservableCollection<PlaceOptionViewModel> Places { get; } = [];

    public bool HasPlaces => Places.Count > 0;

    private PlaceOptionViewModel? SelectedPlace => Places.FirstOrDefault(p => p.IsSelected);

    /// <summary>The folder below ~mods for this install, as the chosen place has it.</summary>
    private string? OwnFolder => SelectedPlace switch
    {
        null => null,
        { KeepsBoth: true } => manager.NewModFolder(Name),
        var place => place.Folder,
    };

    /// <summary>Both mods are in the game then, but the game may show only one of them.</summary>
    [ObservableProperty]
    private string? keepBothText;

    [ObservableProperty]
    private string version;

    /// <summary>The player's own words for the mod, as typed. An update starts with the ones the mod has.</summary>
    [ObservableProperty]
    private string tags = "";

    partial void OnTagsChanged(string value)
    {
        tagsTyped |= !fillingTags;
        OnPropertyChanged(nameof(Suggestions));
        OnPropertyChanged(nameof(HasSuggestions));
        AcceptSuggestionCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Tags to pick under the box; see <see cref="TagHints"/>.</summary>
    public IReadOnlyList<string> Suggestions => TagHints.For(Tags, TagHints.Own(manager.Mods));

    public bool HasSuggestions => Suggestions.Count > 0;

    [RelayCommand]
    private void AddSuggestion(string tag) => Tags = TagHints.Apply(Tags, tag);

    private bool CanAcceptSuggestion() => TagHints.IsTyping(Tags) && Suggestions.Count > 0;

    /// <summary>Tab completes the tag being typed with the first one offered.</summary>
    [RelayCommand(CanExecute = nameof(CanAcceptSuggestion))]
    private void AcceptSuggestion() => AddSuggestion(Suggestions[0]);

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
        KeepSettings,
        Tags,
        OwnFolder));

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

    /// <summary>The choice follows the ticked parts. Each mod that this may update is a place, an update first, as before; keeping both is the last.</summary>
    private void RefreshPlaces(IReadOnlyCollection<PackageGroup> selected)
    {
        var found = manager.SameNameMods(selected, catalog?.Id, SiteId);
        var key = string.Join('|', found.Select(f => $"{f.Mod.Id}:{f.CanUpdate}"));
        if (key == placesKey)
            return;
        placesKey = key;
        placing = true;
        Places.Clear();
        if (found.Count > 0)
        {
            foreach (var mod in found.Where(f => f.CanUpdate))
                Places.Add(new PlaceOptionViewModel(L.F($"Update \"{mod.Mod.Name}\""), false, mod.Folder, OnPlaceChanged));
            Places.Add(new PlaceOptionViewModel(L.T("Keep both: install this as another mod, in a folder of its own in ~mods"), true, null, OnPlaceChanged));
            if (!Places[0].KeepsBoth)
                Places[0].IsSelected = true;
        }
        placing = false;
        OnPropertyChanged(nameof(HasPlaces));
    }

    private void OnPlaceChanged(PlaceOptionViewModel place)
    {
        if (placing)
            return;
        placing = true;
        foreach (var other in Places.Where(p => p != place))
            other.IsSelected = false;
        placing = false;
        Recheck();
    }

    private void Recheck()
    {
        var chosen = Packages.Where(p => p.IsSelected).Select(p => p.Package).ToList();
        RefreshPlaces(chosen);
        var ownFolder = OwnFolder;
        KeepBothText = SelectedPlace is { KeepsBoth: true }
            ? L.F($"This mod and {string.Join(", ", manager.SameNameMods(chosen, catalog?.Id, SiteId).Select(f => f.Mod.Name))} may change the same things in the game. Then only one style or version shows, even with both turned on. Turn off the one you do not want. Only the mod's author can make both work together, with one combined mod.")
            : null;
        check = manager.Check(preview, chosen, catalog?.Id, ownFolder);
        Problems.Clear();
        foreach (var problem in check.Problems)
            Problems.Add(L.F(problem));
        Warnings.Clear();
        foreach (var warning in check.Warnings.Select(L.F).Distinct())
            Warnings.Add(warning);

        var old = check.Replaces;
        // Until the player types, the tags follow the mod this updates.
        if (!tagsTyped)
        {
            fillingTags = true;
            Tags = ModTags.Join(old?.Tags);
            fillingTags = false;
        }
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
        Rows = ModDetailsViewModel.Tree(FilesOf(chosen, ownFolder));
    }

    /// <summary>The files of the ticked parts, in the tree of their folders in the game.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows))]
    private IReadOnlyList<ModTreeRow> rows = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>The download's files that are not installed, by name, so a file that matters cannot go unnoticed.</summary>
    public string? IgnoredText
    {
        get
        {
            const int shown = 8;
            var ignored = preview.IgnoredFiles;
            if (ignored.Count == 0)
                return null;
            var names = string.Join(", ", ignored.Take(shown));
            if (ignored.Count > shown)
                names += "  " + L.Plural(ignored.Count - shown, "…and {0} more file", "…and {0} more files");
            return L.F($"Not installed: {names}");
        }
    }

    /// <summary>Each file of the parts where it goes in the game, with its size and what it is there.</summary>
    private IEnumerable<ModFileItem> FilesOf(IEnumerable<PackageGroup> packages, string? ownFolder)
    {
        var root = manager.Game.RootPath;
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages)
        {
            for (var i = 0; i < package.Files.Count; i++)
            {
                var relative = ModManager.InFolder(ownFolder, package.Target, package.RelativePaths[i]);
                var path = Path.Combine(manager.TargetFolder(package.Target), relative.Replace('/', Path.DirectorySeparatorChar));
                // A shared library that several parts bring lands once.
                if (!placed.Add(path))
                    continue;
                var (state, tone) = SettingsFiles.Is(package.Target, relative) ? (L.T("Settings you may change"), "good")
                    : package.Target is DeployTarget.InstallRoot or DeployTarget.ExeFolder && File.Exists(path)
                        ? (L.T("Takes the place of the game's file, which is kept"), "warn")
                    : ("", "");
                yield return new ModFileItem(Path.GetRelativePath(root, path), path, Format.Size(new FileInfo(package.Files[i]).Length), state, tone);
            }
        }
    }
}

/// <summary>One answer when a download has the same file names as an installed mod: update that mod, or keep both.</summary>
/// <param name="folder">Where the mod it updates has its files below ~mods; null directly in ~mods, or when keeping both.</param>
public sealed partial class PlaceOptionViewModel(string label, bool keepsBoth, string? folder, Action<PlaceOptionViewModel> changed) : LocalizedObservableObject
{
    public string Label => label;

    public bool KeepsBoth => keepsBoth;

    public string? Folder => folder;

    [ObservableProperty]
    private bool isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
            changed(this);
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

    /// <summary>Files for the exe folder that do not look like a loader; see <see cref="PackageGroup.IsUncertain"/>.</summary>
    public bool IsUncertain => package.IsUncertain;

    [ObservableProperty]
    private bool isSelected;

    partial void OnIsSelectedChanged(bool value) => changed(this);
}
