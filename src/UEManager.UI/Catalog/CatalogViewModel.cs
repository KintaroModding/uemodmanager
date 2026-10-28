using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core;
using UEManager.Core.Catalog;
using UEManager.Core.Mods;
using UEManager.UI.ViewModels;

namespace UEManager.UI.Catalog;

/// <summary>
/// The openmods page: the game's mods listed on openmods.nexus, each with a one-click install.
/// The lists are read when the page opens, at most every 10 minutes, as openmods asks.
/// </summary>
public sealed partial class CatalogViewModel : ObservableObject
{
    public const string SiteUrl = "https://openmods.nexus/";

    private readonly MainViewModel owner;
    private readonly HttpClient http;
    private readonly RemoteImages images;
    private readonly string gamesUrl;
    private readonly string? steamAppId;
    private DateTimeOffset loadedAt;
    private bool loaded;
    private IReadOnlyList<ModRecord> installed = [];

    /// <summary>The game's page on openmods, once the list is loaded.</summary>
    private string? gamePage;

    /// <param name="gamesUrl">The openmods games list.</param>
    /// <param name="isTest">The list was given on the command line, so the page says that it is not the real one.</param>
    public CatalogViewModel(MainViewModel owner, HttpClient http, string gamesUrl, string? steamAppId, bool isTest,
        CatalogSortField sortField, bool sortDescending)
    {
        this.owner = owner;
        this.http = http;
        this.gamesUrl = gamesUrl;
        this.steamAppId = steamAppId;
        images = new RemoteImages(http);
        TestCatalogText = isTest ? $"Test list: {gamesUrl}. Start the app without --catalog to see the real one." : null;
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = item => item is CatalogItemViewModel mod && mod.Matches(SearchText);
        this.sortField = sortField;
        this.sortDescending = sortDescending;
        ApplySort();
    }

    public ObservableCollection<CatalogItemViewModel> Items { get; } = [];

    /// <summary>The cards as shown: filtered by the search and sorted.</summary>
    public ICollectionView ItemsView { get; }

    public string? TestCatalogText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    private bool isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    private string? error;

    [ObservableProperty]
    private string searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameSortGlyph), nameof(UpdatedSortGlyph))]
    private CatalogSortField sortField;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameSortGlyph), nameof(UpdatedSortGlyph))]
    private bool sortDescending;

    public string NameSortGlyph => SortGlyph(CatalogSortField.Name);

    public string UpdatedSortGlyph => SortGlyph(CatalogSortField.Updated);

    public bool HasItems => Items.Count > 0;

    public string CountText => $"{ItemsView.Cast<object>().Count()} of {Items.Count} shown";

    /// <summary>The lists were read, and openmods lists nothing for this game.</summary>
    public bool ShowEmpty => loaded && !IsLoading && Error is null && Items.Count == 0;

    public string EmptyText => $"No mods for {owner.Profile.DisplayName} are listed on openmods yet.";

    /// <summary>Mods are listed, but none matches the search.</summary>
    public bool ShowNoMatch => Items.Count > 0 && ItemsView.IsEmpty;

    /// <summary>Reads the lists unless they were read less than 10 minutes ago.</summary>
    /// <param name="force">The user asked for it: says so when the list is still fresh.</param>
    public async Task LoadAsync(bool force = false)
    {
        if (IsLoading)
            return;
        if (loaded && Error is null && DateTimeOffset.Now - loadedAt < OpenMods.RefreshInterval)
        {
            if (force)
                owner.ShowToast("The list is up to date. openmods refreshes it every 10 minutes.");
            return;
        }
        IsLoading = true;
        Error = null;
        try
        {
            var list = await OpenMods.FetchAsync(http, gamesUrl, steamAppId);
            loadedAt = DateTimeOffset.Now;
            loaded = true;
            Show(list);
        }
        catch (ModException ex)
        {
            Log.Warn(ex.Message);
            Error = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Updates every card after the installed mods or UE4SS changed.</summary>
    public void UpdateInstalled(IReadOnlyList<ModRecord> mods)
    {
        installed = mods;
        foreach (var item in Items)
            item.UpdateState(mods);
    }

    [RelayCommand]
    private Task Reload() => LoadAsync(force: true);

    [RelayCommand]
    private void OpenSite() => owner.OpenPath(gamePage ?? SiteUrl);

    /// <summary>
    /// A click on the field the page is already sorted by reverses the order. A new field
    /// starts with names A to Z, or with the newest release first.
    /// </summary>
    [RelayCommand]
    private void SortBy(CatalogSortField field)
    {
        if (field == SortField)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortField = field;
            SortDescending = field == CatalogSortField.Updated;
        }
        ApplySort();
        owner.SaveCatalogSort(SortField, SortDescending);
    }

    partial void OnSearchTextChanged(string value)
    {
        ItemsView.Refresh();
        ShownChanged();
    }

    private void Show(CatalogList? list)
    {
        Items.Clear();
        gamePage = list?.Game.Page;
        foreach (var mod in list?.Mods ?? [])
        {
            var item = new CatalogItemViewModel(owner, mod);
            item.UpdateState(installed);
            Items.Add(item);
        }
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(HasItems));
        ShownChanged();
        _ = LoadCoversAsync(Items.ToList(), list?.Game.Art);
    }

    private void ShownChanged()
    {
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(ShowNoMatch));
    }

    private void ApplySort() => ((ListCollectionView)ItemsView).CustomSort = new CatalogOrder(SortField, SortDescending);

    /// <summary>An arrow on the field the page is sorted by; a quiet dash on the other one, so it reads as sortable too.</summary>
    private string SortGlyph(CatalogSortField field) =>
        field != SortField ? "" : SortDescending ? "" : "";

    /// <summary>A mod without a cover shows the game's picture, as openmods does.</summary>
    private Task LoadCoversAsync(IReadOnlyList<CatalogItemViewModel> items, string? gameArt) =>
        Task.WhenAll(items.Select(async item => item.Cover = await images.LoadAsync(item.Mod.Cover) ?? await images.LoadAsync(gameArt)));
}

