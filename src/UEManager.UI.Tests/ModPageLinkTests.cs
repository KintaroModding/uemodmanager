using System.IO;
using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;
using UEManager.Core.Updates;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>
/// "Open mod page" and the update box in a mod's row and details: for a mod whose download kept the mod site's file
/// name, and for a mod installed from openmods.
/// </summary>
public class ModPageLinkTests : IDisposable
{
    private static IReadOnlyDictionary<string, ModSitePage> Pages(params (string Id, string Version)[] pages) =>
        pages.ToDictionary(p => p.Id, p => new ModSitePage(p.Version, []));
    private const string SiteDownload = "Travel Anywhere 61 1.0.1 2026-10-29T13-42Z LbtEo64mS.zip";

    private readonly string folder = Path.Combine(Path.GetTempPath(), "uemm-mod-page-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    private void With(string? modPageUrl, Action<MainViewModel> check) => UiThread.Run(() =>
    {
        var paths = new AppPaths(Path.Combine(folder, "Data"));
        paths.EnsureWritable();
        var profile = new GameProfile { Id = "test", DisplayName = "Test", ModPageUrl = modPageUrl };
        check(new MainViewModel(profile, paths, new ArtSet(null, null)));
    });

    private ModItemViewModel Row(MainViewModel model, string? source, bool recommended = false) =>
        new(model, new ModRecord { Id = "a", Name = "Travel Anywhere", Version = "1.0.1", SourceFileName = source, Recommended = recommended }, Path.Combine(folder, "library"));

    private ModDetailsViewModel Details(MainViewModel model, string? source, bool recommended = false) =>
        new(model, new ModRecord { Id = "a", Name = "Travel Anywhere", Version = "1.0.1", SourceFileName = source, Recommended = recommended }, [], [], folder);

    [Fact]
    public void A_download_from_the_mod_site_links_its_page() => With("https://example.test/game/mods/{id}", model =>
    {
        Assert.Equal("https://example.test/game/mods/61", Row(model, SiteDownload).ModPageUrl);
        Assert.False(Row(model, SiteDownload).ShowsNoModPage);
        Assert.Equal("https://example.test/game/mods/61", Details(model, SiteDownload).ModPageUrl);
        // A page on the mod site keeps the plain words; only a page on openmods reads as its name, in orange.
        Assert.False(Row(model, SiteDownload).IsOpenModsPage);
        Assert.Equal("Open mod page", Row(model, SiteDownload).ModPageText);
        Assert.Equal("Open mod page", Details(model, SiteDownload).ModPageText);
    });

    [Fact]
    public void Another_download_has_no_link_and_says_so() => With("https://example.test/game/mods/{id}", model =>
    {
        Assert.Null(Row(model, "Travel Anywhere.zip").ModPageUrl);
        Assert.True(Row(model, "Travel Anywhere.zip").ShowsNoModPage);
        Assert.Null(Row(model, null).ModPageUrl);
        Assert.Null(Details(model, "PBZModLoader-2.1.0-win64.zip").ModPageUrl);
    });

    [Fact]
    public void A_mod_on_the_card_shows_neither_even_from_the_mod_site() => With("https://example.test/game/mods/{id}", model =>
    {
        Assert.Null(Row(model, SiteDownload, recommended: true).ModPageUrl);
        Assert.False(Row(model, SiteDownload, recommended: true).ShowsNoModPage);
        Assert.False(Row(model, "PBZModLoader-2.1.0-win64.zip", recommended: true).ShowsNoModPage);
        Assert.Null(Details(model, SiteDownload, recommended: true).ModPageUrl);
    });

    [Fact]
    public void The_update_box_shows_what_the_check_found() => With("https://example.test/game/mods/{id}", model =>
    {
        model.ShowModUpdates(Pages(("61", "1.0.2")));
        var row = Row(model, SiteDownload);
        model.Mods.Add(row);

        Assert.True(row.IsUpdateAvailable);
        Assert.False(row.IsUpToDate);
        Assert.Contains("1.0.2", row.UpdateHint);
        Assert.True(model.HasModUpdates);
        Assert.True(model.IsAnyUpdateAvailable);
        Assert.True(Details(model, SiteDownload).IsUpdateAvailable);

        model.ShowModUpdates(Pages(("61", "1.0.1")));
        var again = Row(model, SiteDownload);

        Assert.False(again.IsUpdateAvailable);
        Assert.True(again.IsUpToDate);
    });

    [Fact]
    public void Without_a_result_or_a_mod_page_the_box_stays_empty() => With("https://example.test/game/mods/{id}", model =>
    {
        Assert.False(Row(model, SiteDownload).IsUpdateAvailable);
        Assert.False(Row(model, SiteDownload).IsUpToDate);

        model.ShowModUpdates(Pages(("61", "1.0.2")));

        Assert.False(Row(model, "Travel Anywhere.zip").IsUpdateAvailable);
        Assert.False(Row(model, SiteDownload, recommended: true).IsUpdateAvailable);
        Assert.False(Row(model, SiteDownload, recommended: true).HasModPageLine);
        Assert.True(Row(model, "Travel Anywhere.zip").HasModPageLine);
    });

