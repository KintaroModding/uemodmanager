using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using UEManager.Core;
using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <param name="ActionText">A button in the toast, such as "Show log"; null for none.</param>
public sealed record ToastMessage(string Text, bool IsError, string? ActionText = null)
{
    public string Tone => IsError ? "bad" : "good";
}

/// <summary>A save backup in Settings: its file, and when it was made and how big it is.</summary>
public sealed record SaveBackupItem(string Path, string Text);

public sealed partial class MainViewModel : LocalizedObservableObject
{
    private readonly AppPaths paths;
    private readonly AppSettings settings;
    private readonly DispatcherTimer toastTimer = new();
    private Action? toastAction;

    /// <summary>Looks every few seconds whether the game runs, to read UE4SS's log once it closed.</summary>
    private readonly DispatcherTimer gameWatch = new() { Interval = TimeSpan.FromSeconds(5) };
    private DateTime? gameRunningSince;
    private bool watching;

    /// <summary>
    /// The app's only connection: the UE4SS manifest and the update check at start, the openmods
    /// lists and covers when the openmods page opens, and the downloads the user starts.
    /// </summary>
    private static readonly HttpClient Http = CreateHttpClient();
    private ModManager? manager;
    private Ue4ssRuntime? ue4ss;
    private string? installedUe4ssVersion;
    private Ue4ssRelease? latestUe4ss;

    /// <summary>Where the UE4SS manifest is read: the game profile's, or a test copy given on the command line.</summary>
    private readonly string? ue4ssManifestUrl;

    /// <summary>
    /// The installed UE4SS files that differ from every official build; empty for an official build, null while
    /// unknown: no manifest list yet, or no UE4SS.
    /// </summary>
    private IReadOnlyList<string>? unofficialUe4ssFiles;

    /// <summary>The build before a game update the player has not confirmed yet; null when there is none.</summary>
    private string? previousBuild;

    /// <param name="catalogUrl">Another openmods games list, such as a test copy; used only while the catalog is switched on.</param>
    /// <param name="updateUrl">Another release list for the update check, such as a test copy.</param>
    /// <param name="ue4ssManifestUrl">Another UE4SS manifest, such as a test copy.</param>
    public MainViewModel(GameProfile profile, AppPaths paths, ArtSet art, string? catalogUrl = null, string? updateUrl = null,
        string? ue4ssManifestUrl = null)
    {
        Profile = profile;
        this.paths = paths;
        this.ue4ssManifestUrl = ue4ssManifestUrl ?? profile.Ue4ssManifestUrl;
        Art = art;
        settings = AppSettings.Load(paths);
        CreateCatalog(catalogUrl);
        Update = new AppUpdateViewModel(this, Http, paths, updateUrl ?? AppInfo.LatestReleaseUrl,
            $"{AppInfo.ReleaseFilePrefix}_{profile.Id}_");
        Ue4ssLogPanel = new Ue4ssLogViewModel(this);
        ModsView = CollectionViewSource.GetDefaultView(Mods);
        ModsView.Filter = item => item is ModItemViewModel mod && mod.Matches(SearchText);
        sortField = Enum.TryParse<ModSortField>(settings.ModsSort, out var field) ? field : ModSortField.Name;
        sortDescending = settings.ModsSortDescending;
        ApplySort();
        toastTimer.Tick += (_, _) => DismissToast();
        gameWatch.Tick += async (_, _) => await WatchGameAsync();
    }

    /// <summary>
    /// One look at the game: when it ran and runs no more, however it was started, UE4SS's log of that game start
    /// is read, and errors that name a mod the manager installed are reported, with the way to the log.
    /// </summary>
    private async Task WatchGameAsync()
    {
        if (watching || Game is not { } game)
            return;
        watching = true;
        try
        {
            var running = await Task.Run(() => GameProcess.IsRunning(game));
            if (running)
                gameRunningSince ??= DateTime.Now;
            else if (gameRunningSince is { } since)
            {
                gameRunningSince = null;
                await ReportUe4ssErrorsAsync(game, since);
            }
        }
        finally
        {
            watching = false;
        }
    }

    private async Task ReportUe4ssErrorsAsync(GameInstall game, DateTime since)
    {
        if (await ReadUe4ssErrorsAsync(game) is not { } read)
            return;
        var (log, named) = read;
        // A log older than this game start comes from another one: UE4SS did not run this time.
        if (log.WrittenAt < since.AddMinutes(-1) || named.Count == 0)
            return;
        var mods = string.Join(", ", manager!.Mods.Where(m => Ue4ssFolders(m).Any(named.Contains)).Select(m => m.Name));
        Log.Info($"After the game closed, UE4SS's log names these mods in errors: {mods}.");
        ShowToast(L.F($"The game closed, and UE4SS reported errors in these mods: {mods}."), isError: true,
            L.T("Show log"), () => SelectedPage = "Ue4ss");
        if (SelectedPage == "Ue4ss")
            await RefreshUe4ssLogAsync();
    }

    /// <summary>
    /// The UE4SS mod folders that errors named in UE4SS's log of the last game start, and when UE4SS last wrote
    /// that log; null before the log was read, or without one.
    /// </summary>
    private (IReadOnlySet<string> Folders, DateTime WrittenAt)? ue4ssErrors;