/// <summary>One mod on the openmods page.</summary>
public sealed partial class CatalogItemViewModel(MainViewModel owner, CatalogMod mod) : ObservableObject
{
    private enum CardAction
    {
        None,
        Install,
        InstallUe4ss,
        UpdateUe4ss,
    }

    private CardAction action;

    public CatalogMod Mod => mod;

    public string Name => mod.Name;

    public string? Summary => mod.Summary;

    public string AuthorText => $"by {mod.Author ?? "an unknown author"}";

    /// <summary>Set when openmods lists another author's project.</summary>
    public string? ListedByText => mod.ListedBy is null ? null : $"listed by {mod.ListedBy}";

    public IReadOnlyList<string> Tags => mod.Tags;

    public string MetaText => string.Join("  ·  ", new[]
    {
        mod.Release is null ? "No release yet" : $"Version {mod.Release.Version}",
        mod.Updated is { } updated ? "Updated " + updated.LocalDateTime.ToString("d MMM yyyy", CultureInfo.CurrentCulture) : null,
        mod.License,
    }.Where(part => part is not null));

    public bool HasPage => mod.Page is not null;

    public bool HasSource => mod.Source is not null;

    [ObservableProperty]
    private ImageSource? cover;

    [ObservableProperty]
    private string? actionText;

    [ObservableProperty]
    private bool hasAction;

    /// <summary>What is installed, or why there is nothing to install.</summary>
    [ObservableProperty]
    private string? statusText;

    [ObservableProperty]
    private string statusTone = "muted";

    [ObservableProperty]
    private string statusGlyph = "";

    /// <summary>The search looks at the name, the description, the authors and the tags.</summary>
    public bool Matches(string search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;
        var text = search.Trim();
        return new[] { mod.Name, mod.Summary, mod.Author, mod.ListedBy }.Concat(mod.Tags)
            .Any(field => field?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false);
    }

    internal void UpdateState(IReadOnlyList<ModRecord> installed)
    {
        Status(null, "muted", "");
        if (mod.IsUe4ss)
        {
            // UE4SS has its own install: the manifest names the release made for this game.
            if (owner.IsUe4ssUpdateAvailable)
                Set(CardAction.UpdateUe4ss, owner.Ue4ssUpdateText);
            else if (owner.IsUe4ssInstalled)
                Set(CardAction.None, null);
            else
                Set(CardAction.InstallUe4ss, "Install UE4SS…");
            if (owner.IsUe4ssInstalled)
                Status(owner.Ue4ssText, "good", "");
            return;
        }
        if (mod.Release is null || mod.InstallFile is null)
        {
            Set(CardAction.None, null);
            Status(mod.Release is null || mod.Release.Files.Count == 0 ? "No download yet" : "Several files: see the mod's page", "muted", "");
            return;
        }

        var record = installed.FirstOrDefault(m => string.Equals(m.CatalogId, mod.Id, StringComparison.OrdinalIgnoreCase));
        if (record is null)
        {
            Set(CardAction.Install, "Install");
            return;
        }
        var text = record.Version is null ? "Installed" : $"Installed  ·  {record.Version}";
        Status(record.Enabled ? text : $"{text}  ·  disabled", "good", "");
        if (ModVersion.IsUpdate(record.Version, mod.Release.Version))
            Set(CardAction.Install, $"Update to {mod.Release.Version}");
        else
            Set(CardAction.None, null);
    }

    [RelayCommand]
    private Task Act()
    {
        switch (action)
        {
            case CardAction.Install:
                return owner.InstallFromCatalogAsync(mod);
            case CardAction.UpdateUe4ss:
                return owner.DownloadAndInstallUe4ssAsync();
            case CardAction.InstallUe4ss:
                owner.ShowUe4ssGuideCommand.Execute(null);
                break;
        }
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void OpenPage()
    {
        if (mod.Page is not null)
            owner.OpenPath(mod.Page);
    }

    [RelayCommand]
    private void OpenSource()
    {
        if (mod.Source is not null)
            owner.OpenPath(mod.Source);
    }

    private void Set(CardAction next, string? text)
    {
        action = next;
        ActionText = text;
        HasAction = next != CardAction.None;
    }

    private void Status(string? text, string tone, string glyph)
    {
        StatusText = text;
        StatusTone = tone;
        StatusGlyph = glyph;
    }
}
