using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>Turning every mod off to see whether the game runs without them, and exactly those back on.</summary>
public class TestWithoutModsTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly GameInstall game;
    private readonly ModManager manager;
    private bool gameRunning;

    public TestWithoutModsTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")), _ => gameRunning);
    }

    public void Dispose() => folder.Dispose();

    private ModRecord Install(string name)
    {
        folder.File($@"Downloads\{name}\{name}_P.pak", name);
        using var preview = manager.Inspect(folder.Combine("Downloads", name));
        return manager.Install(preview, preview.Packages.ToList(), name, null);
    }

    private string Deployed(string name) => Path.Combine(game.ModsPath, name + "_P.pak");

    [Fact]
    public void Turns_every_mod_off_and_exactly_those_back_on()
    {
        var sharp = Install("Sharp");
        var swift = Install("Swift");
        var spare = Install("Spare");
        manager.SetEnabled(spare.Id, false);
        File.WriteAllText(Path.Combine(game.ModsPath, "ByHand_P.pak"), "not ours");

        Assert.Equal(2, manager.TurnOffForTest());

        Assert.False(File.Exists(Deployed("Sharp")));
        Assert.False(File.Exists(Deployed("Swift")));
        Assert.True(File.Exists(Path.Combine(game.ModsPath, "ByHand_P.pak")));
        Assert.Equal(["Sharp", "Swift"], manager.OffForTest.Select(m => m.Name).Order());

        Assert.Equal(2, manager.TurnBackOn());

        Assert.True(File.Exists(Deployed("Sharp")));
        Assert.True(File.Exists(Deployed("Swift")));
        Assert.False(File.Exists(Deployed("Spare")));
        Assert.Empty(manager.OffForTest);
        Assert.Equal(0, manager.TurnBackOn());
        Assert.True(manager.Mods.Single(m => m.Id == sharp.Id).Enabled && manager.Mods.Single(m => m.Id == swift.Id).Enabled);
    }

    [Fact]
    public void The_test_survives_a_restart_of_the_app()
    {
        Install("Sharp");
        manager.TurnOffForTest();

        var restarted = new ModManager(game, new AppPaths(folder.Combine("Data")), _ => false);

        Assert.Equal("Sharp", Assert.Single(restarted.OffForTest).Name);
        Assert.Equal(1, restarted.TurnBackOn());
        Assert.True(File.Exists(Deployed("Sharp")));
    }

    [Fact]
    public void A_changed_file_stops_the_test_and_nothing_changes()
    {
        Install("Sharp");
        Install("Swift");
        File.WriteAllText(Deployed("Swift"), "changed by hand");

        Assert.Throws<ModException>(() => manager.TurnOffForTest());

        Assert.True(File.Exists(Deployed("Sharp")));
        Assert.All(manager.Mods, m => Assert.True(m.Enabled));
        Assert.Empty(manager.OffForTest);
    }

    [Fact]
    public void A_mod_turned_on_during_the_test_stays_on_and_an_uninstalled_one_is_skipped()
    {
        var sharp = Install("Sharp");
        var swift = Install("Swift");
        manager.TurnOffForTest();
        manager.SetEnabled(sharp.Id, true);
        manager.Uninstall(swift.Id);

        Assert.Empty(manager.OffForTest);
        Assert.Equal(0, manager.TurnBackOn());
        Assert.True(File.Exists(Deployed("Sharp")));
    }

    [Fact]
    public void Nothing_changes_while_the_game_runs()
    {
        Install("Sharp");
        gameRunning = true;

        Assert.Throws<ModException>(() => manager.TurnOffForTest());
        Assert.True(File.Exists(Deployed("Sharp")));
    }
}
