using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>Mods whose paks have the same file names, such as two styles of one outfit: an update, or both kept apart.</summary>
public class SameNameModsTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly ModManager manager;
    private readonly GameInstall game;

    public SameNameModsTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")), _ => false);
    }

    public void Dispose() => folder.Dispose();

    private string Deployed(params string[] below) => Path.Combine([game.ModsPath, .. below]);

    private InstallPreview Download(string name, params string[] paks)
    {
        foreach (var pak in paks)
            folder.File($@"Downloads\{name}\{pak}_P.pak", $"{pak} of {name}");
        return manager.Inspect(folder.Combine("Downloads", name));
    }

    private ModRecord Install(string name, string? ownFolder, params string[] paks)
    {
        using var preview = Download(name, paks);
        var parts = preview.Packages.ToList();
        var check = manager.Check(preview, parts, null, ownFolder);
        return manager.Install(preview, parts, name, null, check.Replaces is not null, ownFolder: ownFolder);
    }

    [Fact]
    public void Without_a_choice_the_second_mod_updates_the_first_as_before()
    {
        Install("Red", null, "Skin");
        Install("Blue", null, "Skin");

        Assert.Equal("Blue", Assert.Single(manager.Mods).Name);
        Assert.Equal("Skin of Blue", File.ReadAllText(Deployed("Skin_P.pak")));
    }

    [Fact]
    public void Keeping_both_puts_the_second_in_a_folder_of_its_own()
    {
        var red = Install("Red", null, "Skin");
        var blue = Install("Blue", manager.NewModFolder("Blue"), "Skin");

        Assert.Equal(["Red", "Blue"], manager.Mods.Select(m => m.Name));
        Assert.Equal("Skin of Red", File.ReadAllText(Deployed("Skin_P.pak")));
        Assert.Equal("Skin of Blue", File.ReadAllText(Deployed("Blue", "Skin_P.pak")));
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(red));
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(blue));
        Assert.Empty(manager.UnmanagedFiles());
    }

    [Fact]
    public void A_mod_in_its_folder_turns_off_and_on_and_leaves_with_its_folder()
    {
        var red = Install("Red", null, "Skin");
        var blue = Install("Blue", "Blue", "Skin");

        manager.SetEnabled(blue.Id, false);
        Assert.False(Directory.Exists(Deployed("Blue")));
        Assert.True(File.Exists(Deployed("Skin_P.pak")));

        manager.SetEnabled(blue.Id, true);
        Assert.Equal("Skin of Blue", File.ReadAllText(Deployed("Blue", "Skin_P.pak")));

        manager.Uninstall(blue.Id);
        Assert.False(Directory.Exists(Deployed("Blue")));
        Assert.True(Directory.Exists(game.ModsPath));
        Assert.Equal(red.Id, Assert.Single(manager.Mods).Id);
        Assert.Equal("Skin of Red", File.ReadAllText(Deployed("Skin_P.pak")));
    }

    [Fact]
    public void Both_mods_can_be_updated_each_in_its_own_place()
    {
        var red = Install("Red", null, "Skin");
        var blue = Install("Blue", "Blue", "Skin");

        using var preview = Download("Blue 2", "Skin");
        var parts = preview.Packages.ToList();
        var found = manager.SameNameMods(parts);

        Assert.Equal(["Red", "Blue"], found.Select(f => f.Mod.Name));
        Assert.Equal([null, "Blue"], found.Select(f => f.Folder));
        Assert.All(found, f => Assert.True(f.CanUpdate));

        var check = manager.Check(preview, parts, null, "Blue");
        Assert.Equal(blue.Id, check.Replaces!.Id);
        manager.Install(preview, parts, "Blue", "2.0", replace: true, ownFolder: "Blue");

        Assert.Equal(2, manager.Mods.Count);
        Assert.Equal("Skin of Blue 2", File.ReadAllText(Deployed("Blue", "Skin_P.pak")));
        Assert.Equal("Skin of Red", File.ReadAllText(Deployed("Skin_P.pak")));
        Assert.Equal("2.0", manager.Mods.Single(m => m.Name == "Blue").Version);
        Assert.Contains(manager.Mods, m => m.Id == red.Id);
    }

    [Fact]
    public void Keeping_both_also_saves_a_mod_that_shares_only_one_of_two_paks()
    {
        Install("Red", null, "Common", "Red");
        Install("Blue", "Blue", "Common", "Blue");

        Assert.Equal(2, manager.Mods.Count);
        Assert.True(File.Exists(Deployed("Red_P.pak")));
        Assert.True(File.Exists(Deployed("Common_P.pak")));
        Assert.True(File.Exists(Deployed("Blue", "Common_P.pak")));
    }

    [Fact]
    public void A_mod_that_shares_too_little_cannot_be_updated_but_both_can_be_kept()
    {
        Install("Big", null, "One", "Two", "Three");

        using var preview = Download("Small", "One");
        var parts = preview.Packages.ToList();

        Assert.False(Assert.Single(manager.SameNameMods(parts)).CanUpdate);
        Assert.False(manager.Check(preview, parts).CanInstall);
        var keep = manager.Check(preview, parts, null, "Small");
        Assert.True(keep.CanInstall);
        Assert.Null(keep.Replaces);
    }

    [Fact]
    public void With_other_names_or_from_the_catalog_there_is_no_choice()
    {
        Install("Red", null, "Skin");

        using var other = Download("Blue", "Other");
        Assert.Empty(manager.SameNameMods(other.Packages.ToList()));

        using var same = Download("Green", "Skin");
        Assert.Single(manager.SameNameMods(same.Packages.ToList()));
        Assert.Empty(manager.SameNameMods(same.Packages.ToList(), "someone/green"));
    }

    [Fact]
    public void A_blueprint_mod_of_the_same_name_is_no_choice()
    {
        folder.File(@"Downloads\Logic\LogicMods\Skin_P.pak", "a blueprint mod");
        using (var logic = manager.Inspect(folder.Combine("Downloads", "Logic")))
            manager.Install(logic, logic.Packages.ToList(), "Logic", null);

        using var preview = Download("Red", "Skin");

        Assert.Empty(manager.SameNameMods(preview.Packages.ToList()));
    }

    [Fact]
    public void The_folder_name_is_safe_and_never_one_that_is_taken()
    {
        Assert.Equal("Blue", manager.NewModFolder("Blue"));
        Assert.DoesNotContain(':', manager.NewModFolder("Blue: the best?"));

        var blue = Install("Blue", "Blue", "Skin");
        Assert.Equal("Blue 2", manager.NewModFolder("Blue"));

        // Off, the mod has no folder in the game, and its name stays taken.
        manager.SetEnabled(blue.Id, false);
        Assert.Equal("blue 2", manager.NewModFolder("blue"));

        Directory.CreateDirectory(Deployed("Blue 2"));
        Assert.Equal("Blue 3", manager.NewModFolder("Blue"));
    }

    [Fact]
    public void A_settings_file_in_the_folder_stays_a_settings_file()
    {
        Install("Red", null, "Skin");
        folder.File(@"Downloads\Blue\Skin_P.pak", "pak");
        folder.File(@"Downloads\Blue\Skin.json", "{}");
        using var preview = manager.Inspect(folder.Combine("Downloads", "Blue"));

        var mod = manager.Install(preview, preview.Packages.ToList(), "Blue", null, ownFolder: "Blue");

        Assert.Equal("Skin.json", SettingsFiles.Name(Assert.Single(mod.Files, f => f.IsSettingsFile).FileName));
        Assert.True(File.Exists(Deployed("Blue", "Skin.json")));
    }

    [Fact]
    public void Other_kinds_of_files_ignore_the_folder()
    {
        Assert.Equal("Blue/Skin_P.pak", ModManager.InFolder("Blue", DeployTarget.Mods, "Skin_P.pak"));
        Assert.Equal("Skin_P.pak", ModManager.InFolder(null, DeployTarget.Mods, "Skin_P.pak"));
        Assert.Equal("Skin_P.pak", ModManager.InFolder("Blue", DeployTarget.LogicMods, "Skin_P.pak"));
        Assert.Equal("TestGame/Saved/Keys.sav", ModManager.InFolder("Blue", DeployTarget.InstallRoot, "TestGame/Saved/Keys.sav"));
    }

    [Fact]
    public void A_download_with_the_same_mod_number_is_the_installed_mods_update()
    {
        // The old mod: a pak, from a download that kept the site's file name.
        const string oldName = "Auto Collect Loot 70 1.3.0 2026-10-31T03-18Z pZyhlgONj";
        folder.File($@"Downloads\{oldName}\zzz_PBZAutoCollect_P.pak", "pak");
        folder.File($@"Downloads\{oldName}\zzz_PBZAutoCollect_P.utoc", "utoc");
        folder.File($@"Downloads\{oldName}\zzz_PBZAutoCollect_P.ucas", "ucas");
        using (var oldPreview = manager.Inspect(folder.Combine("Downloads", oldName)))
            manager.Install(oldPreview, oldPreview.Packages.ToList(), "Auto Collect Loot", "1.3.0");

        // The new mod: a script with the same site number, but different file names.
        const string newName = "Auto Collect Loot 70 1.7.2 2026-11-04T23-30Z hs0itkqF1";
        folder.File($@"Downloads\{newName}\TestGame\Content\JavaScript\PBZModsMenu\Mods\PBZAutoCollect\main.js", "the mod");
        using var preview = manager.Inspect(folder.Combine("Downloads", newName));

        Assert.Equal("70", ModNaming.SiteModId(preview.SourceFileName));
        Assert.Equal("70", ModNaming.SiteModId(manager.Mods.Single().SourceFileName));
        Assert.Single(preview.Packages);
        Assert.NotNull(manager.Mods.FirstOrDefault(m => ModNaming.SiteModId(m.SourceFileName) == "70"));
        var check = manager.Check(preview, preview.Packages.ToList());
        Assert.NotNull(check.Replaces);
        Assert.Equal("Auto Collect Loot", check.Replaces!.Name);

        var update = Assert.Single(manager.SameNameMods(preview.Packages.ToList(), siteId: "70"));
        Assert.Equal("Auto Collect Loot", update.Mod.Name);
        Assert.True(update.CanUpdate);
    }
}
