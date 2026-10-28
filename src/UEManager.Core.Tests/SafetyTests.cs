using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>Nothing changes while the game runs, and a file in use leaves a mod as it was.</summary>
public class SafetyTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly GameInstall game;
    private readonly ModManager manager;
    private bool gameRunning;

    public SafetyTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")), _ => gameRunning);
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

    /// <summary>Opens a file the way the running game holds its paks: others may read it, but not move or delete it.</summary>
    private static FileStream Hold(string path) => File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    [Fact]
    public void Nothing_changes_while_the_game_runs()
    {
        var mod = Install(Download("Sharp"));
        gameRunning = true;

        var error = Assert.Throws<ModException>(() => manager.SetEnabled(mod.Id, false));
        Assert.Contains("Close the game", error.Message);
        Assert.Throws<ModException>(() => manager.Uninstall(mod.Id));
        Assert.Throws<ModException>(() => Install(Download("Other")));
        Assert.Throws<ModException>(() => manager.InstallUe4ss(folder.Combine("Nowhere")));
        Assert.Throws<ModException>(() => manager.RemoveUe4ss());

        Assert.True(File.Exists(Deployed("Sharp_P.pak")));
        Assert.False(File.Exists(Deployed("Other_P.pak")));
        Assert.True(Assert.Single(manager.Mods).Enabled);
    }

    [Fact]
    public void A_file_in_use_leaves_the_mod_in_place()
    {
        var mod = Install(Download("Sharp"));

        using (Hold(Deployed("Sharp_P.ucas")))
        {
            var error = Assert.Throws<ModException>(() => manager.SetEnabled(mod.Id, false));
            Assert.Contains("in use", error.Message);
        }

        Assert.All(new[] { "Sharp_P.pak", "Sharp_P.utoc", "Sharp_P.ucas" }, name => Assert.True(File.Exists(Deployed(name))));
        Assert.Empty(Directory.GetFiles(game.ModsPath, "*.uemm-removing"));
        Assert.True(manager.Mods[0].Enabled);
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(manager.Mods[0]));

        manager.SetEnabled(mod.Id, false);
        Assert.False(File.Exists(Deployed("Sharp_P.pak")));
    }

    [Fact]
    public void A_file_in_use_stops_an_uninstall()
    {
        var mod = Install(Download("Sharp"));

        using (Hold(Deployed("Sharp_P.utoc")))
            Assert.Throws<ModException>(() => manager.Uninstall(mod.Id));

        Assert.True(File.Exists(Deployed("Sharp_P.pak")));
        Assert.Single(manager.Mods);
        Assert.True(Directory.Exists(manager.LibraryFolderOf(mod)));
    }

    [Fact]
    public void A_file_in_use_stops_an_update_and_keeps_the_old_version()
    {
        Install(Download("Sharp", "v1"));

        using (Hold(Deployed("Sharp_P.pak")))
            Assert.Throws<ModException>(() => Install(Download("Sharp", "v2"), replace: true));

        Assert.Equal("v1", File.ReadAllText(Deployed("Sharp_P.pak")));
        Assert.Equal("v1", File.ReadAllText(Deployed("Sharp_P.ucas")));
        Assert.True(Assert.Single(manager.Mods).Enabled);
    }
}
