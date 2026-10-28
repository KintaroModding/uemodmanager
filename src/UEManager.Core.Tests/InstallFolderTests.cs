using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>The mod list follows the player from one install of the game to another, and never acts on the wrong one.</summary>
public class InstallFolderTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly AppPaths paths;
    private readonly GameInstall first;

    public InstallFolderTests()
    {
        paths = new AppPaths(folder.Combine("Data"));
        first = Resolve(folder.Game());
    }

    public void Dispose() => folder.Dispose();

    private static GameInstall Resolve(string root) => GameLocator.Resolve(TestFolder.Profile(), root, GameStore.Manual)!;

    private ModRecord Install(ModManager manager, string name)
    {
        folder.File($@"Downloads\{name}\{name}_P.pak", name);
        using var preview = manager.Inspect(folder.Combine("Downloads", name));
        return manager.Install(preview, preview.Packages.ToList(), name, null);
    }

    private static string Deployed(GameInstall game, string fileName) => Path.Combine(game.ModsPath, fileName);

    [Fact]
    public void Mods_in_another_install_block_changes_until_they_are_moved()
    {
        var mod = Install(new ModManager(first, paths), "Sharp");
        var second = Resolve(folder.Game(name: "Copy"));

        var manager = new ModManager(second, paths);
        Assert.Equal(first.RootPath, manager.OtherInstallRoot);
        Assert.Equal(mod.Id, Assert.Single(manager.ModsInOtherInstall).Id);
        var error = Assert.Throws<ModException>(() => manager.SetEnabled(mod.Id, false));
        Assert.Contains("another folder", error.Message);
        Assert.Throws<ModException>(() => manager.Uninstall(mod.Id));
        Assert.True(File.Exists(Deployed(first, "Sharp_P.pak")));

        Assert.Empty(manager.MoveHere());

        Assert.Null(manager.OtherInstallRoot);
        Assert.False(File.Exists(Deployed(first, "Sharp_P.pak")));
        Assert.True(File.Exists(Deployed(second, "Sharp_P.pak")));
        var moved = Assert.Single(manager.Mods);
        Assert.True(moved.Enabled);
        Assert.Equal(Deployed(second, "Sharp_P.pak"), moved.Files[0].DeployedPath);
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(moved));
        Assert.Null(new ModManager(second, paths).OtherInstallRoot);

        manager.SetEnabled(mod.Id, false);
        Assert.False(File.Exists(Deployed(second, "Sharp_P.pak")));
    }

    [Fact]
    public void Nothing_moves_while_the_game_runs_from_the_other_install()
    {
        Install(new ModManager(first, paths), "Sharp");
        var second = Resolve(folder.Game(name: "Copy"));
        var manager = new ModManager(second, paths, install => install.RootPath == first.RootPath);

        var error = Assert.Throws<ModException>(() => manager.MoveHere());

        Assert.Contains("Close the game", error.Message);
        Assert.True(File.Exists(Deployed(first, "Sharp_P.pak")));
        Assert.False(File.Exists(Deployed(second, "Sharp_P.pak")));
        Assert.Equal(first.RootPath, manager.OtherInstallRoot);
    }

    [Fact]
    public void A_file_in_use_in_the_other_install_moves_nothing()
    {
        var setup = new ModManager(first, paths);
        Install(setup, "Sharp");
        Install(setup, "Dull");
        var second = Resolve(folder.Game(name: "Copy"));
        var manager = new ModManager(second, paths);

        using (File.Open(Deployed(first, "Dull_P.pak"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<ModException>(() => manager.MoveHere());

        Assert.True(File.Exists(Deployed(first, "Sharp_P.pak")));
        Assert.True(File.Exists(Deployed(first, "Dull_P.pak")));
        Assert.False(Directory.Exists(second.ModsPath) && Directory.EnumerateFiles(second.ModsPath).Any());
        Assert.All(manager.Mods, mod => Assert.True(mod.Enabled));
        Assert.Equal(2, manager.ModsInOtherInstall.Count);
    }

    [Fact]
    public void A_copy_of_the_game_folder_keeps_its_mods()
    {
        Install(new ModManager(first, paths), "Sharp");
        var copy = folder.Combine("Copy");
        foreach (var file in Directory.EnumerateFiles(first.RootPath, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(copy, Path.GetRelativePath(first.RootPath, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        var second = Resolve(copy);
        var manager = new ModManager(second, paths);

        Assert.Empty(manager.MoveHere());

        Assert.False(File.Exists(Deployed(first, "Sharp_P.pak")));
        Assert.True(File.Exists(Deployed(second, "Sharp_P.pak")));
        Assert.Empty(manager.UnmanagedFiles());
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(manager.Mods[0]));
    }

    [Fact]
    public void A_file_of_another_mod_in_the_way_leaves_that_mod_disabled()
    {
        Install(new ModManager(first, paths), "Sharp");
        var second = Resolve(folder.Game(name: "Copy"));
        Directory.CreateDirectory(second.ModsPath);
        File.WriteAllText(Deployed(second, "Sharp_P.pak"), "someone else's");
        var manager = new ModManager(second, paths);

        var stayed = Assert.Single(manager.MoveHere());

        Assert.Equal("Sharp", stayed.Name);
        Assert.False(manager.Mods[0].Enabled);
        Assert.False(File.Exists(Deployed(first, "Sharp_P.pak")));
        Assert.Equal("someone else's", File.ReadAllText(Deployed(second, "Sharp_P.pak")));
        Assert.Null(manager.OtherInstallRoot);
    }

    [Fact]
    public void A_moved_game_folder_is_followed()
    {
        Install(new ModManager(first, paths), "Sharp");
        var moved = folder.Combine("Moved");
        Directory.Move(first.RootPath, moved);
        var second = Resolve(moved);

        var manager = new ModManager(second, paths);

        Assert.Null(manager.OtherInstallRoot);
        Assert.Equal(Deployed(second, "Sharp_P.pak"), manager.Mods[0].Files[0].DeployedPath);
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(manager.Mods[0]));
        Assert.Equal(Deployed(second, "Sharp_P.pak"), new ModManager(second, paths).Mods[0].Files[0].DeployedPath);
    }

    [Fact]
    public void A_moved_game_folder_never_takes_a_foreign_file_of_the_same_name_and_size()
    {
        Install(new ModManager(first, paths), "Sharp");
        var moved = folder.Combine("Moved");
        Directory.Move(first.RootPath, moved);
        var second = Resolve(moved);
        File.WriteAllText(Deployed(second, "Sharp_P.pak"), "SHARP");

        var manager = new ModManager(second, paths);

        Assert.Null(manager.Mods[0].Files[0].DeployedPath);
        Assert.Equal(ModIntegrity.FilesMissing, ModManager.Integrity(manager.Mods[0]));
        Assert.Equal(["Sharp_P.pak"], manager.UnmanagedFiles());
    }

    [Fact]
    public void A_move_that_cannot_be_saved_is_undone()
    {
        var mod = Install(new ModManager(first, paths), "Sharp");
        var second = Resolve(folder.Game(name: "Copy"));
        Directory.CreateDirectory(second.ModsPath);
        File.WriteAllText(Deployed(second, "Sharp_P.pak"), "SHARP");
        var manager = new ModManager(second, paths);

        using (File.Open(paths.StateFile(first.Profile.Id), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<ModException>(() => manager.MoveHere());

        Assert.Equal("Sharp", File.ReadAllText(Deployed(first, "Sharp_P.pak")));
        Assert.Equal("SHARP", File.ReadAllText(Deployed(second, "Sharp_P.pak")));
        Assert.Equal(first.RootPath, manager.OtherInstallRoot);
        Assert.True(manager.Mods[0].Enabled);
        Assert.Equal(Deployed(first, "Sharp_P.pak"), manager.Mods[0].Files[0].DeployedPath);

        // Once the list can be saved, the mod is moved, or stays disabled when a foreign file is in the way.
        var restarted = new ModManager(second, paths);
        Assert.Equal(mod.Id, Assert.Single(restarted.MoveHere()).Id);
        Assert.False(new ModManager(second, paths).Mods[0].Enabled);
        Assert.Equal("SHARP", File.ReadAllText(Deployed(second, "Sharp_P.pak")));
    }
}
