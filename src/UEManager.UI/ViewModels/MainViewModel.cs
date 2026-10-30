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

public sealed record ToastMessage(string Text, bool IsError)
{
    public string Tone => IsError ? "bad" : "good";
}

public sealed partial class MainViewModel : LocalizedObservableObject
{
    private readonly AppPaths paths;
    private readonly AppSettings settings;
    private readonly DispatcherTimer toastTimer = new();

    /// <summary>
    /// The app's only connection: the UE4SS manifest and the update check at start, the openmods
    /// lists and covers when the openmods page opens, and the downloads the user starts.
    /// </summary>
    private static readonly HttpClient Http = CreateHttpClient();
    private ModManager? manager;
    private Ue4ssRuntime? ue4ss;
    private string? installedUe4ssVersion;
    private Ue4ssRelease? latestUe4ss;

    /// <summary>The build before a game update the player has not confirmed yet; null when there is none.</summary>
    private string? previousBuild;

    /// <param name="catalogUrl">Another openmods games list, such as a test copy; used by a build with the catalog only.</param>
    /// <param name="updateUrl">Another release list for the update check, such as a test copy.</param>
    public MainViewModel(GameProfile profile, AppPaths paths, ArtSet art, string? catalogUrl = null, string? updateUrl = null)
    {
        Profile = profile;
        this.paths = paths;
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
        toastTimer.Tick += (_, _) =>
        {
            toastTimer.Stop();
            Toast = null;
        };
    }

    // Hooks for the optional catalog, implemented in Catalog\MainViewModel.Catalog.cs. A build without
    // the catalog leaves that folder out, so the hooks stay empty and their calls vanish.
    partial void CreateCatalog(string? catalogUrl);

    /// <summary>The installed mods or UE4SS changed.</summary>
    partial void OnModsChanged();

    public GameProfile Profile { get; }

    public string GameDisplayName => L.T(Profile.DisplayName);

    public string GameTagline => L.T(Profile.Tagline ?? "");

    public IReadOnlyList<LanguageOption> Languages { get; } =
        [new("zh-CN", "简体中文"), new("en", "English")];

    public string SelectedLanguage
    {
        get => L.Current.Language;
        set
        {
            if (value is not ("en" or "zh-CN") || value == L.Current.Language)
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

    public string GameUpdatedText =>
        L.F($"{GameDisplayName} was updated from {(Game?.Store == GameStore.Epic ? L.T("version") : L.T("build"))} {previousBuild} to {Game?.BuildId}. ")
        + L.T("A game update can break mods until their authors update them. If the game crashes or acts strangely, ")
        + L.T("disable mods one by one to find the one that needs an update.");

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
                : L.F($"{Format.Count(needing, "installed mod needs", "installed mods need")} UE4SS; {enabled} enabled.");
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

    /// <summary>The game is known but UE4SS is not in it, so the install guide is offered.</summary>
    public bool IsUe4ssMissing => ue4ss is { Installed: false };

    public string UnmanagedText =>
        L.F($"{Format.Count(UnmanagedCount, "file", "files")} in the mods folder {(UnmanagedCount == 1 ? L.T("was") : L.T("were"))} not installed by the manager");

    public async Task InitializeAsync(IReadOnlyList<string> downloads)
    {
        ModManager.ClearStaging(paths);
        GameInstall? found = null;
        await RunAsync(L.T("Looking for the game…"), async () => found = await Task.Run(FindGame));
        await SetGameAsync(found);
        _ = CheckUe4ssManifestAsync();
        _ = Update.CheckAsync();
        if (downloads.Count > 0)
            await InstallAsync(downloads);
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
                        L.F($"\"{Path.GetFileName(source)}\" is UE4SS itself, not a mod. Install it into {GameDisplayName}? ")
                        + L.T("Your existing UE4SS settings are kept."), L.T("Install UE4SS")))
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
        var note = item.HasSettings ? L.T(" Settings you changed are backed up first.") : "";
        if (!await AskAsync(L.T("Uninstall mod"),
                L.F($"Remove \"{item.Name}\" from the game and from the manager's library? The original download is not touched.{note}"),
                L.T("Uninstall")))
            return;
        string? backup = null;
        if (await RunAsync(L.T("Uninstalling…"), async () => backup = await Task.Run(() => manager!.Uninstall(item.Id))))
            ShowToast(backup is null
                ? L.F($"Uninstalled \"{item.Name}\".")
                : L.F($"Uninstalled \"{item.Name}\". The settings you changed were backed up to {backup}."));
        RefreshMods();
    }

