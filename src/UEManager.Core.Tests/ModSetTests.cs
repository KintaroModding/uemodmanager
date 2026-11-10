using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>Named sets of mods that the player turns on together.</summary>
public class ModSetTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly GameInstall game;
    private readonly ModManager manager;

    public ModSetTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")));
    }

    public void Dispose() => folder.Dispose();

    private ModRecord Install(string name, string content = "", bool replace = false)
    {
        folder.File($@"Downloads\{name}\{name}_P.pak", name + content);
        using var preview = manager.Inspect(folder.Combine("Downloads", name));
        return manager.Install(preview, preview.Packages.ToList(), name, null, replace);
    }

    private string Deployed(string name) => Path.Combine(game.ModsPath, name + "_P.pak");

    private IEnumerable<string> Enabled() => manager.Mods.Where(m => m.Enabled).Select(m => m.Name).Order();

    /// <summary>A UE4SS mod, as the recommended one is.</summary>
    private ModRecord InstallUe4ssMod(string folderName, string version = "1.0", string? catalogId = null, bool replace = false)
    {
        folder.File($@"Downloads\{folderName}_{version}\ue4ss\Mods\{folderName}\enabled.txt", "");
        folder.File($@"Downloads\{folderName}_{version}\ue4ss\Mods\{folderName}\Scripts\main.lua", "main " + version);
        using var preview = manager.Inspect(folder.Combine("Downloads", $"{folderName}_{version}"));
        return manager.Install(preview, preview.Packages.ToList(), folderName, version, replace, catalogId);
    }

    [Fact]
    public void Sets_leave_the_recommended_mod_as_it_is()
    {
        var sharp = Install("Sharp");
        manager.SaveSet("Before the fix");
        var fix = InstallUe4ssMod("TheFix");
        manager.MarkRecommended([("KintaroModding/the_fix", "TheFix")]);

        // A set saved before the fix came does not turn it off, and a new set does not hold it.
        manager.SetEnabled(sharp.Id, false);
        manager.ApplySet("Before the fix");
        Assert.Equal(["Sharp", "TheFix"], Enabled());
        Assert.DoesNotContain(fix.Id, manager.SaveSet("With the fix").ModIds);

        // Turned off by the player, it stays off when a set is applied.
        manager.SetEnabled(fix.Id, false);
        manager.ApplySet("With the fix");
        Assert.Equal(["Sharp"], Enabled());
    }

    [Fact]
    public void The_recommended_mod_is_found_by_its_repo_or_its_folder_and_loses_the_mark_when_the_manifest_drops_it()
    {
        Install("Sharp");
        var byHand = InstallUe4ssMod("TheFix");
        Assert.False(byHand.Recommended);

        manager.MarkRecommended([("KintaroModding/the_fix", "thefix")]);

        var marked = Assert.Single(manager.Mods, m => m.Recommended);
        Assert.Equal("TheFix", marked.Name);
        Assert.Equal("KintaroModding/the_fix", marked.CatalogId);

        // An update keeps the mark, and the repo finds it even when the folder name changed.
        var updated = InstallUe4ssMod("TheFix", "1.1", "KintaroModding/the_fix", replace: true);
        Assert.True(updated.Recommended);
        manager.MarkRecommended([("KintaroModding/the_fix", "AnotherFolder")]);
        Assert.True(Assert.Single(manager.Mods, m => m.Name == "TheFix").Recommended);

        manager.MarkRecommended([]);
        Assert.DoesNotContain(manager.Mods, m => m.Recommended);
    }

    [Fact]
    public void A_set_turns_on_exactly_its_mods()
    {
        var sharp = Install("Sharp");
        Install("Swift");
        var spare = Install("Spare");
        manager.SetEnabled(spare.Id, false);
        manager.SaveSet("Co-op");
        manager.SetEnabled(sharp.Id, false);
        manager.SetEnabled(spare.Id, true);

        Assert.Equal((1, 1), manager.ApplySet("co-op"));

        Assert.Equal(["Sharp", "Swift"], Enabled());
        Assert.True(File.Exists(Deployed("Sharp")));
        Assert.False(File.Exists(Deployed("Spare")));
        Assert.Equal((0, 0), manager.ApplySet("Co-op"));
    }

    [Fact]
    public void Saving_with_the_same_name_replaces_the_set_in_its_place()
    {
        var sharp = Install("Sharp");
        Install("Swift");
        manager.SaveSet("Hard");
        manager.SaveSet("Easy");
        manager.SetEnabled(sharp.Id, false);

        manager.SaveSet(" hard ");

        Assert.Equal(["hard", "Easy"], manager.Sets.Select(s => s.Name));
        Assert.Single(manager.Sets[0].ModIds);
        Assert.Throws<ModException>(() => manager.SaveSet("  "));
    }

    [Fact]
    public void Sets_survive_a_restart_and_can_be_deleted()
    {
        Install("Sharp");
        manager.SaveSet("All");

        var restarted = new ModManager(game, new AppPaths(folder.Combine("Data")));

        Assert.Equal("All", Assert.Single(restarted.Sets).Name);
        restarted.DeleteSet("All");
        Assert.Empty(new ModManager(game, new AppPaths(folder.Combine("Data"))).Sets);
        Assert.Throws<ModException>(() => restarted.DeleteSet("All"));
    }

    [Fact]
    public void An_update_keeps_the_mod_in_its_sets_and_an_uninstall_takes_it_out()
    {
        Install("Sharp");
        var swift = Install("Swift");
        manager.SaveSet("Both");

        var updated = Install("Sharp", " 2.0", replace: true);
        manager.Uninstall(swift.Id);

        Assert.Equal([updated.Id], manager.Sets.Single().ModIds);
    }

    [Fact]
    public void A_set_ends_a_test_without_mods()
    {
        Install("Sharp");
        Install("Swift");
        manager.SaveSet("Both");
        manager.TurnOffForTest();

        Assert.Equal((2, 0), manager.ApplySet("Both"));

        Assert.Empty(manager.OffForTest);
        Assert.Equal(0, manager.TurnBackOn());
        Assert.Equal(["Sharp", "Swift"], Enabled());
    }

    [Fact]
    public void Saving_during_a_test_without_mods_keeps_the_mods_the_test_turned_off()
    {
        Install("Sharp");
        manager.TurnOffForTest();

        Assert.Single(manager.SaveSet("Mine").ModIds);
    }

    [Fact]
    public void A_set_changes_nothing_when_a_file_was_changed()
    {
        var sharp = Install("Sharp");
        var swift = Install("Swift");
        manager.SetEnabled(swift.Id, false);
        manager.SaveSet("Sharp only");
        manager.SetEnabled(sharp.Id, false);
        manager.SetEnabled(swift.Id, true);

        File.WriteAllText(Deployed("Swift"), "changed by hand");
        Assert.Throws<ChangedFilesException>(() => manager.ApplySet("Sharp only"));

        Assert.Equal(["Swift"], Enabled());
    }
}
