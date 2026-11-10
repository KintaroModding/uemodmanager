using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>Renaming an installed mod changes only its name and version in the mod list.</summary>
public class RenameTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly GameInstall game;
    private readonly ModManager manager;

    public RenameTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")));
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
    public void A_new_name_and_version_survive_a_restart_and_the_game_files_stay()
    {
        var mod = Install("Sharp");
        manager.SaveSet("Mine");

        manager.Rename(mod.Id, "  Sharp Blades ", " 1.2 ");

        var restarted = new ModManager(game, new AppPaths(folder.Combine("Data")));
        var renamed = Assert.Single(restarted.Mods);
        Assert.Equal(("Sharp Blades", "1.2"), (renamed.Name, renamed.Version));
        Assert.True(renamed.Enabled);
        Assert.True(File.Exists(Deployed("Sharp")));
        Assert.Equal([mod.Id], Assert.Single(restarted.Sets).ModIds);
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(renamed));
    }

    [Fact]
    public void A_blank_version_means_none_and_a_blank_name_is_refused()
    {
        var mod = Install("Sharp");
        manager.Rename(mod.Id, "Sharp", "1.0");

        manager.Rename(mod.Id, "Sharp", "  ");
        Assert.Null(manager.Mods.Single().Version);

        Assert.Throws<ModException>(() => manager.Rename(mod.Id, "   ", "2.0"));
        Assert.Equal(("Sharp", (string?)null), (manager.Mods.Single().Name, manager.Mods.Single().Version));
    }

    [Fact]
    public void A_rename_changes_only_mod_metadata()
    {
        var mod = Install("Sharp");

        manager.Rename(mod.Id, "Sharp Blades", null);

        Assert.Equal("Sharp Blades", manager.Mods.Single().Name);
    }
}