    /// <summary>Opens the mod's settings files in the game with the app set for their type, or Notepad.</summary>
    internal void EditSettings(ModItemViewModel item)
    {
        foreach (var file in item.DeployedSettingsFiles)
            TryOpen(file);
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
        if (string.IsNullOrWhiteSpace(Profile.Ue4ssManifestUrl))
            return;
        latestUe4ss = await Ue4ssManifest.FetchAsync(Http, Profile.Ue4ssManifestUrl, Profile.Id);
        if (latestUe4ss is null)
            return;
        OnModsChanged();
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private Task UpdateUe4ss() => DownloadAndInstallUe4ssAsync();

    [RelayCommand]
    private async Task RemoveUe4ss()
    {
        if (!EnsureGame() || !await AskAsync(L.T("Remove UE4SS"),
                L.F($"This removes UE4SS from {GameDisplayName} completely: dwmapi.dll and the whole ue4ss folder, ")
                + L.T("with UE4SS's settings and every mod inside it, also mods installed by hand.\n\n")
                + L.T("Mods in your list that need UE4SS are disabled. Their copies stay in the library, ")
                + L.T("so you can enable them again after installing UE4SS.\n\nClose the game first."),
                L.T("Remove UE4SS")))
            return;
        Ue4ssRemoveResult? result = null;
        await RunAsync(L.T("Removing UE4SS…"), async () => result = await Task.Run(() => manager!.RemoveUe4ss()));
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
            parts.Add(L.F($"{Format.Count(result.ModsTurnedOff, "mod", "mods")} turned off"));
        if (result.Ue4ssRemoved)
            parts.Add(L.T("UE4SS removed"));
        if (result.FilesMovedAside > 0)
            parts.Add(L.F($"{Format.Count(result.FilesMovedAside, "other file", "other files")} moved to a backup folder"));
        if (result.ModsDeleted > 0)
            parts.Add(L.F($"{Format.Count(result.ModsDeleted, "mod", "mods")} deleted from the manager"));
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
        var removed = result.RemovedFiles.Count == 0 ? "" : L.F($" {Format.Count(result.RemovedFiles.Count, "old file was", "old files were")} removed.");
        var kept = result.KeptFiles.Count == 0 ? "" : L.F($" {Format.Count(result.KeptFiles.Count, "existing file was", "existing files were")} kept.");
        var hint = installedUe4ssVersion is null ? L.T(" Start the game once to show its version.") : "";
        ShowToast((result.Updated ? L.T("UE4SS updated.") : L.T("UE4SS installed.")) + removed + kept + hint);
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

    /// <summary>Looks for UE4SS again and reads its log, as when the UE4SS page opens.</summary>
    internal Task RefreshUe4ssLogAsync()
    {
        ue4ss = Game is null ? null : Ue4ssRuntime.Detect(Game);
        return Ue4ssLogPanel.LoadAsync(ue4ss);
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
        var note = "";
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
                    note = L.T(" There are no saves to back up yet.");
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
            ShowToast(L.F($"Starting {GameDisplayName}…{note}"));
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
            current?.Mods ?? [], [], current?.OtherInstallRoot, paths.LogsDir);
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
                L.F($"{Format.Count(count, "enabled mod is", "enabled mods are")} still installed in another folder of {GameDisplayName}:\n\n")
                + L.F($"{other}\n\nMove {(count == 1 ? L.T("it") : L.T("them"))} to {next.Game.RootPath}? The mod files are removed from the other ")
                + L.T("folder and installed in this one. Close the game first.\n\nKeep the other folder to go on using it."),
                L.T("Move mods"), L.T("Keep the other folder")))
            return false;
        IReadOnlyList<ModRecord> stayed = [];
        if (!await RunAsync(L.T("Moving mods…"), async () => stayed = await Task.Run(next.MoveHere)))
            return false;
        ShowToast(stayed.Count == 0
                ? L.F($"Moved {Format.Count(count, "mod", "mods")} to {next.Game.RootPath}.")
                : L.F($"Moved {Format.Count(count, "mod", "mods")} to {next.Game.RootPath}. ")
                  + L.F($"{string.Join(", ", stayed.Select(m => $"\"{m.Name}\""))} stayed disabled, because another file of the same name is in the way."),
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
        RefreshMods();
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
            ShowToast(ex.Message, isError: true);
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

    private async Task<bool> AskAsync(string title, string message, string confirmText, string cancelText = "Cancel")
    {
        Confirm = new ConfirmViewModel(title, message, confirmText, cancelText);
        var accepted = await Confirm.Result;
        Confirm = null;
        return accepted;
    }

    /// <summary>A dialog, the busy screen or the drop target covers the window.</summary>
    internal bool HasOverlay => IsBusy || IsDragOver || InstallDialog is not null || Ue4ssGuide is not null
        || Confirm is not null || HelpDialog is not null || ResetDialog is not null || Update.IsDialogOpen || IsPreReleaseDialogOpen;

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

    internal void ShowToast(string text, bool isError = false)
    {
        Toast = new ToastMessage(text, isError);
        toastTimer.Stop();
        toastTimer.Interval = TimeSpan.FromSeconds(isError ? 12 : 6);
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