    [Fact]
    public void A_mod_update_shows_at_once_from_the_page_version_already_known() => With("https://example.test/game/mods/{id}", model =>
    {
        model.ShowModUpdates(Pages(("61", "1.0.2")));
        Assert.True(Row(model, SiteDownload).IsUpdateAvailable);

        // The updated mod's new download has the same number on the site and the page's version.
        var updated = new ModItemViewModel(model, new ModRecord { Id = "b", Name = "Travel Anywhere", Version = "1.0.2",
            SourceFileName = "Travel Anywhere 61 1.0.2 2026-11-10T10-00Z AbCdEf123.zip" }, Path.Combine(folder, "library"));

        Assert.True(updated.IsUpToDate);
    });

    [Fact]
    public void A_mod_without_a_version_in_the_list_uses_the_one_in_its_download_name() => With("https://example.test/game/mods/{id}", model =>
    {
        // Installed on 2026-10-31, before the app read a name that ends in "(1)": no version in the list.
        const string source = "Cutscene Skip 1.0.0 32 1.0.0 2026-10-29T07-06Z Q3IfNbyi9(1).zip";
        model.ShowModUpdates(Pages(("32", "1.0.0")));

        var row = new ModItemViewModel(model, new ModRecord { Id = "c", Name = "Cutscene Skip", SourceFileName = source }, Path.Combine(folder, "library"));

        Assert.True(row.IsUpToDate);

        model.ShowModUpdates(Pages(("32", "1.1.0")));
        var newer = new ModItemViewModel(model, new ModRecord { Id = "c", Name = "Cutscene Skip", SourceFileName = source }, Path.Combine(folder, "library"));

        Assert.True(newer.IsUpdateAvailable);
        Assert.Contains("you have 1.0.0", newer.UpdateHint);
    });

    [Fact]
    public void A_mod_the_player_keeps_shows_not_tracked_instead_of_a_result() => With("https://example.test/game/mods/{id}", model =>
    {
        model.ShowModUpdates(Pages(("61", "1.0.2")));
        var record = new ModRecord { Id = "a", Name = "Travel Anywhere", Version = "1.0.1", SourceFileName = SiteDownload, NotTracked = true };

        var row = new ModItemViewModel(model, record, Path.Combine(folder, "library"));
        model.Mods.Add(row);

        Assert.True(row.IsNotTracked);
        Assert.False(row.IsUpdateAvailable);
        Assert.False(row.IsUpToDate);
        Assert.False(model.HasModUpdates);
        Assert.False(new ModDetailsViewModel(model, record, [], [], folder).TrackUpdates);
        // Without a mod page there is nothing to track.
        Assert.False(new ModItemViewModel(model, new ModRecord { Id = "b", Name = "Own", SourceFileName = "Own.zip", NotTracked = true }, folder).IsNotTracked);
    });

    [Fact]
    public void The_check_at_start_is_on_until_the_player_turns_it_off() => With("https://example.test/game/mods/{id}", model =>
    {
        Assert.True(model.CheckModUpdatesAtStart);

        model.CheckModUpdatesAtStart = false;

        Assert.False(AppSettings.Load(new AppPaths(Path.Combine(folder, "Data"))).CheckModUpdatesAtStart);
    });

    [Fact]
    public void The_button_comes_back_after_its_wait_even_when_the_timer_fires_early() => UiThread.Run(() =>
    {
        var paths = new AppPaths(Path.Combine(folder, "Data"));
        paths.EnsureWritable();
        var profile = new GameProfile
        {
            Id = "test", DisplayName = "Test", ModPageUrl = "https://example.test/game/mods/{id}",
            ModSiteApiUrl = "https://api.example.test/graphql", ModSiteGameId = "1",
        };
        var model = new MainViewModel(profile, paths, new ArtSet(null, null));
        Assert.True(model.CanCheckModUpdates);

        // The check at start: the button spins, then is ready at once, so the first visit never finds it grey.
        model.BeginModUpdateCheck(byButton: false);
        Assert.True(model.IsCheckingModUpdates);
        Assert.False(model.CanCheckModUpdates);
        model.EndModUpdateCheck();
        Assert.True(model.CanCheckModUpdates);

        // A press: after the check, the button waits.
        model.BeginModUpdateCheck(byButton: true);
        model.EndModUpdateCheck();
        Assert.False(model.CanCheckModUpdates);

        // A check a second short of 30 seconds ago: the timer fired early. A second, not less: on a busy machine the
        // test itself may take a few milliseconds to get to the tick.
        model.lastModUpdateCheck = Environment.TickCount64 - 29_000;
        model.OnModUpdateWaitTick();
        Assert.False(model.CanCheckModUpdates);
        Assert.Equal("You can check again 30 seconds after the last check.", model.CheckModUpdatesHint);

        model.lastModUpdateCheck = Environment.TickCount64 - 30_100;
        model.OnModUpdateWaitTick();
        Assert.True(model.CanCheckModUpdates);
        Assert.True(model.CheckModUpdatesCommand.CanExecute(null));
        Assert.Equal("Check for updates", model.CheckModUpdatesHint);
    });

