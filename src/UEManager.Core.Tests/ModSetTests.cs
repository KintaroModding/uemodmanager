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
    private bool gameRunning;

    public ModSetTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")), _ => gameRunning);
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

        var restarted = new ModManager(game, new AppPaths(folder.Combine("Data")), _ => false);

        Assert.Equal("All", Assert.Single(restarted.Sets).Name);
        restarted.DeleteSet("All");
        Assert.Empty(new ModManager(game, new AppPaths(folder.Combine("Data")), _ => false).Sets);
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
    public void A_set_changes_nothing_while_the_game_runs_or_a_file_was_changed()
    {
        var sharp = Install("Sharp");
        var swift = Install("Swift");
        manager.SetEnabled(swift.Id, false);
        manager.SaveSet("Sharp only");
        manager.SetEnabled(sharp.Id, false);
        manager.SetEnabled(swift.Id, true);

        gameRunning = true;
        Assert.Throws<ModException>(() => manager.ApplySet("Sharp only"));
        gameRunning = false;
        File.WriteAllText(Deployed("Swift"), "changed by hand");
        Assert.Throws<ModException>(() => manager.ApplySet("Sharp only"));

        Assert.Equal(["Swift"], Enabled());
    }
}
