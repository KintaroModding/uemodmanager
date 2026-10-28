using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

public class ModManagerTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly ModManager manager;
    private readonly GameInstall game;

    public ModManagerTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")));
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

    [Fact]
    public void Install_deploys_files_and_records_them()
    {
        var mod = Install(Download("Sharp"));

        Assert.True(mod.Enabled);
        Assert.Equal(3, mod.Files.Count);
        Assert.True(File.Exists(Deployed("Sharp_P.pak")));
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(mod));

        var reloaded = new ModManager(game, new AppPaths(folder.Combine("Data")));
        Assert.Equal("Sharp", Assert.Single(reloaded.Mods).Name);
    }

    [Fact]
    public void Disable_and_enable_move_only_the_mods_files()
    {
        var mod = Install(Download("Sharp"));
        File.WriteAllText(Deployed("Someone_Else_P.pak"), "not ours");

        manager.SetEnabled(mod.Id, false);
        Assert.False(File.Exists(Deployed("Sharp_P.pak")));
        Assert.True(File.Exists(Deployed("Someone_Else_P.pak")));

        manager.SetEnabled(mod.Id, true);
        Assert.True(File.Exists(Deployed("Sharp_P.pak")));
        Assert.Equal(["Someone_Else_P.pak"], manager.UnmanagedFiles());
    }

    [Fact]
    public void A_changed_file_is_never_deleted()
    {
        var mod = Install(Download("Sharp"));
        File.WriteAllText(Deployed("Sharp_P.pak"), "edited by hand");

        Assert.Throws<ModException>(() => manager.Uninstall(mod.Id));

        Assert.Equal("edited by hand", File.ReadAllText(Deployed("Sharp_P.pak")));
        Assert.True(File.Exists(Deployed("Sharp_P.utoc")));
        Assert.Single(manager.Mods);
    }

    [Fact]
    public void Uninstall_removes_deployed_files_and_the_library_copy()
    {
        var mod = Install(Download("Sharp"));
        var library = manager.LibraryFolderOf(mod);

        manager.Uninstall(mod.Id);

        Assert.Empty(manager.Mods);
        Assert.False(File.Exists(Deployed("Sharp_P.pak")));
        Assert.False(Directory.Exists(library));
    }

    [Fact]
    public void An_unmanaged_file_with_the_same_name_blocks_the_install()
    {
        Directory.CreateDirectory(game.ModsPath);
        File.WriteAllText(Deployed("Sharp_P.pak"), "installed by hand");
        using var preview = manager.Inspect(Download("Sharp"));

        var check = manager.Check(preview, preview.Packages.ToList());

        Assert.False(check.CanInstall);
        Assert.Throws<ModException>(() => manager.Install(preview, preview.Packages.ToList(), "Sharp", null));
        Assert.Equal("installed by hand", File.ReadAllText(Deployed("Sharp_P.pak")));
    }

    [Fact]
    public void A_newer_download_replaces_the_installed_mod()
    {
        var first = Install(Download("Sharp", "v1"));
        using (var preview = manager.Inspect(Download("Sharp", "v2")))
        {
            var check = manager.Check(preview, preview.Packages.ToList());
            Assert.True(check.CanInstall);
            Assert.Equal(first.Id, check.Replaces?.Id);
            Assert.Throws<ModException>(() => manager.Install(preview, preview.Packages.ToList(), "Sharp", null));
        }

        var second = Install(Download("Sharp", "v2"), replace: true);

        Assert.Equal(second.Id, Assert.Single(manager.Mods).Id);
        Assert.Equal("v2", File.ReadAllText(Deployed("Sharp_P.pak")));
        Assert.False(Directory.Exists(manager.LibraryFolderOf(first)));
    }

    [Fact]
    public void A_missing_file_is_reported_and_fixed_by_toggling()
    {
        var mod = Install(Download("Sharp"));
        File.Delete(Deployed("Sharp_P.ucas"));

        Assert.Equal(ModIntegrity.FilesMissing, ModManager.Integrity(manager.Mods[0]));

        manager.SetEnabled(mod.Id, false);
        manager.SetEnabled(mod.Id, true);
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(manager.Mods[0]));
    }
}
