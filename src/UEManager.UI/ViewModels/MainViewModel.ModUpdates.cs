using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core;
using UEManager.Core.Mods;
using UEManager.Core.Updates;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>What the last update check found for a mod with a mod page: the version on the page, and whether it is newer.</summary>
/// <param name="IsAvailable">The page has a newer version than the installed one.</param>
/// <param name="Latest">The version on the mod page.</param>
/// <param name="Installed">The installed version it was compared with.</param>
public sealed record ModUpdateStatus(bool IsAvailable, string Latest, string Installed);

/// <summary>
/// The update check for mods with a mod page: one request for all of them, at each start (unless turned off in
/// Settings) and on the button in the mod list's header, which then waits 30 seconds. It only tells: the player
/// downloads a newer version on the mod page, and the app never downloads it.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>How long the button waits after a check, so it cannot flood the mod site.</summary>
    internal static readonly TimeSpan ModUpdateWait = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ModUpdateTimeout = TimeSpan.FromSeconds(15);

    /// <summary>The shortest time the button spins, however fast the mod site answers.</summary>
    private static readonly TimeSpan ModUpdateSpin = TimeSpan.FromSeconds(1);

    /// <summary>A check runs: the button in the mod list's header spins.</summary>
    public bool IsCheckingModUpdates => checkingModUpdates;

    /// <summary>
    /// The page version and files per mod number on the site, from the last check. A mod's state comes from its own
    /// version at the time, so an update in the manager shows at once, without asking the site again.
    /// </summary>
    private readonly Dictionary<string, ModSitePage> sitePages = new(StringComparer.Ordinal);

    private bool checkingModUpdates;
    /// <summary>When the last check started, on the system's running clock (<see cref="Environment.TickCount64"/>), which a change of the PC's clock does not move; null before the first.</summary>
    internal long? lastModUpdateCheck;
    private DispatcherTimer? modUpdateWait;

    /// <summary>
    /// The mod's page: on openmods for a mod installed from there, else on the mod site from the number in its download's
    /// file name. Null without one, and for the card's mods, which have their own notes and links.
    /// </summary>
    internal string? ModPageOf(ModRecord mod) => mod.Recommended ? null : CatalogPageOf(mod) ?? Profile.ModPage(mod.SourceFileName);

    /// <summary>The state of a mod for its update box; null when it was not checked, or the check could not tell.</summary>
    internal ModUpdateStatus? ModUpdateOf(ModRecord mod)
    {
        if (mod.NotTracked)
            return null;
        if (IsFromCatalog(mod))
            return CatalogUpdateOf(mod);
        if (ModNaming.SiteModId(mod.SourceFileName) is not { } id || !sitePages.TryGetValue(id, out var page)
            || InstalledVersion(mod) is not { } installed)
            return null;
        var check = ModSiteVersions.Check(installed, mod.SourceFileName, page);
        return check.State switch
        {
            ModSiteState.UpdateAvailable => new ModUpdateStatus(true, check.ComparedVersion!, installed),
            ModSiteState.UpToDate => new ModUpdateStatus(false, check.ComparedVersion!, installed),
            _ => null,
        };
    }

    /// <summary>The words of the blue box, in English in every language: a mod from openmods installs its update with one click.</summary>
    internal static string UpdateBoxText(ModRecord mod) => IsFromCatalog(mod) ? "Install update" : "Update available";

    /// <summary>The tooltip of the update box: what was compared, and what a click does.</summary>
    internal static string? ModUpdateHint(ModRecord mod, ModUpdateStatus? update) => (update, IsFromCatalog(mod)) switch
    {
        ({ IsAvailable: true } found, true) => L.F($"Version {found.Latest} is on openmods; you have {found.Installed}. A click installs it."),
        ({ } found, true) => L.F($"openmods has version {found.Latest}, the one you have."),
        ({ IsAvailable: true } found, false) => L.F($"Version {found.Latest} is on the mod page; you have {found.Installed}. Opens the mod page's files."),
        ({ } found, false) => L.F($"The mod page has version {found.Latest}, the one you have."),
        _ => null,
    };

    /// <summary>
    /// A click on the blue box: a mod from openmods updates at once, as on the openmods page; any other mod opens its
    /// mod page's files, where the player downloads the update, as the mod site asks.
    /// </summary>
    internal Task OpenModUpdateAsync(ModRecord mod)
    {
        if (CatalogModOf(mod) is { InstallFile: not null } entry)
            return InstallFromCatalogAsync(entry);
        if (ModPageOf(mod) is { } page)
            OpenPath(IsFromCatalog(mod) ? page : page + "?tab=files");
        return Task.CompletedTask;
    }

    /// <summary>
    /// The mod's version in the list, or else the one in its download's name: a mod installed before the app read
    /// that name, such as one whose name ends in "(1)", has no version in the list.
    /// </summary>
    private static string? InstalledVersion(ModRecord mod) =>
        mod.Version is { Length: > 0 } version ? version
        : mod.SourceFileName is { Length: > 0 } source ? ModNaming.FromSource(source).Version
        : null;

    /// <summary>The player keeps a mod's version, or tracks it again; see <see cref="ModRecord.NotTracked"/>.</summary>
    internal void SetModTracked(ModRecord mod, bool tracked)
    {
        if (manager is null)
            return;
        try
        {
            manager.SetTracked(mod.Id, tracked);
        }
        catch (Exception ex) when (ex is ModException or IOException or UnauthorizedAccessException)
        {
            ShowToast(L.F($"Something went wrong: {ex.Message}"), isError: true);
        }
        RefreshMods();
    }

    /// <summary>Puts in the pages a check found, per mod number, and shows them in the rows.</summary>
    internal void ShowModUpdates(IReadOnlyDictionary<string, ModSitePage> found)
    {
        sitePages.Clear();
        foreach (var (id, page) in found)
            sitePages[id] = page;
        RefreshMods();
    }

    /// <summary>A mod in the list has a newer version on its mod page.</summary>
    public bool HasModUpdates => Mods.Any(m => m.IsUpdateAvailable);

    /// <summary>The UPDATE mark on the Mods entry: a newer version of the card's mod or of a mod in the list.</summary>
    public bool IsAnyUpdateAvailable => IsRecommendedUpdateAvailable || HasModUpdates;

    /// <summary>The game profile names the mod site's API, so mods with a mod page can be checked.</summary>
    public bool HasModSite => Profile.ModSiteApiUrl is { Length: > 0 } && Profile.ModSiteGameId is { Length: > 0 };

    /// <summary>Mods can be checked: those of the mod site, or those installed from openmods.</summary>
    public bool HasUpdateCheck => HasModSite || Catalog is not null;

    /// <summary>The notes on how the check works and where it cannot tell, from the box next to its button.</summary>
    [ObservableProperty]
    private bool isUpdateNotesOpen;

    /// <summary>
    /// The notes behind the box next to the button: what is compared and how the page is found, when, what the box
    /// says, the version rules, mods from openmods, the limits of one version per page, and what to do about a wrong box.
    /// </summary>
    public IReadOnlyList<string> UpdateNotes =>
    [
        L.T("What is compared: your version of a mod against the version of the file you have on its page, as the file's name in the download tells it; else against the page's main file, else the version the author set on the page. The app finds the page by the mod's number in the download's file name, such as 61 in \"Travel Anywhere 61 1.0.1 2026-10-29T13-42Z LbtEo64mS.zip\", so keep that name: a renamed download has no mod page and no check. Your version, 1.0.1 here, comes from the same name, or from Edit."),
        L.T("When: at each start and when you click the button; then the button waits 30 seconds. The app sends only the game's ID and the mod IDs, needs no account and downloads nothing. Settings ▸ Mod updates turns off the check at start."),
        L.T("The box: Update available when the file you have has a newer version, Up to date when it has the same or an older one. Its tooltip shows both versions. An empty box means the check failed or could not compare the two: the app log says why."),
        L.T("The rules: numbers are compared one by one, so 1.10 is newer than 1.9, and 1.6 equals 1.6.0. Text after the numbers counts only when the numbers are the same: a single letter in the alphabet's order, so 1.2b is newer than 1.2a; other text, such as 1.2-beta against 1.2, leaves the box empty unless both versions are the same."),
        L.T("Mods from the openmods page are compared with their newest release on openmods, which the app reads at most every 10 minutes. Their box says Install update: one click downloads and installs it."),
        L.T("The limits: the download's file name is the only sign of which file you have, so a renamed download is compared with the page's main file. A file the page no longer lists counts as the newest file of its variant; a page whose files the site does not tell leaves only the version the author set on the page."),
        L.T("If a box looks wrong, compare the versions in its tooltip with the mod page, and fix yours with Edit. To keep your version, turn off Check this mod for updates in the mod's details: the box then says Not tracked."),
    ];

    [RelayCommand]
    private void ShowUpdateNotes() => IsUpdateNotesOpen = true;

    [RelayCommand]
    private void CloseUpdateNotes() => IsUpdateNotesOpen = false;

    /// <summary>How long the button still waits after the last check; zero or less when it may check.</summary>
    private TimeSpan ModUpdateWaitLeft => lastModUpdateCheck is { } started
        ? ModUpdateWait - TimeSpan.FromMilliseconds(Environment.TickCount64 - started)
        : TimeSpan.Zero;

    private bool IsWaitingAfterModUpdateCheck => ModUpdateWaitLeft > TimeSpan.Zero;

    public bool CanCheckModUpdates => HasUpdateCheck && !checkingModUpdates && !IsWaitingAfterModUpdateCheck;

    public string CheckModUpdatesHint =>
        checkingModUpdates ? L.T("Checking for updates…")
        : IsWaitingAfterModUpdateCheck ? L.T("You can check again 30 seconds after the last check.")
        : L.T("Check for updates");

    /// <summary>At each start the app checks, unless the player turned it off; the button works either way.</summary>
    public bool CheckModUpdatesAtStart
    {
        get => settings.CheckModUpdatesAtStart;
        set
        {
            settings.CheckModUpdatesAtStart = value;
            SaveSettings();
            OnPropertyChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCheckModUpdates))]
    private Task CheckModUpdates() => CheckModUpdatesAsync(byButton: true);

    /// <summary>
    /// Asks the mod site once for the page version of every mod with a mod page; the card's mods and mods without a mod
    /// page are left out, and with none of them nothing is sent. Mods installed from openmods are compared with the
    /// openmods list, which is read at most every 10 minutes, as openmods asks. A failed check changes nothing in the
    /// rows; the log says why. After a press on the button, it waits 30 seconds; the check at start leaves it ready, so
    /// the first visit of the Mods page never finds it grey.
    /// </summary>
    internal async Task CheckModUpdatesAsync(bool byButton = false)
    {
        if (!CanCheckModUpdates || manager is not { } current)
            return;
        List<string> ids = !HasModSite ? [] : current.Mods
            .Where(m => !m.Recommended && !m.NotTracked && !IsFromCatalog(m) && Profile.ModPage(m.SourceFileName) is not null)
            .Select(m => ModNaming.SiteModId(m.SourceFileName)!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var fromCatalog = Catalog is not null && current.Mods.Any(m => IsFromCatalog(m) && !m.NotTracked);
        BeginModUpdateCheck(byButton);
        try
        {
            // The button spins as long as the requests take, but at least a second, so a click is never without a sign;
            // the rows change when it stops. The openmods list shows its mods' boxes itself once it is read.
            var shortest = Task.Delay(ModUpdateSpin);
            var asking = ids.Count == 0
                ? Task.FromResult<IReadOnlyDictionary<string, ModSitePage>?>(null)
                : ModSiteVersions.FetchAsync(Http, Profile.ModSiteApiUrl!, Profile.ModSiteGameId!, ids, AppInfo.Version, ModUpdateTimeout);
            var reading = fromCatalog ? Catalog!.LoadAsync() : Task.CompletedTask;
            await Task.WhenAll(shortest, asking, reading);
            if (await asking is { } found)
                ShowModUpdates(found);
        }
        finally
        {
            EndModUpdateCheck();
        }
    }

    /// <summary>A check starts: the button spins, and after a press it then waits.</summary>
    internal void BeginModUpdateCheck(bool byButton)
    {
        checkingModUpdates = true;
        if (byButton)
        {
            lastModUpdateCheck = Environment.TickCount64;
            StartModUpdateWait();
        }
        OnModUpdateCheckChanged();
    }

    internal void EndModUpdateCheck()
    {
        checkingModUpdates = false;
        OnModUpdateCheckChanged();
    }

    /// <summary>The button comes back when the wait is over.</summary>
    private void StartModUpdateWait()
    {
        if (modUpdateWait is null)
        {
            modUpdateWait = new DispatcherTimer();
            modUpdateWait.Tick += (_, _) => OnModUpdateWaitTick();
        }
        modUpdateWait.Stop();
        modUpdateWait.Interval = ModUpdateWait;
        modUpdateWait.Start();
    }

    /// <summary>
    /// The timer may fire a little early: then it waits for the rest, or the button would stay grey, as nothing else
    /// brings it back.
    /// </summary>
    internal void OnModUpdateWaitTick()
    {
        var left = ModUpdateWaitLeft;
        if (left > TimeSpan.Zero && modUpdateWait is not null)
        {
            modUpdateWait.Stop();
            modUpdateWait.Interval = left + TimeSpan.FromMilliseconds(50);
            modUpdateWait.Start();
            return;
        }
        modUpdateWait?.Stop();
        OnModUpdateCheckChanged();
    }

    private void OnModUpdateCheckChanged()
    {
        OnPropertyChanged(nameof(IsCheckingModUpdates));
        OnPropertyChanged(nameof(CanCheckModUpdates));
        OnPropertyChanged(nameof(CheckModUpdatesHint));
        CheckModUpdatesCommand.NotifyCanExecuteChanged();
    }
}