    [Fact]
    public void A_game_without_the_mod_sites_api_checks_only_the_mods_from_openmods() => With("https://example.test/game/mods/{id}", model =>
    {
        Assert.False(model.HasModSite);
        Assert.Equal(AppInfo.IsCatalogOn, model.HasUpdateCheck);
        Assert.Equal(AppInfo.IsCatalogOn, model.CanCheckModUpdates);
        Assert.Equal(AppInfo.IsCatalogOn, model.CheckModUpdatesCommand.CanExecute(null));
    });

    private const string OpenModsPage = "https://openmods.nexus/mods/kintaromodding/phantombladezero_passiveregen";

    /// <summary>A mod installed from openmods, as the one-click install records it.</summary>
    private static ModRecord FromOpenMods(string version = "1.2", string? page = OpenModsPage, bool notTracked = false) => new()
    {
        Id = "p",
        Name = "Passive Regen",
        Version = version,
        SourceFileName = "phantombladezero_passiveregen_1.2.zip",
        CatalogId = "kintaromodding/phantombladezero_passiveregen",
        CatalogPage = page,
        NotTracked = notTracked,
    };

    private ModItemViewModel CatalogRow(MainViewModel model, ModRecord record) => new(model, record, Path.Combine(folder, "library"));

    private ModDetailsViewModel CatalogDetails(MainViewModel model, ModRecord record) => new(model, record, [], [], folder);

    /// <summary>The openmods list as the page reads it, with Passive Regen at this release.</summary>
    private static void ListOpenMods(MainViewModel model, string tag, string page = OpenModsPage) =>
        model.ShowCatalogList(UEManager.Core.Catalog.OpenMods.ParseList($$"""
            { "openmods": 1, "mods": [ { "name": "Passive Regen", "page": "{{page}}",
              "source": "https://github.com/KintaroModding/phantombladezero_passiveregen",
              "release": { "tag": "{{tag}}", "files": [ { "name": "phantombladezero_passiveregen.zip", "size": 1, "url": "https://example.test/p.zip" } ] } } ] }
            """));

    [Fact]
    public void A_mod_from_openmods_links_its_stored_page_before_the_list_is_read() => With("https://example.test/game/mods/{id}", model =>
    {
        var row = CatalogRow(model, FromOpenMods());

        Assert.Equal(OpenModsPage, row.ModPageUrl);
        Assert.False(row.ShowsNoModPage);
        Assert.False(row.IsUpdateAvailable);
        Assert.False(row.IsUpToDate);
        Assert.Equal(OpenModsPage, CatalogDetails(model, FromOpenMods()).ModPageUrl);
        // The link reads "openmods.nexus", in orange, so it stands apart from a mod site's page.
        Assert.True(row.IsOpenModsPage);
        Assert.Equal("openmods.nexus", row.ModPageText);
        Assert.True(CatalogDetails(model, FromOpenMods()).IsOpenModsPage);
        Assert.Equal("openmods.nexus", CatalogDetails(model, FromOpenMods()).ModPageText);
    });

    [Fact]
    public void A_mod_from_openmods_takes_its_page_from_the_list_once_read() => With("https://example.test/game/mods/{id}", model =>
    {
        ListOpenMods(model, "v1.2", page: "https://openmods.nexus/mods/kintaromodding/moved");

        Assert.Equal("https://openmods.nexus/mods/kintaromodding/moved", CatalogRow(model, FromOpenMods(page: null)).ModPageUrl);
    });

    [Fact]
    public void A_newer_release_on_openmods_offers_the_one_click_update() => With("https://example.test/game/mods/{id}", model =>
    {
        ListOpenMods(model, "v1.3");
        var row = CatalogRow(model, FromOpenMods());

        Assert.True(row.IsUpdateAvailable);
        Assert.Equal("Install update", row.UpdateBoxText);
        Assert.Equal("Version 1.3 is on openmods; you have 1.2. A click installs it.", row.UpdateHint);
        Assert.Equal("Update available", Row(model, SiteDownload).UpdateBoxText);
    });

    [Fact]
    public void The_same_release_on_openmods_is_up_to_date_and_a_kept_version_is_not_tracked() => With("https://example.test/game/mods/{id}", model =>
    {
        ListOpenMods(model, "v1.2");
        var row = CatalogRow(model, FromOpenMods());
        var kept = CatalogRow(model, FromOpenMods(notTracked: true));

        Assert.True(row.IsUpToDate);
        Assert.Equal("openmods has version 1.2, the one you have.", row.UpdateHint);
        Assert.True(kept.IsNotTracked);
        Assert.False(kept.IsUpdateAvailable);
        Assert.False(kept.IsUpToDate);
    });

    [Fact]
    public void A_game_without_a_page_address_has_no_link() => With(null, model =>
    {
        Assert.Null(Row(model, SiteDownload).ModPageUrl);
        Assert.Null(Details(model, SiteDownload).ModPageUrl);
    });
}
