using System.IO;
using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>
/// The install review: for a loader's download with other entry DLLs it ticks one, as only one entry may be in the game;
/// and it names the game the mod goes into.
/// </summary>
public sealed class InstallDialogEntryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "uemm-entries-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string File(string relative)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, relative);
        return path;
    }

    private ModManager Manager()
    {
        File(@"Game\Engine\Binaries\Win64\placeholder");
        File(@"Game\TestGame\Content\Paks\placeholder");
        File(@"Game\TestGame\Binaries\Win64\TestGame-Win64-Shipping.exe");
        File(@"Game\TestGame.exe");
        var profile = new GameProfile { Id = "test-game", DisplayName = "Test Game", SteamAppId = "999999", ProjectFolder = "TestGame" };
        var game = GameLocator.Resolve(profile, Path.Combine(root, "Game"), GameStore.Manual)!;
        return new ModManager(game, new AppPaths(Path.Combine(root, "Data")));
    }

    [Fact]
    public void A_zip_for_two_loaders_waits_for_a_loader_and_then_keeps_the_choice()
    {
        File(@"Markers\TestGame\PBZModLoader\mods\Markers\main.js");
        File(@"Markers\TestGame\Content\JavaScript\PBZModsMenu\Mods\Markers\main.js");
        var manager = Manager();
        using (var preview = manager.Inspect(Path.Combine(root, "Markers")))
        {
            var dialog = new InstallDialogViewModel(manager, preview, () => { });

            Assert.Equal("PBZModLoader", Assert.Single(dialog.Packages, p => p.IsSelected).Label);
            Assert.False(dialog.InstallCommand.CanExecute(null));

            File(@"Game\TestGame\Content\JavaScript\PBZModsMenu\bootstrap.js");
            var menu = Assert.Single(dialog.Packages, p => p.Label == "Mods Menu");
            menu.IsSelected = true;
            Assert.True(dialog.InstallCommand.CanExecute(null));
            Assert.Single(dialog.Packages, p => p.IsSelected);

            manager.Install(preview, [menu.Package], "Markers", null);
        }

        using var next = manager.Inspect(Path.Combine(root, "Markers"));
        var update = new InstallDialogViewModel(manager, next, () => { });

        Assert.Equal("Mods Menu", Assert.Single(update.Packages, p => p.IsSelected).Label);
    }

    [Fact]
    public void The_review_names_the_game_the_mod_goes_into()
    {
        File(@"Blue\Blue_P.pak");
        var manager = Manager();
        using var preview = manager.Inspect(Path.Combine(root, "Blue"));

        Assert.Equal("Test Game", new InstallDialogViewModel(manager, preview, () => { }).GameName);
    }

    [Fact]
    public void A_zip_for_two_loaders_ticks_the_copy_for_the_loader_in_the_game()
    {
        File(@"Markers\TestGame\PBZModLoader\mods\Markers\main.js");
        File(@"Markers\TestGame\Content\JavaScript\PBZModsMenu\Mods\Markers\main.js");
        System.IO.File.WriteAllText(File(@"Game\TestGame\Binaries\Win64\version.dll"), "[PBZMOD]");
        var manager = Manager();
        using var preview = manager.Inspect(Path.Combine(root, "Markers"));

        var dialog = new InstallDialogViewModel(manager, preview, () => { });

        Assert.Equal("PBZModLoader", Assert.Single(dialog.Packages, p => p.IsSelected).Label);
        Assert.True(dialog.InstallCommand.CanExecute(null));
    }

    [Fact]
    public void The_review_ticks_the_usual_entry_only_and_a_choice_clears_it()
    {
        File(@"Loader\TestGame\Binaries\Win64\version.dll");
        foreach (var name in new[] { "dsound", "dxgi", "hid", "winmm" })
            File($@"Loader\alternative-entries\{name}\{name}.dll");
        var manager = Manager();
        using var preview = manager.Inspect(Path.Combine(root, "Loader"));

        var dialog = new InstallDialogViewModel(manager, preview, () => { });

        Assert.Equal(5, dialog.Packages.Count);
        var ticked = Assert.Single(dialog.Packages, p => p.IsSelected);
        Assert.Equal("version.dll", ticked.Package.Name);

        var hid = Assert.Single(dialog.Packages, p => p.Package.Name == "hid");
        Assert.False(hid.IsUncertain);
        hid.IsSelected = true;

        Assert.Same(hid, Assert.Single(dialog.Packages, p => p.IsSelected));
    }

    [Fact]
    public async Task A_new_mod_starts_without_tags_and_takes_what_the_player_types()
    {
        File(@"Sharp\Sharp_P.pak");
        var manager = Manager();
        using var preview = manager.Inspect(Path.Combine(root, "Sharp"));
        var dialog = new InstallDialogViewModel(manager, preview, () => { });

        Assert.Equal("", dialog.Tags);

        dialog.Tags = "Combat, UI";
        dialog.InstallCommand.Execute(null);

        Assert.Equal("Combat, UI", (await dialog.Result)!.Tags);
    }

    [Fact]
    public void An_update_starts_with_the_mods_tags_and_keeps_what_the_player_types()
    {
        File(@"Sharp\Sharp_P.pak");
        File(@"Sharp\x2\Sharp_P.pak");
        var manager = Manager();
        using var preview = manager.Inspect(Path.Combine(root, "Sharp"));
        manager.Install(preview, [preview.Packages[0]], "Sharp", null, tags: ["Combat"]);

        using var next = manager.Inspect(Path.Combine(root, "Sharp"));
        var dialog = new InstallDialogViewModel(manager, next, () => { });
        Assert.Equal("Combat", dialog.Tags);

        dialog.Tags = "Maps";
        dialog.Packages[1].IsSelected = true;

        Assert.Equal("Maps", dialog.Tags);
    }

    private (ModManager Manager, ModRecord Red) RedInstalled()
    {
        File(@"Red\Skin_P.pak");
        var manager = Manager();
        using var preview = manager.Inspect(Path.Combine(root, "Red"));
        return (manager, manager.Install(preview, preview.Packages.ToList(), "Red", null));
    }

    [Fact]
    public async Task Only_the_same_mod_page_ticks_keep_both_first_and_offers_the_update()
    {
        File(@"Auto Collect Loot 70 1.3.0 2026-10-31T03-18Z pZyhlgONj\zzz_PBZAutoCollect_P.pak");
        var manager = Manager();
        using (var old = manager.Inspect(Path.Combine(root, "Auto Collect Loot 70 1.3.0 2026-10-31T03-18Z pZyhlgONj")))
            manager.Install(old, old.Packages.ToList(), "Auto Collect Loot", "1.3.0");
        File(@"Auto Collect Loot 70 1.7.2 2026-11-04T23-30Z hs0itkqF1\TestGame\Content\JavaScript\PBZAutoCollect\main.js");
        using var preview = manager.Inspect(Path.Combine(root, "Auto Collect Loot 70 1.7.2 2026-11-04T23-30Z hs0itkqF1"));

        var dialog = new InstallDialogViewModel(manager, preview, () => { });

        Assert.Equal("An installed mod comes from the same mod page. Keep both, or update it:", dialog.PlacesTitle);
        Assert.Equal(["Keep both: install this as another mod", "Update \"Auto Collect Loot\""], dialog.Places.Select(p => p.Label));
        Assert.True(dialog.Places[0].IsSelected);
        Assert.Equal("Install", dialog.ConfirmText);
        Assert.Contains("same mod page as Auto Collect Loot", dialog.KeepBothText);

        dialog.Places[1].IsSelected = true;

        Assert.Equal("Update", dialog.ConfirmText);
        Assert.Null(dialog.KeepBothText);

        dialog.Places[0].IsSelected = true;
        dialog.InstallCommand.Execute(null);

        var choice = (await dialog.Result)!;
        Assert.False(choice.Replace);
        Assert.NotNull(choice.OwnFolder);
    }

    [Fact]
    public void An_update_keeps_the_name_the_player_gave_the_mod()
    {
        // Issue #20: a name set with Edit went back to the download's name on each update.
        var (manager, red) = RedInstalled();
        manager.Rename(red.Id, "My red skin", null, ["Outfits"]);
        File(@"Blue\Skin_P.pak");
        using var preview = manager.Inspect(Path.Combine(root, "Blue"));

        var dialog = new InstallDialogViewModel(manager, preview, () => { });

        Assert.Equal("Update", dialog.ConfirmText);
        Assert.Equal("My red skin", dialog.Name);

        // Keeping both makes another mod, which takes the download's name.
        dialog.Places[1].IsSelected = true;
        Assert.Equal("Blue", dialog.Name);
        Assert.Contains(dialog.Rows, row => row.Name.EndsWith("Blue", StringComparison.Ordinal));

        dialog.Places[0].IsSelected = true;
        Assert.Equal("My red skin", dialog.Name);
    }

    [Fact]
    public void An_update_of_a_mod_the_app_named_takes_the_new_downloads_name()
    {
        var (manager, _) = RedInstalled();
        File(@"Blue\Skin_P.pak");
        using var preview = manager.Inspect(Path.Combine(root, "Blue"));

        var dialog = new InstallDialogViewModel(manager, preview, () => { });

        Assert.Equal("Update", dialog.ConfirmText);
        Assert.Equal("Blue", dialog.Name);
    }

    [Fact]
    public void A_name_the_player_types_in_the_review_stays()
    {
        var (manager, red) = RedInstalled();
        manager.Rename(red.Id, "My red skin", null);
        File(@"Blue\Skin_P.pak");
        using var preview = manager.Inspect(Path.Combine(root, "Blue"));
        var dialog = new InstallDialogViewModel(manager, preview, () => { });

        dialog.Name = "Typed";
        dialog.Places[1].IsSelected = true;
        dialog.Places[0].IsSelected = true;

        Assert.Equal("Typed", dialog.Name);
    }

    [Fact]
    public void A_download_with_other_file_names_has_no_choice()
    {
        var (manager, _) = RedInstalled();
        File(@"Blue\Other_P.pak");
        using var preview = manager.Inspect(Path.Combine(root, "Blue"));

        var dialog = new InstallDialogViewModel(manager, preview, () => { });

        Assert.False(dialog.HasPlaces);
        Assert.Null(dialog.KeepBothText);
    }

    [Fact]
    public async Task The_same_file_names_offer_an_update_first_as_before()
    {
        var (manager, red) = RedInstalled();
        File(@"Blue\Skin_P.pak");
        using var preview = manager.Inspect(Path.Combine(root, "Blue"));

        var dialog = new InstallDialogViewModel(manager, preview, () => { });

        Assert.True(dialog.HasPlaces);
        Assert.Equal("An installed mod has the same file names. Update it, or keep both:", dialog.PlacesTitle);
        Assert.Equal(["Update \"Red\"", "Keep both: install this as another mod, in a folder of its own in ~mods"], dialog.Places.Select(p => p.Label));
        Assert.True(dialog.Places[0].IsSelected);
        Assert.Equal("Update", dialog.ConfirmText);
        Assert.Contains("Red", dialog.ReplaceText);
        Assert.Null(dialog.KeepBothText);

        dialog.InstallCommand.Execute(null);

        var choice = (await dialog.Result)!;
        Assert.True(choice.Replace);
        Assert.Null(choice.OwnFolder);
        Assert.Equal(red.Id, manager.Mods.Single().Id);
    }

    [Fact]
    public async Task Keep_both_warns_and_shows_the_folder_and_installs_as_a_new_mod()
    {
        var (manager, _) = RedInstalled();
        File(@"Blue\Skin_P.pak");
        using var preview = manager.Inspect(Path.Combine(root, "Blue"));
        var dialog = new InstallDialogViewModel(manager, preview, () => { });

        dialog.Places[1].IsSelected = true;

        Assert.False(dialog.Places[0].IsSelected);
        Assert.Equal("Install", dialog.ConfirmText);
        Assert.Null(dialog.ReplaceText);
        Assert.Contains("only one style or version", dialog.KeepBothText);
        Assert.Contains("Red", dialog.KeepBothText);
        Assert.Contains(dialog.Rows, row => row.Name.EndsWith("Blue", StringComparison.Ordinal));

        dialog.Name = "Blue style";
        Assert.Contains(dialog.Rows, row => row.Name.EndsWith("Blue style", StringComparison.Ordinal));

        dialog.InstallCommand.Execute(null);

        var choice = (await dialog.Result)!;
        Assert.False(choice.Replace);
        Assert.Equal("Blue style", choice.OwnFolder);
        manager.Install(preview, choice.Packages, choice.Name, choice.Version, choice.Replace, ownFolder: choice.OwnFolder);
        Assert.Equal(2, manager.Mods.Count);
    }

    [Fact]
    public void Back_to_update_takes_the_warning_and_the_folder_away()
    {
        var (manager, _) = RedInstalled();
        File(@"Blue\Skin_P.pak");
        using var preview = manager.Inspect(Path.Combine(root, "Blue"));
        var dialog = new InstallDialogViewModel(manager, preview, () => { });

        dialog.Places[1].IsSelected = true;
        dialog.Places[0].IsSelected = true;

        Assert.False(dialog.Places[1].IsSelected);
        Assert.Null(dialog.KeepBothText);
        Assert.Equal("Update", dialog.ConfirmText);
    }

    /// <summary>Two character mods of one author that bring the same framework, as Mo Yuan and Play as Bai Chilian.</summary>
    private void Character(string name)
    {
        foreach (var (relative, text) in new[]
                 {
                     ("Bridge_P.pak", "bridge"), ($"{name}_P.pak", name), (@"Core\Scripts\main.lua", "core"),
                     ($@"Core\Scripts\Profile_{name}.lua", name), (@"Core\enabled.txt", ""),
                 })
        {
            var path = Path.Combine(root, name, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, text);
        }
    }

    [Fact]
    public async Task Mods_with_the_same_framework_offer_to_keep_both_and_share_it()
    {
        var manager = Manager();
        Character("Mo");
        Character("Bai");
        using (var mo = manager.Inspect(Path.Combine(root, "Mo")))
            manager.Install(mo, mo.Packages.ToList(), "Mo", null);
        using var preview = manager.Inspect(Path.Combine(root, "Bai"));

        var dialog = new InstallDialogViewModel(manager, preview, () => { });

        // One choice: update Mo (first, as before), or keep both and share. A folder of its own would not keep the
        // framework's UE4SS files apart, so it is no choice here.
        Assert.True(dialog.HasPlaces);
        Assert.Equal(["Update \"Mo\"", "Keep both, and share the 3 files that are the same in both"], dialog.Places.Select(p => p.Label));
        Assert.True(dialog.Places[0].IsSelected);
        Assert.False(dialog.ShareFiles);
        Assert.Null(dialog.ShareNote);
        Assert.Equal("Update", dialog.ConfirmText);

        dialog.Places[1].IsSelected = true;

        Assert.False(dialog.Places[0].IsSelected);
        Assert.True(dialog.ShareFiles);
        Assert.Null(dialog.ReplaceText);
        Assert.Null(dialog.KeepBothText);
        Assert.Equal("Install", dialog.ConfirmText);
        Assert.Contains("\"Mo\"", dialog.ShareNote);
        Assert.Contains("only when the mods' authors say they work together", dialog.ShareNote);
        Assert.True(dialog.InstallCommand.CanExecute(null));

        // Back to the update, and then sharing again, which installs it beside Mo.
        dialog.Places[0].IsSelected = true;
        Assert.Equal("Update", dialog.ConfirmText);
        Assert.Null(dialog.ShareNote);
        dialog.Places[1].IsSelected = true;
        dialog.InstallCommand.Execute(null);

        var choice = (await dialog.Result)!;
        Assert.True(choice.ShareFiles);
        Assert.False(choice.Replace);
        Assert.Null(choice.OwnFolder);
        manager.Install(preview, choice.Packages, choice.Name, choice.Version, choice.Replace, ownFolder: choice.OwnFolder,
            shareFiles: choice.ShareFiles);
        Assert.Equal(["Bai", "Mo"], manager.Mods.Select(m => m.Name).Order(StringComparer.Ordinal));
        Assert.All(manager.Mods, m => Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(m)));
    }

    [Fact]
    public void A_mod_that_shares_little_offers_sharing_without_a_choice_that_cannot_work()
    {
        // Few files in common, so not an update of Mo: keeping both in a folder of its own would still clash with Mo's
        // UE4SS files, so only sharing is offered, and nothing is ticked until the player chooses it.
        var manager = Manager();
        Character("Mo");
        using (var mo = manager.Inspect(Path.Combine(root, "Mo")))
            manager.Install(mo, mo.Packages.ToList(), "Mo", null);
        foreach (var (relative, text) in new[] { ("Big_P.pak", "big"), ("Big2_P.pak", "big 2"), (@"Core\Scripts\main.lua", "core"), (@"Core\enabled.txt", ""), (@"Core\Scripts\Big.lua", "big") })
        {
            var path = Path.Combine(root, "Big", relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, text);
        }
        using var preview = manager.Inspect(Path.Combine(root, "Big"));

        var dialog = new InstallDialogViewModel(manager, preview, () => { });

        Assert.Equal("An installed mod has some of the same files. Update it, or keep both:", dialog.PlacesTitle);
        var share = Assert.Single(dialog.Places);
        Assert.True(share.Shares);
        Assert.False(share.IsSelected);
        Assert.False(dialog.InstallCommand.CanExecute(null));

        share.IsSelected = true;

        Assert.True(dialog.InstallCommand.CanExecute(null));
    }

    [Fact]
    public void A_third_mod_that_shares_the_framework_already_says_so_with_nothing_to_choose()
    {
        var manager = Manager();
        foreach (var name in new[] { "Mo", "Bai", "Yu" })
            Character(name);
        using (var mo = manager.Inspect(Path.Combine(root, "Mo")))
            manager.Install(mo, mo.Packages.ToList(), "Mo", null);
        using (var bai = manager.Inspect(Path.Combine(root, "Bai")))
            manager.Install(bai, bai.Packages.ToList(), "Bai", null, shareFiles: true);
        using var preview = manager.Inspect(Path.Combine(root, "Yu"));

        var dialog = new InstallDialogViewModel(manager, preview, () => { });

        Assert.False(dialog.HasPlaces);
        Assert.Contains("as they do already", dialog.AlreadySharedText);
        Assert.Equal("Install", dialog.ConfirmText);
        Assert.True(dialog.InstallCommand.CanExecute(null));
    }

    [Fact]
    public void A_mod_that_shares_too_little_stops_until_the_player_keeps_both()
    {
        File(@"Big\One_P.pak");
        File(@"Big\Two_P.pak");
        File(@"Big\Three_P.pak");
        var manager = Manager();
        using (var big = manager.Inspect(Path.Combine(root, "Big")))
            manager.Install(big, big.Packages.ToList(), "Big", null);
        File(@"Small\One_P.pak");
        using var preview = manager.Inspect(Path.Combine(root, "Small"));

        var dialog = new InstallDialogViewModel(manager, preview, () => { });

        var keep = Assert.Single(dialog.Places);
        Assert.True(keep.KeepsBoth);
        Assert.False(keep.IsSelected);
        Assert.False(dialog.InstallCommand.CanExecute(null));

        keep.IsSelected = true;

        Assert.True(dialog.InstallCommand.CanExecute(null));
    }

    [Fact]
    public void A_download_from_the_catalog_has_no_choice()
    {
        var (manager, _) = RedInstalled();
        File(@"Blue\Skin_P.pak");
        using var preview = manager.Inspect(Path.Combine(root, "Blue"));

        var dialog = new InstallDialogViewModel(manager, preview, () => { }, new CatalogSource("someone/blue", "Blue", "1.0"));

        Assert.False(dialog.HasPlaces);
    }
}