    /// <summary>
    /// Reads UE4SS's log for errors that name a UE4SS mod the manager installed, and marks those mods in the list.
    /// UE4SS's own mods, such as BPModLoaderMod, are not ours to mark.
    /// </summary>
    /// <returns>The log and the folders its errors name; null without a log, or when it could not be read.</returns>
    private async Task<(Ue4ssLog Log, IReadOnlySet<string> Named)?> ReadUe4ssErrorsAsync(GameInstall game)
    {
        if (manager is null || Ue4ssRuntime.Detect(game) is not { Installed: true } runtime)
            return null;
        var folders = manager.Mods.SelectMany(Ue4ssFolders).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (folders.Count == 0)
            return null;
        Ue4ssLog? log;
        IReadOnlySet<string> named;
        try
        {
            (log, named) = await Task.Run(() =>
            {
                var read = Ue4ssLog.Read(runtime.LogPath);
                return (read, (IReadOnlySet<string>)(read?.ModsNamedInErrors(folders) ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase));
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"UE4SS.log could not be read for the mods' errors: {ex.Message}");
            return null;
        }
        // Another game was chosen meanwhile.
        if (Game != game)
            return null;
        var marked = ue4ssErrors is { Folders.Count: > 0 };
        // Without a log, as after Clear on the UE4SS page, no mod is marked.
        ue4ssErrors = log is null ? null : (named, log.WrittenAt);
        if (marked || named.Count > 0)
            RefreshMods();
        return log is null ? null : (log, named);
    }

    /// <summary>The folders a mod placed in UE4SS's Mods folder; none for a pak mod. The shared folder is UE4SS's.</summary>
    private static IEnumerable<string> Ue4ssFolders(ModRecord mod) => mod.Files
        .Where(f => f.Target == DeployTarget.Ue4ssMods && !f.IsShared)
        .Select(f => f.FileName.Split('/')[0])
        .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether UE4SS reported errors in this enabled mod at the last game start. A mod installed or updated after
    /// that start is not marked: the errors came from what it replaced.
    /// </summary>
    internal bool HadUe4ssErrors(ModRecord mod) =>
        mod.Enabled && ue4ssErrors is { } errors && mod.InstalledAt.LocalDateTime <= errors.WrittenAt
        && Ue4ssFolders(mod).Any(errors.Folders.Contains);

    /// <summary>The UE4SS page, with the log searched for the mod's folder, so its errors show with the lines around them.</summary>
    internal void ShowUe4ssErrors(ModRecord mod)
    {
        Ue4ssLogPanel.SearchText = Ue4ssFolders(mod).FirstOrDefault(folder => ue4ssErrors?.Folders.Contains(folder) == true) ?? "";
        Ue4ssLogPanel.OnlyProblems = false;
        SelectedPage = "Ue4ss";
    }

    // Hooks for the catalog, implemented in Catalog\MainViewModel.Catalog.cs. While the catalog is
    // switched off (AppInfo.IsCatalogOn), there is no catalog view model, so they do nothing.
    partial void CreateCatalog(string? catalogUrl);

    /// <summary>The installed mods or UE4SS changed.</summary>
    partial void OnModsChanged();

    public GameProfile Profile { get; }

    public string GameDisplayName => L.T(Profile.DisplayName);

    public string GameTagline => L.T(Profile.Tagline ?? "");

    public IReadOnlyList<LanguageOption> Languages => L.Current.Languages;

    public string SelectedLanguage
    {
        get => L.Current.Language;
        set
        {
            if (value == L.Current.Language || Languages.All(language => language.Code != value))
                return;
            settings.Language = value;
            L.Current.SetLanguage(value);
            SaveSettings();
        }
    }

    protected override void RefreshLanguage()
    {
        DismissToast();
        OnModsChanged();
        ModsView.Refresh();
        base.RefreshLanguage();
    }

    public ArtSet Art { get; }

    public IReadOnlyList<LicenseNotice> Licenses { get; } = LicenseNotices.Load();

    /// <summary>The app's own license (GPL-3.0) and its additional terms.</summary>
    public IReadOnlyList<LicenseNotice> OwnLicenses { get; } = LicenseNotices.LoadOwn();

    /// <summary>Everyone in CONTRIBUTORS.md, named on the About page.</summary>
    public IReadOnlyList<string> Contributors { get; } = LicenseNotices.Contributors();

    /// <summary>The version check at start, and the update window.</summary>
    public AppUpdateViewModel Update { get; }

    /// <summary>The UE4SS log on the UE4SS page.</summary>
    public Ue4ssLogViewModel Ue4ssLogPanel { get; }

    [ObservableProperty]
    private GameInstall? game;

    [ObservableProperty]
    private string selectedPage = "Home";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string busyText = "";

    [ObservableProperty]
    private bool isDragOver;

    [ObservableProperty]
    private ToastMessage? toast;

    [ObservableProperty]
    private InstallDialogViewModel? installDialog;

    [ObservableProperty]
    private Ue4ssGuideViewModel? ue4ssGuide;

    [ObservableProperty]
    private ConfirmViewModel? confirm;

    [ObservableProperty]
    private HelpReportViewModel? helpDialog;

    [ObservableProperty]
    private SettingsEditorViewModel? settingsEditor;

    [ObservableProperty]
    private ModDetailsViewModel? modDetails;

    [ObservableProperty]
    private ModSetsViewModel? modSets;

    [ObservableProperty]
    private ResetModdingViewModel? resetDialog;

    [ObservableProperty]
    private string searchText = "";

    [ObservableProperty]
    private int unmanagedCount;

    public ObservableCollection<ModItemViewModel> Mods { get; } = [];

    public ICollectionView ModsView { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameSortGlyph), nameof(InstalledSortGlyph))]
    private ModSortField sortField;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameSortGlyph), nameof(InstalledSortGlyph))]
    private bool sortDescending;

    /// <summary>The glyph next to each sortable column header.</summary>
    public string NameSortGlyph => SortGlyph(ModSortField.Name);

    public string InstalledSortGlyph => SortGlyph(ModSortField.Installed);

    public string AppName => AppInfo.Name;

    /// <summary>The taskbar title; the game tells builds for different games apart.</summary>
    public string WindowTitle => $"{AppInfo.Name} · {GameDisplayName}";

    public string AppVersionText => $"{AppInfo.Name} {AppInfo.Version}";

    /// <summary>A pre-release build: the yellow box below About, which explains what that means.</summary>
    public bool IsPreRelease => AppInfo.IsPreRelease;

    [ObservableProperty]
    private bool isPreReleaseDialogOpen;

    public bool IsGameFound => Game is not null;

    public string GameStatusText => IsGameFound ? L.T("Game connected") : L.T("Game not found");

    public string NotFoundHint =>
        L.F($"{GameDisplayName} was not found in Steam or the Epic Games Launcher. Choose its install folder, or search again.");

    public string StoreText => Game?.Store switch
    {
        GameStore.Steam => "Steam",
        GameStore.Epic => "Epic Games",
        GameStore.Manual => L.T("Folder"),
        _ => "—",
    };

    public string StoreDetail => Game?.Store switch
    {
        GameStore.Steam or GameStore.Epic => L.T("Found automatically"),
        GameStore.Manual => L.T("Chosen by hand"),
        _ => L.T("Not found yet"),
    };

    public string BuildText => Game?.BuildId ?? "—";

    /// <summary>Save backups need the game profile to name the save folder; see <see cref="SaveBackups.IsAvailable"/>.</summary>
    public bool SaveBackupsAvailable => SaveBackups.IsAvailable(Profile);

    /// <summary>Why the Save backups card is off; null while it is on.</summary>
    public string? SaveBackupsHint => SaveBackupsAvailable
        ? null
        : L.F($"Coming in the next update. Save backups need the exact save folder of {GameDisplayName}, which is not known for sure yet.");

    /// <summary>Where the saves are: the player's choice, else the game profile's; null while save backups are not available.</summary>
    private string? SaveFolder => SaveBackups.Folder(Profile, Game, settings.SaveFolders.GetValueOrDefault(Profile.Id));

    public string SaveFolderText => SaveFolder ?? (SaveBackupsAvailable ? L.T("Not found yet") : L.T("Not known yet"));

    public string SaveFolderDetail => SaveFolder switch
    {
        null when SaveBackupsAvailable => L.F($"It is in the game folder. Locate {GameDisplayName} first."),
        null => L.T("Save backups come in the next update, once this folder is known for sure."),
        var folder when !Directory.Exists(folder) => L.T("This folder does not exist yet. The game makes it at its first save."),
        _ when settings.SaveFolders.ContainsKey(Profile.Id) => L.T("Chosen by hand"),
        _ => L.T("From the game profile"),
    };

    public string SaveFolderTone => SaveFolder is { } folder && !Directory.Exists(folder) ? "warn" : "";

    public bool BackUpSavesOnLaunch
    {
        get => settings.BackUpSavesOnLaunch;
        set
        {
            settings.BackUpSavesOnLaunch = value;
            SaveSettings();
            OnPropertyChanged();
        }
    }

    /// <summary>The save backups, newest first, for Restore in Settings.</summary>
    public IReadOnlyList<SaveBackupItem> SaveBackupList => SaveBackups.List(paths.SaveBackupDir(Profile.Id))
        .Select(path => new SaveBackupItem(path, $"{File.GetLastWriteTime(path):G}  ·  {Format.Size(new FileInfo(path).Length)}"))
        .ToList();

    public string SaveBackupsText
    {
        get
        {
            var backups = SaveBackups.List(paths.SaveBackupDir(Profile.Id));
            return backups.Count == 0
                ? L.T("None yet")
                : L.F($"{backups.Count} of {SaveBackups.Keep} kept  ·  newest {File.GetLastWriteTime(backups[0]):g}");
        }
    }

    public bool IsGameUpdated => previousBuild is not null;

    public string GameUpdatedText => L.Join(
        Game?.Store == GameStore.Epic
            ? L.F($"{GameDisplayName} was updated from version {previousBuild} to {Game?.BuildId}.")
            : L.F($"{GameDisplayName} was updated from build {previousBuild} to {Game?.BuildId}."),
        L.T("A game update can break mods until their authors update them. If the game crashes or acts strangely, disable mods one by one to find the one that needs an update."));

    public string BuildDetail => Game?.Store switch
    {
        GameStore.Steam => L.T("Steam build id"),
        GameStore.Epic => L.T("Epic version"),
        _ => L.T("Not known for this install"),
    };

    public string GamePath => Game?.RootPath ?? L.T("Not set");

    public string ModsPath => Game?.ModsPath ?? "—";

    public string PaksPath => Game?.PaksPath ?? "—";

    public string LogicModsPath => Game?.LogicModsPath ?? "—";

    /// <summary>The game's real exe, which UE4SS is installed next to.</summary>
    public string GameExeText => Game is null ? "—" : Game.FindShippingExecutable() ?? L.F($"Not found in {Game.BinariesPath}");

    /// <summary>The version the manager installed is exact; otherwise the one UE4SS wrote into its log.</summary>
    public string Ue4ssText => ue4ss switch
    {
        null => "—",
        { Installed: false } => L.T("Not installed"),
        _ when (installedUe4ssVersion ?? ue4ss.Version) is { } version => L.F($"Installed  ·  {version}"),
        _ => L.T("Installed"),
    };

    public bool IsUe4ssInstalled => ue4ss is { Installed: true };

    /// <summary>The manifest names a UE4SS release other than the installed one (or the installed one is unknown).</summary>
    public bool IsUe4ssUpdateAvailable => IsUe4ssInstalled && Ue4ssManifest.IsUpdate(installedUe4ssVersion, latestUe4ss);

    public string Ue4ssUpdateText => latestUe4ss is null ? "" : L.F($"Update to {latestUe4ss.Version}");

    /// <summary>What one-click install uses: the manifest's release, else the fixed one in the game profile.</summary>
    public Ue4ssRelease? AvailableUe4ss => latestUe4ss
        ?? (string.IsNullOrWhiteSpace(Profile.Ue4ssPackageUrl)
            ? null
            : new Ue4ssRelease(Ue4ssManifest.VersionFromFileName(Profile.Ue4ssPackageUrl) ?? "", Profile.Ue4ssPackageUrl, Profile.Ue4ssPackageSha256));

    public string Ue4ssModsPath => ue4ss switch
    {
        null => "—",
        { Installed: false } => L.T("UE4SS is not installed"),
        _ => ue4ss.ModsPath,
    };

    public string Ue4ssFolderPath => ue4ss is { Installed: true } ? ue4ss.Folder : "—";

    /// <summary>How many installed mods are Lua, C++ or Blueprint mods, which only run with UE4SS.</summary>
    public string Ue4ssModsText
    {
        get
        {
            var needing = Mods.Count(m => m.NeedsUe4ss);
            var enabled = Mods.Count(m => m.NeedsUe4ss && m.Enabled);
            return needing == 0
                ? L.T("None of your installed mods needs UE4SS.")
                : L.Plural(needing, "{0} installed mod needs UE4SS; {1} enabled.", "{0} installed mods need UE4SS; {1} enabled.", enabled);
        }
    }

    public string ProjectText => Game switch
    {
        null => "—",
        { ProjectFolder: null } => L.T("Not used by this profile"),
        _ when string.Equals(Game.ProjectFolder, Profile.ProjectFolder, StringComparison.OrdinalIgnoreCase) => Game.ProjectFolder,
        _ => L.F($"{Game.ProjectFolder}  (detected; the profile says \"{Profile.ProjectFolder}\")"),
    };

    public string DataPath => paths.Root;

    public int InstalledCount => Mods.Count;

    public string EnabledText => L.F($"{Mods.Count(m => m.Enabled)} enabled");

    public string ModsCountText => L.F($"{ModsView.Cast<object>().Count()} of {Mods.Count} shown");

    public bool ShowEmpty => ModsView.IsEmpty;

    public string EmptyTitle => Mods.Count == 0 ? L.T("No mods installed yet") : L.T("No mods match the search");

    public string EmptyHint => Mods.Count == 0
        ? L.T("Drop a download anywhere on this window, or install from an archive or a folder.")
        : L.T("Try another name, version or file name.");

    public bool HasUnmanaged => UnmanagedCount > 0;

    /// <summary>How many mods a test without mods turned off; 0 when no such test is on.</summary>
    public int TestOffCount => manager?.OffForTest.Count ?? 0;

    public bool IsTestingWithoutMods => TestOffCount > 0;

    public bool CanTestWithoutMods => !IsTestingWithoutMods && Mods.Any(m => m.Enabled);

    public string TestText => L.Plural(TestOffCount,
        "Testing without mods: {0} mod is turned off.", "Testing without mods: {0} mods are turned off.");

    /// <summary>Turns every mod off and remembers which ones, to see whether the game runs without them.</summary>
    [RelayCommand]
    private async Task TestWithoutMods()
    {
        if (!EnsureGame())
            return;
        var count = 0;
        if (await RunAsync(L.T("Turning mods off…"), async () => count = await Task.Run(manager!.TurnOffForTest)))
            ShowToast(L.Plural(count, "{0} mod is off. Start the game to test it without mods.", "{0} mods are off. Start the game to test it without mods."));
        RefreshMods();
    }

    /// <summary>Turns the mods a test without mods turned off on again.</summary>
    [RelayCommand]
    private async Task TurnModsBackOn()
    {
        if (!EnsureGame())
            return;
        var count = 0;
        if (await RunAsync(L.T("Turning mods back on…"), async () => count = await Task.Run(manager!.TurnBackOn)))
            ShowToast(L.Plural(count, "{0} mod is on again.", "{0} mods are on again."));
        RefreshMods();
    }

    /// <summary>The game is known but UE4SS is not in it, so the install guide is offered.</summary>
    public bool IsUe4ssMissing => ue4ss is { Installed: false };

    public string UnmanagedText => L.Plural(UnmanagedCount,
        "{0} file in the mods folder was not installed by the manager", "{0} files in the mods folder were not installed by the manager");

    public async Task InitializeAsync(IReadOnlyList<string> downloads)
    {
        if (settings.Language is null && L.Current.Offer() is { } offered)
            await OfferLanguageAsync(offered);
        ModManager.ClearStaging(paths);
        GameInstall? found = null;
        await RunAsync(L.T("Looking for the game…"), async () => found = await Task.Run(FindGame));
        await SetGameAsync(found);
        gameWatch.Start();
        _ = CheckUe4ssManifestAsync();
        _ = Update.CheckAsync();
        if (downloads.Count > 0)
            await InstallAsync(downloads);
    }

    /// <summary>
    /// Asks a player who chose no language yet whether to switch to the language of Windows. The question is in
    /// that language and in English; Yes and No stay English, which everyone reads. Either answer is saved, so
    /// the question comes once.
    /// </summary>
    internal async Task OfferLanguageAsync(LanguageOption offered)
    {
        var accepted = await AskAsync(L.Both(offered, "Language", "  ·  "),
            L.Both(offered, "Switch the app to {0}?") + "\n\n" + L.Both(offered, "You can change this later in Settings ▸ Language."),
            "Yes", "No");
        if (accepted)
        {
            SelectedLanguage = offered.Code;
            return;
        }
        settings.Language = L.English;
        SaveSettings();
    }

    /// <summary>Installs downloads one after another, each through the review dialog.</summary>
    public async Task InstallAsync(IEnumerable<string> sources)
    {
        if (IsBusy || InstallDialog is not null)
        {
            ShowToast(L.T("Finish the current step first, then try again."), isError: true);
            return;
        }
        if (!EnsureGame())
            return;
        foreach (var source in sources)
        {
            if (!ArchiveStager.IsSupported(source))
            {
                ShowToast(L.F($"'{Path.GetFileName(source)}' is not a mod file this manager can install."), isError: true);
                continue;
            }

            InstallPreview? preview = null;
            await RunAsync(L.F($"Reading {Path.GetFileName(source)}…"),
                async () => preview = await Task.Run(() => manager!.Inspect(source)));
            if (preview is null)
                continue;

            // UE4SS itself, dropped like a mod: offer to install it where it belongs.
            if (preview.ContainsUe4ssRuntime)
            {
                preview.Dispose();
                if (await AskAsync(L.T("Install UE4SS"),
                        L.F($"\"{Path.GetFileName(source)}\" is UE4SS itself, not a mod. Install it into {GameDisplayName}? Your existing UE4SS settings are kept."), L.T("Install UE4SS")))
                    await InstallUe4ssAsync(source);
                continue;
            }

            using (preview)
            {
                var choice = await ReviewAsync(preview, catalog: null);
                if (choice is not null)
                    await InstallChoiceAsync(preview, choice, showMods: true);
            }
        }
    }

    internal async Task SetEnabledAsync(ModItemViewModel item, bool enabled)
    {
        await RunAsync(enabled ? L.T("Enabling…") : L.T("Disabling…"), () => Task.Run(() => manager!.SetEnabled(item.Id, enabled)));
        RefreshMods();
    }

    internal async Task UninstallAsync(ModItemViewModel item)
    {
        if (!await AskAsync(L.T("Uninstall mod"),
                L.Join(L.F($"Remove \"{item.Name}\" from the game and from the manager's library? The original download is not touched."),
                    item.HasSettings ? L.T("Settings you changed are backed up first.") : null),
                L.T("Uninstall")))
            return;
        string? backup = null;
        if (await RunAsync(L.T("Uninstalling…"), async () => backup = await Task.Run(() => manager!.Uninstall(item.Id))))
            ShowToast(backup is null
                ? L.F($"Uninstalled \"{item.Name}\".")
                : L.F($"Uninstalled \"{item.Name}\". The settings you changed were backed up to {backup}."));
        RefreshMods();
    }

    /// <summary>Opens the mod's settings files in the game in the app's own settings editor.</summary>
    internal void EditSettings(ModItemViewModel item)
    {
        if (item.DeployedSettingsFiles is { Count: > 0 } files)
            SettingsEditor = new SettingsEditorViewModel(this, item.Name, files);
    }

    internal void CloseSettingsEditor() => SettingsEditor = null;

    /// <summary>Shows a mod's details: where it came from, and each file with its place in the game and its state.</summary>
    internal void ShowModDetails(ModRecord mod)
    {
        if (manager is null || Game is null)
            return;
        var files = mod.Files.Select(file =>
        {
            var path = file.DeployedPath ?? Path.Combine(manager.TargetFolder(file.Target), file.FileName.Replace('/', Path.DirectorySeparatorChar));
            var (state, tone) = ModDetailsViewModel.StateOf(mod, file);
            return new ModFileItem(Path.GetRelativePath(Game.RootPath, path), path, Format.Size(file.Size), state, tone);
        }).ToList();
        ModDetails = new ModDetailsViewModel(this, mod, files);
    }

    internal void CloseModDetails() => ModDetails = null;

    /// <summary>Opens the player's mod sets.</summary>
    [RelayCommand]
    private void ShowModSets()
    {
        if (!EnsureGame())
            return;
        ModSets = new ModSetsViewModel(this);
        ModSets.Load(manager!.Sets, manager.Mods);
    }

    internal void CloseModSets() => ModSets = null;

    /// <summary>Saves the mods that are on now as a set, in place of a set with the same name.</summary>
    /// <returns>Whether the set was saved.</returns>
    internal async Task<bool> SaveModSetAsync(string name)
    {
        ModSet? set = null;
        var saved = await RunAsync(L.T("Saving the set…"), async () => set = await Task.Run(() => manager!.SaveSet(name)));
        if (saved)
            ShowToast(L.F($"Set \"{set!.Name}\" saved."));
        ModSets?.Load(manager!.Sets, manager.Mods);
        return saved;
    }

    /// <summary>Turns on exactly the mods of a set and closes the sets.</summary>
    internal async Task ApplyModSetAsync(string name)
    {
        (int On, int Off) changed = default;
        if (await RunAsync(L.F($"Turning on the set \"{name}\"…"), async () => changed = await Task.Run(() => manager!.ApplySet(name))))
        {
            ModSets = null;
            ShowToast(changed is (0, 0)
                ? L.F($"Set \"{name}\" was already in use.")
                : L.F($"Set \"{name}\" is in use. Turned on: {changed.On}. Turned off: {changed.Off}."));
        }
        RefreshMods();
        ModSets?.Load(manager!.Sets, manager.Mods);
    }

    internal async Task DeleteModSetAsync(string name)
    {
        await RunAsync(L.T("Deleting the set…"), () => Task.Run(() => manager!.DeleteSet(name)));
        ModSets?.Load(manager!.Sets, manager.Mods);
    }

    internal async Task ResetSettingsAsync(ModItemViewModel item)
    {
        if (!await AskAsync(L.T("Reset settings"),
                L.F($"Put back the settings \"{item.Name}\" came with? The settings you changed are backed up first.\n\nClose the game first."),
                L.T("Reset settings")))
            return;
        string? backup = null;
        var reset = await RunAsync(L.T("Resetting settings…"), async () => backup = await Task.Run(() => manager!.ResetSettings(item.Id)));
        RefreshMods();
        if (reset)
            ShowToast(backup is null
                ? L.F($"The settings of \"{item.Name}\" are already the ones it came with.")
                : L.F($"Reset the settings of \"{item.Name}\". Your version was backed up to {backup}."));
    }

    /// <summary>Shows the install review and waits for the user; null when cancelled.</summary>
    private async Task<InstallChoice?> ReviewAsync(InstallPreview preview, CatalogSource? catalog)
    {
        var dialog = new InstallDialogViewModel(manager!, preview, ShowUe4ssGuide, catalog);
        InstallDialog = dialog;
        var choice = await dialog.Result;
        InstallDialog = null;
        return choice;
    }

    /// <param name="showMods">Switch to the Mods page afterwards; the openmods page stays where it is.</param>
    private async Task InstallChoiceAsync(InstallPreview preview, InstallChoice choice, bool showMods)
    {
        ModRecord? installed = null;
        await RunAsync(L.F($"Installing {choice.Name}…"), async () => installed = await Task.Run(() =>
            manager!.Install(preview, choice.Packages, choice.Name, choice.Version, choice.Replace, choice.CatalogId, choice.KeepSettings)));
        if (installed is null)
            return;
        RefreshMods();
        if (showMods)
            SelectedPage = "Mods";
        ShowToast(!choice.Replace ? L.F($"Installed \"{installed.Name}\".")
            : installed.Version is null ? L.F($"Updated \"{installed.Name}\".")
            : L.F($"Updated \"{installed.Name}\" to version {installed.Version}."));
    }

    internal void OpenPath(string path) => TryOpen(path);

    internal void CloseUe4ssGuide() => Ue4ssGuide = null;

    internal async Task InstallUe4ssFromFileAsync()
    {
        if (!EnsureGame())
            return;
        var dialog = new OpenFileDialog
        {
            Title = L.T("Choose the downloaded UE4SS release"),
            Filter = L.T("UE4SS release (*.zip, *.7z)|*.zip;*.7z"),
        };
        if (dialog.ShowDialog() == true)
            await InstallUe4ssAsync(dialog.FileName);
    }

    /// <summary>Downloads the available UE4SS release (manifest first, game profile second), checks it, and installs it.</summary>
    internal async Task DownloadAndInstallUe4ssAsync()
    {
        if (!EnsureGame() || AvailableUe4ss is not { } release)
            return;
        var folder = Path.Combine(paths.StagingDir, "download-" + Guid.NewGuid().ToString("N"));
        Ue4ssInstallResult? result = null;
        try
        {
            await RunAsync(L.T("Downloading UE4SS…"), async () =>
            {
                var progress = new Progress<double>(share => BusyText = L.F($"Downloading UE4SS…  {share:P0}"));
                var file = await Ue4ssDownloader.DownloadAsync(Http, release.Url, release.Sha256, folder, progress);
                BusyText = L.T("Installing UE4SS…");
                var version = string.IsNullOrEmpty(release.Version) ? null : release.Version;
                result = await Task.Run(() => manager!.InstallUe4ss(file, version));
            });
        }
        finally
        {
            TryDeleteFolder(folder);
        }
        if (result is not null)
            Ue4ssInstalled(result);
    }

    private async Task InstallUe4ssAsync(string package)
    {
        Ue4ssInstallResult? result = null;
        await RunAsync(L.T("Installing UE4SS…"), async () => result = await Task.Run(() => manager!.InstallUe4ss(package)));
        if (result is not null)
            Ue4ssInstalled(result);
    }

    /// <summary>Reads the online manifest once per start. Offline or failing, it stays quiet.</summary>
    private async Task CheckUe4ssManifestAsync()
    {
        if (string.IsNullOrWhiteSpace(ue4ssManifestUrl))
            return;
        latestUe4ss = await Ue4ssManifest.FetchAsync(Http, ue4ssManifestUrl, Profile.Id);
        if (latestUe4ss is null)
            return;
        OnModsChanged();
        OnPropertyChanged(string.Empty);
        await CheckUe4ssBuildAsync();
    }

    /// <summary>The box on Home: UE4SS files differ from the official build.</summary>
    public bool HasUnofficialUe4ss => IsUe4ssInstalled && unofficialUe4ssFiles is { Count: > 0 };

    /// <summary>
    /// Compares the installed UE4SS with the official builds the manifest lists, off the UI thread: UE4SS.dll alone
    /// is several megabytes. Without the manifest's list, or without UE4SS, nothing is said.
    /// </summary>
    private async Task CheckUe4ssBuildAsync()
    {
        IReadOnlyList<string>? differing = null;
        if (Game is { } game && latestUe4ss?.Files is { } official && Ue4ssRuntime.Detect(game).Installed)
        {
            try
            {
                differing = await Task.Run(() => Ue4ssBuild.DifferingFiles(game.BinariesPath, official));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"The UE4SS files could not be compared with the official build: {ex.Message}");
            }
            // Another game was chosen meanwhile.
            if (Game != game)
                return;
            if (differing is { Count: > 0 })
                Log.Info($"UE4SS differs from the official build in: {string.Join(", ", differing)}.");
        }
        unofficialUe4ssFiles = differing;
        OnPropertyChanged(nameof(HasUnofficialUe4ss));
    }

    /// <summary>The box on Home: which UE4SS files differ from the official build, and the way to the official one.</summary>
    [RelayCommand]
    private async Task ShowUnofficialUe4ss()
    {
        const int shown = 8;
        var files = unofficialUe4ssFiles ?? [];
        var list = string.Join("\n", files.Take(shown).Select(file => "•  " + file));
        if (files.Count > shown)
            list += "\n" + L.Plural(files.Count - shown, "…and {0} more file", "…and {0} more files");
        if (await AskAsync(L.T("Unofficial UE4SS build"),
                L.F($"These UE4SS files differ from the official build for {GameDisplayName}:\n\n{list}\n\nMods are made and tested with the official build, so some may not work with this one. Installing the official build replaces these files. Your UE4SS settings and your mods stay."),
                L.T("Install the official build"), L.T("Close")))
            await DownloadAndInstallUe4ssAsync();
    }

    [RelayCommand]
    private Task UpdateUe4ss() => DownloadAndInstallUe4ssAsync();

    [RelayCommand]
    private async Task RemoveUe4ss()
    {
        if (!EnsureGame() || !await AskAsync(L.T("Remove UE4SS"),
                L.F($"This removes UE4SS from {GameDisplayName} completely: dwmapi.dll and the whole ue4ss folder, with UE4SS's settings and every mod inside it, also mods installed by hand.\n\nMods in your list that need UE4SS are disabled. Their copies stay in the library, so you can enable them again after installing UE4SS.\n\nClose the game first."),
                L.T("Remove UE4SS")))
            return;
        Ue4ssRemoveResult? result = null;
        await RunAsync(L.T("Removing UE4SS…"), async () => result = await Task.Run(() => manager!.RemoveUe4ss()));
        unofficialUe4ssFiles = null;
        RefreshMods();
        if (result is not null)
            ShowToast(result.DisabledMods switch
            {
                0 => L.T("UE4SS removed."),
                1 => L.T("UE4SS removed. 1 mod was disabled and stays in the library."),
                var count => L.F($"UE4SS removed. {count} mods were disabled and stay in the library."),
            });
    }

    /// <summary>
    /// Takes the game back to its original files after a question that shows what changes. The player may
    /// also delete the mods from the manager, and reset the app's settings, after which the app starts again.
    /// </summary>
    [RelayCommand]
    private async Task ResetModding()
    {
        if (!EnsureGame())
            return;
        ResetPreview? preview = null;
        if (!await RunAsync(L.T("Looking at the game…"), async () => preview = await Task.Run(() => manager!.PreviewReset())))
            return;
        var dialog = new ResetModdingViewModel(GameDisplayName, preview!);
        ResetDialog = dialog;
        var accepted = await dialog.Result;
        ResetDialog = null;
        if (!accepted)
            return;

        ResetResult? result = null;
        var done = await RunAsync(L.T("Resetting modding…"), async () => result = await Task.Run(() => manager!.ResetModding(dialog.DeleteMods)));
        RefreshMods();
        if (!done)
            return;
        if (dialog.ResetSettings)
        {
            ResetAppSettings();
            return;
        }
        var parts = new List<string>();
        if (result!.ModsTurnedOff > 0)
            parts.Add(L.Plural(result.ModsTurnedOff, "{0} mod turned off", "{0} mods turned off"));
        if (result.Ue4ssRemoved)
            parts.Add(L.T("UE4SS removed"));
        if (result.FilesMovedAside > 0)
            parts.Add(L.Plural(result.FilesMovedAside, "{0} other file moved to a backup folder", "{0} other files moved to a backup folder"));
        if (result.ModsDeleted > 0)
            parts.Add(L.Plural(result.ModsDeleted, "{0} mod deleted from the manager", "{0} mods deleted from the manager"));
        ShowToast(parts.Count == 0
            ? L.F($"{GameDisplayName} had no mods to remove.")
            : L.F($"Modding reset: {string.Join(", ", parts)}. {GameDisplayName} is back to its original files."));
    }

    /// <summary>Puts every app setting back to its default and starts the app again, so nothing of the old ones stays.</summary>
    private void ResetAppSettings()
    {
        try
        {
            File.Delete(paths.SettingsFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("The app's settings could not be reset.", ex);
            ShowToast(L.F($"Modding is reset, but the app's settings could not be reset: {ex.Message}"), isError: true);
            return;
        }
        Log.Info("Reset the app's settings; starting again.");
        if (!App.Restart())
            ShowToast(L.T("Modding and the app's settings are reset. Close the app and start it again."));
    }

    private void Ue4ssInstalled(Ue4ssInstallResult result)
    {
        Ue4ssGuide = null;
        RefreshMods();
        _ = CheckUe4ssBuildAsync();
        ShowToast(L.Join(
            result.Updated ? L.T("UE4SS updated.") : L.T("UE4SS installed."),
            result.RemovedFiles.Count == 0 ? null : L.Plural(result.RemovedFiles.Count, "{0} old file was removed.", "{0} old files were removed."),
            result.KeptFiles.Count == 0 ? null : L.Plural(result.KeptFiles.Count, "{0} existing file was kept.", "{0} existing files were kept."),
            installedUe4ssVersion is null ? L.T("Start the game once to show its version.") : null));
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"UEModManager/{AppInfo.Version}");
        return http;
    }

    internal static void TryDeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Folder '{folder}' could not be removed: {ex.Message}");
        }
    }

    /// <summary>Looks for UE4SS again after the user installed it by hand.</summary>
    internal void CheckUe4ss()
    {
        RefreshMods();
        if (ue4ss is { Installed: true })
        {
            Ue4ssGuide = null;
            ShowToast(ue4ss.Version is null ? L.T("UE4SS found.") : L.F($"UE4SS found: {ue4ss.Version}."));
        }
        else if (Game is not null)
        {
            ShowToast(L.F($"UE4SS was not found yet. UE4SS.dll belongs in {Path.Combine(Game.BinariesPath, "ue4ss")}."), isError: true);
        }
    }

    [RelayCommand]
    private void OpenUe4ssFolder()
    {
        if (ue4ss is { Installed: true })
            TryOpen(ue4ss.Folder);
    }

    /// <summary>Clear on the UE4SS page: deletes UE4SS's log, so its old lines, and the marks they caused, are gone.</summary>
    internal async Task ClearUe4ssLogAsync()
    {
        if (!EnsureGame())
            return;
        var game = Game!;
        await RunAsync(L.T("Clearing the log…"), () => Task.Run(() => manager!.ClearUe4ssLog()));
        await RefreshUe4ssLogAsync();
        await ReadUe4ssErrorsAsync(game);
    }

    /// <summary>Looks for UE4SS again and reads its log, as when the UE4SS page opens.</summary>
    internal Task RefreshUe4ssLogAsync()
    {
        ue4ss = Game is null ? null : Ue4ssRuntime.Detect(Game);
        return Ue4ssLogPanel.LoadAsync(ue4ss);
    }

    /// <summary>F5: checks the mods' files in the game again, and on the UE4SS page reads its log again.</summary>
    internal async Task ReloadAsync()
    {
        RefreshMods();
        await CheckUe4ssBuildAsync();
        if (SelectedPage == "Ue4ss")
            await RefreshUe4ssLogAsync();
    }

    partial void OnSelectedPageChanged(string? oldValue, string newValue)
    {
        if (newValue == "Ue4ss")
            _ = RefreshUe4ssLogAsync();
    }

    [RelayCommand]
    private void ShowUe4ssGuide()
    {
        if (Game is not null)
            Ue4ssGuide = new Ue4ssGuideViewModel(this, Game, AvailableUe4ss);
    }

    [RelayCommand]
    private async Task InstallFromArchive()
    {
        if (!EnsureGame())
            return;
        var dialog = new OpenFileDialog
        {
            Title = L.T("Choose mod archives"),
            Filter = L.T("Archives (*.7z, *.zip, *.rar)|*.7z;*.zip;*.rar"),
            Multiselect = true,
        };
        if (dialog.ShowDialog() == true)
            await InstallAsync(dialog.FileNames);
    }

    [RelayCommand]
    private async Task InstallFromFolder()
    {
        if (!EnsureGame())
            return;
        var dialog = new OpenFolderDialog
        {
            Title = L.T("Choose extracted mod folders"),
            Multiselect = true,
        };
        if (dialog.ShowDialog() == true)
            await InstallAsync(dialog.FolderNames);
    }

    [RelayCommand]
    private async Task LocateGame()
    {
        var dialog = new OpenFolderDialog { Title = L.F($"Choose the {GameDisplayName} install folder") };
        if (dialog.ShowDialog() != true)
            return;
        var install = GameLocator.FromFolder(Profile, dialog.FolderName);
        if (install is null)
        {
            var folders = Profile.ProjectFolder is { Length: > 0 } project ? L.F($"the Engine and {project} folders") : L.T("the Engine folder");
            ShowToast(L.F($"This does not look like the {GameDisplayName} install folder. Choose the game's main folder, the one that holds {folders}."),
                isError: true);
            return;
        }
        if (!await SetGameAsync(install, L.F($"Using {install.RootPath}.")))
            return;
        settings.GamePaths[Profile.Id] = install.RootPath;
        SaveSettings();
    }

    [RelayCommand]
    private async Task DetectGame()
    {
        GameInstall? found = null;
        await RunAsync(L.T("Searching Steam and Epic…"), async () => found = await Task.Run(() => GameLocator.Discover(Profile)));
        if (found is null)
        {
            ShowToast(L.F($"{GameDisplayName} was not found automatically. Choose its folder by hand."), isError: true);
            return;
        }
        var store = found.Store == GameStore.Epic ? "Epic Games" : "Steam";
        if (!await SetGameAsync(found, L.F($"Found {GameDisplayName} through {store}.")))
            return;
        settings.GamePaths.Remove(Profile.Id);
        SaveSettings();
    }

    /// <summary>
    /// Backs up the saves first, when the player wants that and the save folder is known. Without
    /// saves yet there is nothing to back up; when the backup fails, the player decides whether to play.
    /// </summary>
    [RelayCommand]
    private async Task LaunchGame()
    {
        if (!EnsureGame())
            return;
        string? note = null;
        if (settings.BackUpSavesOnLaunch && SaveFolder is { } saves)
        {
            string? backupError = null;
            try
            {
                IsBusy = true;
                BusyText = L.T("Backing up your saves…");
                var backup = await Task.Run(() => SaveBackups.HasSaves(saves)
                    ? SaveBackups.Create(saves, paths.SaveBackupDir(Profile.Id), DateTime.Now)
                    : null);
                if (backup is null)
                    note = L.T("There are no saves to back up yet.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Log.Warn($"The save backup failed: {ex.Message}");
                backupError = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
            OnPropertyChanged(nameof(SaveBackupsText));
            if (backupError is not null && !await AskAsync(L.T("Saves not backed up"),
                    L.F($"Your saves could not be backed up: {backupError}\n\nStart {GameDisplayName} anyway?"), L.T("Start anyway")))
                return;
        }
        try
        {
            Game!.Launch();
            ShowToast(L.Join(L.F($"Starting {GameDisplayName}…"), note));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Warn($"Launch failed: {ex.Message}");
            ShowToast(L.F($"The game could not be started: {ex.Message}"), isError: true);
        }
    }

    [RelayCommand]
    private void ChooseSaveFolder()
    {
        var dialog = new OpenFolderDialog { Title = L.F($"Choose the folder that holds your {GameDisplayName} saves") };
        if (SaveFolder is { } current && Directory.Exists(current))
            dialog.InitialDirectory = current;
        if (dialog.ShowDialog() != true)
            return;
        settings.SaveFolders[Profile.Id] = dialog.FolderName;
        SaveSettings();
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private void OpenSaveFolder()
    {
        if (SaveFolder is { } folder && Directory.Exists(folder))
            TryOpen(folder);
        else
            ShowToast(L.T("The save folder was not found. Choose it first."), isError: true);
    }

    [RelayCommand]
    private void OpenSaveBackups()
    {
        var folder = paths.SaveBackupDir(Profile.Id);
        Directory.CreateDirectory(folder);
        TryOpen(folder);
    }

    [RelayCommand]
    private async Task BackUpSavesNow()
    {
        if (SaveFolder is not { } saves)
        {
            ShowToast(L.F($"Save backups are not available for {GameDisplayName} yet."), isError: true);
            return;
        }
        string? backup = null;
        if (await RunAsync(L.T("Backing up your saves…"),
                async () => backup = await Task.Run(() => SaveBackups.Create(saves, paths.SaveBackupDir(Profile.Id), DateTime.Now))))
            ShowToast(L.F($"Saves backed up to {backup}."));
        OnPropertyChanged(nameof(SaveBackupsText));
        OnPropertyChanged(nameof(SaveBackupList));
    }

    /// <summary>Puts a save backup back, after a question; the saves of now are backed up first.</summary>
    [RelayCommand]
    private async Task RestoreSaveBackup(string backup)
    {
        if (!EnsureGame() || SaveFolder is not { } saves)
            return;
        if (GameProcess.IsRunning(Game!))
        {
            ShowToast(L.F($"{GameDisplayName} is running. Close the game first, then try again."), isError: true);
            return;
        }
        var made = File.GetLastWriteTime(backup);
        if (!await AskAsync(L.T("Restore saves"),
                L.F($"Put back the saves of {made:G}? Your saves of now are backed up first, so you can go back to them.\n\nIf Steam then asks about a conflict with its cloud saves, choose the files on this PC."),
                L.T("Restore saves")))
            return;
        if (await RunAsync(L.T("Restoring your saves…"),
                () => Task.Run(() => SaveBackups.Restore(backup, saves, paths.SaveBackupDir(Profile.Id), DateTime.Now))))
            ShowToast(L.F($"The saves of {made:G} are back."));
        OnPropertyChanged(nameof(SaveBackupsText));
        OnPropertyChanged(nameof(SaveBackupList));
    }

    [RelayCommand]
    private void OpenModFolder()
    {
        if (!EnsureGame())
            return;
        Directory.CreateDirectory(Game!.ModsPath);
        TryOpen(Game.ModsPath);
    }

    [RelayCommand]
    private void OpenGameFolder()
    {
        if (EnsureGame())
            TryOpen(Game!.RootPath);
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        Directory.CreateDirectory(paths.Root);
        TryOpen(paths.Root);
    }

    [RelayCommand]
    private void OpenLogs()
    {
        Directory.CreateDirectory(paths.LogsDir);
        TryOpen(paths.LogsDir);
    }

    /// <summary>Opens "Report a problem" with the facts about the game, UE4SS and the mods.</summary>
    [RelayCommand]
    private async Task ShowHelpDialog()
    {
        var current = manager;
        var facts = new HelpFacts(Profile, Game, Game is null ? null : Ue4ssRuntime.Detect(Game), installedUe4ssVersion,
            current?.Mods ?? [], [], current?.OtherInstallRoot, paths.LogsDir, IsUe4ssInstalled ? unofficialUe4ssFiles : null);
        string? collected = null;
        await RunAsync(L.T("Collecting the facts…"), async () => collected = await Task.Run(() =>
            HelpReport.Collect(facts with { Unmanaged = current?.UnmanagedFiles() ?? [] })));
        if (collected is not null)
            HelpDialog = new HelpReportViewModel(this, collected);
    }

    internal void CloseHelpDialog() => HelpDialog = null;

    [RelayCommand]
    private void ShowPreRelease() => IsPreReleaseDialogOpen = true;

    [RelayCommand]
    private void ClosePreRelease() => IsPreReleaseDialogOpen = false;

    [RelayCommand]
    private void OpenProjectPage() => TryOpen(AppInfo.ProjectUrl);

    [RelayCommand]
    private void OpenDiscord() => TryOpen(AppInfo.DiscordUrl);

    [RelayCommand]
    private void OpenBackups()
    {
        var folder = paths.BackupDir(Profile.Id);
        Directory.CreateDirectory(folder);
        TryOpen(folder);
    }

    /// <summary>
    /// A click on the column the list is already sorted by reverses the order. A new
    /// column starts with names A to Z, or with the newest installation first.
    /// </summary>
    [RelayCommand]
    private void SortBy(ModSortField field)
    {
        if (field == SortField)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortField = field;
            SortDescending = field == ModSortField.Installed;
        }
        ApplySort();
        settings.ModsSort = SortField.ToString();
        settings.ModsSortDescending = SortDescending;
        SaveSettings();
    }

    [RelayCommand]
    private void DismissToast()
    {
        toastTimer.Stop();
        Toast = null;
        toastAction = null;
    }

    /// <summary>The toast's button, such as Show log.</summary>
    [RelayCommand]
    private void ToastAction()
    {
        var action = toastAction;
        DismissToast();
        action?.Invoke();
    }

    partial void OnSearchTextChanged(string value)
    {
        ModsView.Refresh();
        OnPropertyChanged(nameof(ModsCountText));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyHint));
    }

    private void ApplySort() => ((ListCollectionView)ModsView).CustomSort = new ModOrder(SortField, SortDescending);

    /// <summary>An arrow on the sorted column; a quiet dash on the other one, so it reads as sortable too.</summary>
    private string SortGlyph(ModSortField field) =>
        field != SortField ? "" : SortDescending ? "" : "";

    /// <summary>The saved folder first, then Steam and Epic.</summary>
    private GameInstall? FindGame()
    {
        if (settings.GamePaths.TryGetValue(Profile.Id, out var saved))
        {
            var install = GameLocator.FromFolder(Profile, saved);
            if (install is not null)
                return install;
            Log.Warn($"The saved game folder '{saved}' no longer matches the profile.");
        }
        return GameLocator.Discover(Profile);
    }

    /// <summary>
    /// Connects to an install. When enabled mods are still in another install of the game, the
    /// player first moves them here, or keeps the install they are in.
    /// </summary>
    /// <param name="connectedText">Shown once connected, unless mods were moved.</param>
    /// <returns>False when the other install was kept.</returns>
    private async Task<bool> SetGameAsync(GameInstall? install, string? connectedText = null)
    {
        ModManager? next = null;
        if (install is not null && !await RunAsync(L.T("Reading the mod list…"), async () => next = await Task.Run(() => new ModManager(install, paths))))
            return false;
        if (next?.OtherInstallRoot is { } other)
        {
            if (!await MoveModsAsync(next, other))
            {
                // Nothing is connected yet at start, so the install that holds the mods is used from now on.
                if (manager is null && GameLocator.FromFolder(Profile, other) is { } holding)
                {
                    Connect(new ModManager(holding, paths));
                    settings.GamePaths[Profile.Id] = holding.RootPath;
                    SaveSettings();
                    ShowToast(L.F($"Using {holding.RootPath}, where your mods are."));
                }
                return false;
            }
            connectedText = null;
        }
        Connect(next);
        if (connectedText is not null)
            ShowToast(connectedText);
        return true;
    }

    /// <summary>Asks whether to move the enabled mods from the other install, and moves them.</summary>
    /// <returns>False when the player declined or the move failed; nothing changed then.</returns>
    private async Task<bool> MoveModsAsync(ModManager next, string other)
    {
        var count = next.ModsInOtherInstall.Count;
        if (!await AskAsync(L.T("Move your mods"),
                L.Plural(count,
                    "{0} enabled mod is still installed in another folder of {1}:\n\n{2}\n\nMove it to {3}? The mod files are removed from the other folder and installed in this one. Close the game first.\n\nKeep the other folder to go on using it.",
                    "{0} enabled mods are still installed in another folder of {1}:\n\n{2}\n\nMove them to {3}? The mod files are removed from the other folder and installed in this one. Close the game first.\n\nKeep the other folder to go on using it.",
                    GameDisplayName, other, next.Game.RootPath),
                L.T("Move mods"), L.T("Keep the other folder")))
            return false;
        IReadOnlyList<ModRecord> stayed = [];
        if (!await RunAsync(L.T("Moving mods…"), async () => stayed = await Task.Run(next.MoveHere)))
            return false;
        var moved = L.Plural(count, "Moved {0} mod to {1}.", "Moved {0} mods to {1}.", next.Game.RootPath);
        ShowToast(stayed.Count == 0
                ? moved
                : L.Join(moved, L.F($"{string.Join(", ", stayed.Select(m => $"\"{m.Name}\""))} stayed disabled, because another file of the same name is in the way.")),
            isError: stayed.Count > 0);
        return true;
    }

    private void Connect(ModManager? next)
    {
        manager = next;
        Game = next?.Game;
        Log.Info(Game is null
            ? $"{Profile.DisplayName} was not found."
            : $"{Profile.DisplayName} at '{Game.RootPath}' ({Game.Store}); mods folder '{Game.ModsPath}'.");
        CheckGameBuild();
        ue4ssErrors = null;
        unofficialUe4ssFiles = null;
        RefreshMods();
        if (Game is { } game)
        {
            _ = ReadUe4ssErrorsAsync(game);
            _ = CheckUe4ssBuildAsync();
        }
    }

    /// <summary>
    /// Notices a game update by the store's build id. With mods enabled, Home says so until the player
    /// confirms; otherwise the new build is just remembered.
    /// </summary>
    private void CheckGameBuild()
    {
        previousBuild = null;
        if (Game?.BuildId is not { } build || !settings.GameBuilds.TryGetValue(Profile.Id, out var known) || known == build
            || manager is null || !manager.Mods.Any(m => m.Enabled))
        {
            RememberBuild();
            return;
        }
        previousBuild = known;
        Log.Info($"{Profile.DisplayName} was updated from '{known}' to '{build}'.");
    }

    [RelayCommand]
    private void DismissGameUpdate()
    {
        previousBuild = null;
        RememberBuild();
        OnPropertyChanged(nameof(IsGameUpdated));
    }

    private void RememberBuild()
    {
        if (Game?.BuildId is not { } build
            || (settings.GameBuilds.TryGetValue(Profile.Id, out var known) && known == build))
            return;
        settings.GameBuilds[Profile.Id] = build;
        SaveSettings();
    }

    private void RefreshMods()
    {
        Mods.Clear();
        if (manager is not null)
        {
            foreach (var mod in manager.Mods)
                Mods.Add(new ModItemViewModel(this, mod, manager.LibraryFolderOf(mod)));
        }
        try
        {
            UnmanagedCount = manager?.UnmanagedFiles().Count ?? 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"The mods folder could not be read: {ex.Message}");
            UnmanagedCount = 0;
        }
        ue4ss = Game is null ? null : Ue4ssRuntime.Detect(Game);
        installedUe4ssVersion = manager?.InstalledUe4ssVersion;
        OnModsChanged();
        // Every summary on every page derives from the game and the mod list.
        OnPropertyChanged(string.Empty);
    }

    private bool EnsureGame()
    {
        if (Game is not null && manager is not null)
            return true;
        ShowToast(L.F($"Locate {GameDisplayName} first: Settings > Locate game folder."), isError: true);
        return false;
    }

    /// <summary>Runs work behind the busy overlay. A failure becomes a message instead of a crash.</summary>
    internal async Task<bool> RunAsync(string text, Func<Task> action)
    {
        BusyText = text;
        IsBusy = true;
        try
        {
            await action();
            return true;
        }
        catch (ModException ex)
        {
            Log.Warn(ex.Message);
            ShowToast(L.Message(ex), isError: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log.Error(text, ex);
            ShowToast(L.F($"Something went wrong: {ex.Message}"), isError: true);
        }
        finally
        {
            IsBusy = false;
        }
        return false;
    }

    private async Task<bool> AskAsync(string title, string message, string confirmText, string? cancelText = null)
    {
        Confirm = new ConfirmViewModel(title, message, confirmText, cancelText ?? L.T("Cancel"));
        var accepted = await Confirm.Result;
        Confirm = null;
        return accepted;
    }

    /// <summary>A dialog, the busy screen or the drop target covers the window.</summary>
    internal bool HasOverlay => IsBusy || IsDragOver || InstallDialog is not null || Ue4ssGuide is not null
        || Confirm is not null || HelpDialog is not null || ResetDialog is not null || SettingsEditor is not null || ModDetails is not null
        || ModSets is not null || Update.IsDialogOpen || IsPreReleaseDialogOpen;

    /// <summary>The newest app version the banner at start announced; see <see cref="AppSettings.AnnouncedUpdate"/>.</summary>
    internal string? AnnouncedUpdate
    {
        get => settings.AnnouncedUpdate;
        set
        {
            settings.AnnouncedUpdate = value;
            SaveSettings();
        }
    }

    /// <param name="actionText">A button in the toast, which runs <paramref name="action"/>; the toast then stays longer.</param>
    internal void ShowToast(string text, bool isError = false, string? actionText = null, Action? action = null)
    {
        Toast = new ToastMessage(text, isError, action is null ? null : actionText);
        toastAction = action;
        toastTimer.Stop();
        toastTimer.Interval = TimeSpan.FromSeconds(action is not null ? 30 : isError ? 12 : 6);
        toastTimer.Start();
    }

    private void TryOpen(string path)
    {
        try
        {
            Shell.Open(path);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            ShowToast(L.F($"'{path}' could not be opened: {ex.Message}"), isError: true);
        }
    }

    /// <summary>Where the window was when it last closed; see <see cref="AppSettings.Window"/>.</summary>
    internal WindowPlacement? WindowPlacement => settings.Window;

    /// <summary>Remembers where the window is as the app closes. A failure is only logged: no one would see a note.</summary>
    internal void RememberWindow(WindowPlacement placement)
    {
        settings.Window = placement;
        try
        {
            settings.Save(paths);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"The window's place could not be saved: {ex.Message}");
        }
    }

    private void SaveSettings()
    {
        try
        {
            settings.Save(paths);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Settings could not be saved.", ex);
            ShowToast(L.F($"Settings could not be saved: {ex.Message}"), isError: true);
        }
    }
}
