using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>A pak mod that reads its settings from a file next to its pak, such as a .json in ~mods.</summary>
public class PakSettingsTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly GameInstall game;
    private readonly ModManager manager;

    public PakSettingsTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")));
    }

    public void Dispose() => folder.Dispose();

    private string Settings => Path.Combine(game.ModsPath, "Highlight.json");

    /// <summary>The layout of the Speaker Highlight download: the game's folders, a readme at the top.</summary>
    private string Download(string version, string settings = """{ "color": "#FFE9A8" }""")
    {
        var root = $@"Downloads\Highlight_{version}";
        folder.File($@"{root}\README.txt", "how to install");
        folder.File($@"{root}\TestGame\Content\Paks\~mods\zzz_Highlight_P.pak", "pak " + version);
        folder.File($@"{root}\TestGame\Content\Paks\~mods\zzz_Highlight_P.utoc", "utoc " + version);
        folder.File($@"{root}\TestGame\Content\Paks\~mods\zzz_Highlight_P.ucas", "ucas " + version);
        folder.File($@"{root}\TestGame\Content\Paks\~mods\Highlight.json", settings);
        return folder.Combine(root);
    }

    private ModRecord Install(string source, bool replace = false)
    {
        using var preview = manager.Inspect(source);
        return manager.Install(preview, preview.Packages.ToList(), "Highlight", null, replace);
    }

    [Fact]
    public void A_settings_file_next_to_the_pak_in_mods_is_installed_with_it()
    {
        using (var preview = manager.Inspect(Download("1")))
        {
            var package = Assert.Single(preview.Packages);
            Assert.Equal(["zzz_Highlight_P.pak", "zzz_Highlight_P.utoc", "zzz_Highlight_P.ucas", "Highlight.json"], package.RelativePaths);
            Assert.Equal("IoStore", package.TypeLabel);
            Assert.Equal(1, preview.IgnoredFileCount);
        }

        var mod = Install(Download("1"));

        Assert.Equal("""{ "color": "#FFE9A8" }""", File.ReadAllText(Settings));
        Assert.True(mod.Files.Single(f => f.FileName == "Highlight.json").IsSettingsFile);
        Assert.False(File.Exists(Path.Combine(game.ModsPath, "README.txt")));
    }

    [Fact]
    public void A_settings_file_outside_a_mods_folder_is_not_installed()
    {
        folder.File(@"Downloads\Flat\Flat_P.pak", "pak");
        folder.File(@"Downloads\Flat\info.json", "{}");

        using var preview = manager.Inspect(folder.Combine("Downloads", "Flat"));

        Assert.Equal(["Flat_P.pak"], Assert.Single(preview.Packages).RelativePaths);
        Assert.Equal(1, preview.IgnoredFileCount);
    }

    [Fact]
    public void With_several_packages_the_settings_file_goes_with_the_one_it_names()
    {
        folder.File(@"Downloads\Two\~mods\Another_P.pak", "pak");
        folder.File(@"Downloads\Two\~mods\zzz_Highlight_P.pak", "pak");
        folder.File(@"Downloads\Two\~mods\Highlight.json", "{}");

        using var preview = manager.Inspect(folder.Combine("Downloads", "Two"));

        Assert.Equal(["Another_P.pak"], preview.Packages.Single(p => p.Name == "Another_P").RelativePaths);
        Assert.Equal(["zzz_Highlight_P.pak", "Highlight.json"], preview.Packages.Single(p => p.Name == "zzz_Highlight_P").RelativePaths);
    }

    [Fact]
    public void The_players_settings_survive_disable_and_update()
    {
        var mod = Install(Download("1"));
        File.WriteAllText(Settings, """{ "color": "#8FD3FF" }""");

        manager.SetEnabled(mod.Id, false);
        Assert.False(File.Exists(Settings));
        manager.SetEnabled(mod.Id, true);
        Assert.Equal("""{ "color": "#8FD3FF" }""", File.ReadAllText(Settings));

        Install(Download("2"), replace: true);

        Assert.Equal("""{ "color": "#8FD3FF" }""", File.ReadAllText(Settings));
        Assert.Equal("pak 2", File.ReadAllText(Path.Combine(game.ModsPath, "zzz_Highlight_P.pak")));
    }
}
