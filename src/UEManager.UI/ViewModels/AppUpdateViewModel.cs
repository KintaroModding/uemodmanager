using System.Globalization;
using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core;
using UEManager.Core.Mods;
using UEManager.Core.Storage;
using UEManager.Core.Updates;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

public enum UpdateState
{
    Checking,
    UpToDate,
    Available,
    Failed,
}

/// <summary>
/// The version check at start and the update window. The newest GitHub release holds one zip per
/// build and game; the app either updates itself from the zip of this build, or the user downloads
/// it from the release page.
/// </summary>
/// <param name="releaseUrl">GitHub's answer for the newest release, or a test copy of it.</param>
/// <param name="filePrefix">The start of the zip name of this build for this game, such as "uemm_phantom-blade-zero_".</param>
public sealed partial class AppUpdateViewModel(MainViewModel owner, HttpClient http, AppPaths paths, string releaseUrl, string filePrefix)
    : ObservableObject
{
    /// <summary>How long the banner for a new version stays in the middle of the window.</summary>
    private static readonly TimeSpan BannerTime = TimeSpan.FromSeconds(3);

    /// <summary>The banner's fade-out, in MainWindow.xaml; the update box flashes after it.</summary>
    private static readonly TimeSpan BannerFadeTime = TimeSpan.FromMilliseconds(300);

    /// <summary>The flash of the update box, in MainWindow.xaml.</summary>
    private static readonly TimeSpan FlashTime = TimeSpan.FromMilliseconds(800);

    private AppRelease? release;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(StatusTone), nameof(StatusGlyph), nameof(StatusHint), nameof(IsAvailable))]
    private UpdateState state = UpdateState.Checking;

    [ObservableProperty]
    private bool isDialogOpen;

    [ObservableProperty]
    private bool isBannerVisible;

    /// <summary>True for a moment after the banner: the update box in the sidebar flashes once.</summary>
    [ObservableProperty]
    private bool isFlashing;

    private string? failure;

    public string StatusText => State switch
    {
        UpdateState.Checking => "Checking for updates…",
        UpdateState.UpToDate => "Up to date",
        UpdateState.Available => $"Update available: {release?.Version}",
        _ => "Update check failed",
    };

    public string StatusTone => State switch
    {
        UpdateState.UpToDate => "good",
        UpdateState.Available => "warn",
        _ => "muted",
    };

    public string StatusGlyph => State switch
    {
        UpdateState.UpToDate => "",
        UpdateState.Available => "",
        UpdateState.Checking => "",
        _ => "",
    };

    public string? StatusHint => State switch
    {
        UpdateState.UpToDate => $"Version {AppInfo.Version} is the newest release.",
        UpdateState.Available => "Show the update",
        UpdateState.Failed => failure,
        _ => null,
    };

    public bool IsAvailable => State == UpdateState.Available;

    public string BannerTitle => $"Version {release?.Version} is available";

    public string DialogTitle => $"Update to version {release?.Version}";

    public string DialogText => release?.Date is { } date
        ? $"You have version {AppInfo.Version}. Version {release.Version} came out on {date.LocalDateTime.ToString("d MMM yyyy", CultureInfo.CurrentCulture)}."
        : $"You have version {AppInfo.Version}.";

    public string? Notes => release?.Notes;

    /// <summary>The release holds the zip of this build for this game, so the app can update itself.</summary>
    public bool CanUpdateItself => release?.File is not null && Environment.ProcessPath is not null;

    private static string ExeName => Path.GetFileName(Environment.ProcessPath) ?? "UEModManager.exe";

    public string AutomaticText => CanUpdateItself
        ? $"The app downloads {release!.File!.Name}, checks it, replaces {ExeName} and starts again. Your mods, settings and data stay as they are."
        : "This release has no file for this app and game, so the app cannot update itself. Use the release page.";

    public string ManualText =>
        $"Opens the release page. Download {release?.File?.Name ?? "the zip for your app"}, close the app, and replace {ExeName} with the one from the zip.";

    /// <summary>
    /// Asks GitHub for the newest release. A new version is announced once; offline or failing,
    /// the box says so and nothing else happens.
    /// </summary>
    public async Task CheckAsync()
    {
        State = UpdateState.Checking;
        try
        {
            release = await AppReleases.FetchLatestAsync(http, releaseUrl, filePrefix);
            State = ModVersion.IsUpdate(AppInfo.Version, release.Version) ? UpdateState.Available : UpdateState.UpToDate;
        }
        catch (ModException ex)
        {
            Log.Warn($"Update check: {ex.Message}");
            failure = ex.Message;
            State = UpdateState.Failed;
        }
        OnPropertyChanged(string.Empty);
        if (State == UpdateState.Available)
            await AnnounceAsync(release!.Version);
    }

    /// <summary>
    /// Once per new version, without a sound: a short banner in the middle of the window, then the
    /// update box in the sidebar flashes once. While a dialog or the busy screen is open, it waits.
    /// </summary>
    private async Task AnnounceAsync(string version)
    {
        if (string.Equals(owner.AnnouncedUpdate, version, StringComparison.OrdinalIgnoreCase))
            return;
        while (owner.HasOverlay)
            await Task.Delay(500);
        owner.AnnouncedUpdate = version;
        IsBannerVisible = true;
        await Task.Delay(BannerTime);
        IsBannerVisible = false;
        await Task.Delay(BannerFadeTime);
        IsFlashing = true;
        await Task.Delay(FlashTime);
        IsFlashing = false;
    }

    [RelayCommand]
    private void Show()
    {
        if (IsAvailable)
            IsDialogOpen = true;
    }

    [RelayCommand]
    private void Close() => IsDialogOpen = false;

    [RelayCommand]
    private void OpenReleasePage() => owner.OpenPath(release?.PageUrl ?? AppInfo.ReleasesPageUrl);

    /// <summary>Downloads the zip of this build, checks it, puts the new exe in place of this one and restarts.</summary>
    [RelayCommand]
    private async Task UpdateNow()
    {
        if (release is not { File: { } file } || Environment.ProcessPath is not { } exe)
            return;
        IsDialogOpen = false;
        var folder = Path.Combine(paths.StagingDir, "update-" + Guid.NewGuid().ToString("N"));
        var title = $"Downloading version {release.Version}…";
        var replaced = await owner.RunAsync(title, async () =>
        {
            var progress = new Progress<double>(share => owner.BusyText = $"{title}  {share:P0}");
            var zip = await FileDownloader.DownloadAsync(http, file.Url, file.Name, file.Size, file.Sha256, folder, $"Version {release.Version}", progress);
            owner.BusyText = "Installing the update…";
            await Task.Run(() =>
            {
                var unpacked = Path.Combine(folder, "unpacked");
                ArchiveStager.Stage(zip, unpacked);
                SelfUpdate.Replace(exe, SelfUpdate.FindExecutable(unpacked, release.Version));
            });
        });
        MainViewModel.TryDeleteFolder(folder);
        if (!replaced)
            return;

        Log.Info($"Updated to {release.Version}; starting the new version.");
        // The new exe is in place; only a failed restart is left to the player.
        if (!App.Restart())
            owner.ShowToast($"Version {release.Version} is installed. Close the app and start it again to use it.");
    }
}
