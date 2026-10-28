using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>When the mod list cannot be saved, the game and the list stay as they were, also after a restart.</summary>
public class SaveFailureTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly GameInstall game;
    private readonly AppPaths paths;
    private readonly ModManager manager;

    public SaveFailureTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        paths = new AppPaths(folder.Combine("Data"));
        manager = new ModManager(game, paths, _ => false);
    }

    public void Dispose() => folder.Dispose();

    private string Download(string name, string content = "v1")
    {
        folder.File($@"Downloads\{name}\{name}_P.pak", content);
        folder.File($@"Downloads\{name}\{name}_P.utoc", content);
        folder.File($@"Downloads\{name}\{name}_P.ucas", content);
        return folder.Combine("Downloads", name);
    }

    private ModRecord Install(string source, bool replace = false)
    {
        using var preview = manager.Inspect(source);
        return manager.Install(preview, preview.Packages.ToList(), preview.SuggestedName, preview.SuggestedVersion, replace);
    }

    private string Deployed(string fileName) => Path.Combine(game.ModsPath, fileName);

    /// <summary>Holds the mod list the way a virus scanner may: others may read it, but not replace it.</summary>
    private FileStream HoldList() => File.Open(paths.StateFile(game.Profile.Id), FileMode.Open, FileAccess.Read, FileShare.Read);

    private ModRecord AfterRestart(string id) => new ModManager(game, paths, _ => false).Mods.Single(m => m.Id == id);

    [Fact]
    public void A_failed_enable_is_undone()
    {
        var mod = Install(Download("Sharp"));
        manager.SetEnabled(mod.Id, false);

        using (HoldList())
        {
            var error = Assert.Throws<ModException>(() => manager.SetEnabled(mod.Id, true));
            Assert.Contains("could not save", error.Message);
        }

        Assert.False(File.Exists(Deployed("Sharp_P.pak")));
        Assert.False(manager.Mods[0].Enabled);
        Assert.False(AfterRestart(mod.Id).Enabled);
        manager.SetEnabled(mod.Id, true);
        Assert.True(File.Exists(Deployed("Sharp_P.pak")));
    }

    [Fact]
    public void A_failed_disable_is_undone()
    {
        var mod = Install(Download("Sharp"));

        using (HoldList())
            Assert.Throws<ModException>(() => manager.SetEnabled(mod.Id, false));

        Assert.All(new[] { "Sharp_P.pak", "Sharp_P.utoc", "Sharp_P.ucas" }, name => Assert.True(File.Exists(Deployed(name))));
        Assert.Empty(Directory.GetFiles(game.ModsPath, "*.uemm-removing"));
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(manager.Mods[0]));
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(AfterRestart(mod.Id)));
        manager.SetEnabled(mod.Id, false);
        Assert.False(File.Exists(Deployed("Sharp_P.pak")));
    }

    [Fact]
    public void A_failed_uninstall_is_undone()
    {
        var mod = Install(Download("Sharp"));

        using (HoldList())
            Assert.Throws<ModException>(() => manager.Uninstall(mod.Id));

        Assert.True(File.Exists(Deployed("Sharp_P.pak")));
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(Assert.Single(manager.Mods)));
        Assert.True(Directory.Exists(manager.LibraryFolderOf(mod)));
        manager.Uninstall(mod.Id);
        Assert.Empty(manager.Mods);
    }

    [Fact]
    public void A_failed_update_keeps_the_old_version()
    {
        var old = Install(Download("Sharp", "v1"));

        using (HoldList())
            Assert.Throws<ModException>(() => Install(Download("Sharp", "v2"), replace: true));

        Assert.Equal("v1", File.ReadAllText(Deployed("Sharp_P.pak")));
        Assert.Equal(old.Id, Assert.Single(manager.Mods).Id);
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(manager.Mods[0]));
        Assert.Single(Directory.GetDirectories(paths.LibraryDir(game.Profile.Id)));
    }

    [Fact]
    public void A_short_hold_is_waited_out()
    {
        var mod = Install(Download("Sharp"));
        var hold = HoldList();
        _ = Task.Delay(200).ContinueWith(_ => hold.Dispose());

        manager.SetEnabled(mod.Id, false);

        Assert.False(File.Exists(Deployed("Sharp_P.pak")));
        Assert.False(AfterRestart(mod.Id).Enabled);
    }
}
