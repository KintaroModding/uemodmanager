using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>
/// A download that also places a file in one of the game's own folders, as the DualSense layout mod for Phantom
/// Blade Zero does with ProjectPBZ/Saved/EnhancedInputUserSettings.sav: the pak files alone do not bring the
/// keybinding presets, so the download is not installable without that file.
/// </summary>
public class InstallRootTests : IDisposable
{
    private const string SavedFile = @"TestGame\Saved\EnhancedInputUserSettings.sav";

    private readonly TestFolder folder = new();
    private readonly ModManager manager;
    private readonly GameInstall game;

    public InstallRootTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")));
    }

    public void Dispose() => folder.Dispose();

    /// <summary>The game's own file, which the mod replaces.</summary>
    private string InstalledFile => Path.Combine(folder.Combine("Game"), SavedFile);

    /// <summary>The download as the mod ships it: a pak trio in ~mods, and the keybinding file below the project folder.</summary>
    private string Download(string name = "Keybinds")
    {
        folder.File($@"Downloads\{name}\TestGame\Content\Paks\~mods\{name}_P.pak", "pak");
        folder.File($@"Downloads\{name}\TestGame\Content\Paks\~mods\{name}_P.utoc", "utoc");
        folder.File($@"Downloads\{name}\TestGame\Content\Paks\~mods\{name}_P.ucas", "ucas");
        folder.File($@"Downloads\{name}\{SavedFile}", "the mod's keybindings");
        return folder.Combine("Downloads", name);
    }

    private ModRecord Install(string source)
    {
        using var preview = manager.Inspect(source);
        return manager.Install(preview, preview.Packages.ToList(), preview.SuggestedName, preview.SuggestedVersion);
    }

    [Fact]
    public void The_keybinding_file_is_a_part_of_the_download_and_keeps_its_path()
    {
        using var preview = manager.Inspect(Download());

        var file = Assert.Single(preview.Packages, p => p.Target == DeployTarget.InstallRoot);
        Assert.Equal(PackageKind.File, file.Kind);
        Assert.Equal(["TestGame/Saved/EnhancedInputUserSettings.sav"], file.RelativePaths);
        Assert.Equal(2, preview.Packages.Count);
        Assert.Equal(0, preview.IgnoredFileCount);
    }

    [Fact]
    public void Installing_replaces_the_games_file_and_keeps_a_copy_of_the_old_one()
    {
        folder.File($@"Game\{SavedFile}", "the player's own keybindings");

        var mod = Install(Download());

        Assert.Equal("the mod's keybindings", File.ReadAllText(InstalledFile));
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(mod));
        Assert.True(File.Exists(Path.Combine(manager.LibraryFolderOf(mod), ".replaced", "InstallRoot", SavedFile)));
    }

    [Fact]
    public void Disabling_puts_the_games_file_back_and_enabling_takes_it_again()
    {
        folder.File($@"Game\{SavedFile}", "the player's own keybindings");
        var mod = Install(Download());

        manager.SetEnabled(mod.Id, false);
        Assert.Equal("the player's own keybindings", File.ReadAllText(InstalledFile));

        manager.SetEnabled(mod.Id, true);
        Assert.Equal("the mod's keybindings", File.ReadAllText(InstalledFile));
    }

    [Fact]
    public void Uninstalling_puts_the_games_file_back()
    {
        folder.File($@"Game\{SavedFile}", "the player's own keybindings");
        var mod = Install(Download());

        manager.Uninstall(mod.Id);

        Assert.Equal("the player's own keybindings", File.ReadAllText(InstalledFile));
        Assert.Empty(manager.Mods);
    }

    [Fact]
    public void A_file_the_game_rewrote_is_removed_without_a_complaint_and_puts_the_old_one_back()
    {
        folder.File($@"Game\{SavedFile}", "the player's own keybindings");
        var mod = Install(Download());
        // The game writes its settings file while the player is in the menus, so its content says nothing.
        File.WriteAllText(InstalledFile, "what the game wrote while the player played");

        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(mod));

        manager.Uninstall(mod.Id);
        Assert.Equal("the player's own keybindings", File.ReadAllText(InstalledFile));
    }

    [Fact]
    public void Without_a_file_of_the_games_own_the_mod_file_is_simply_removed()
    {
        var mod = Install(Download());
        Assert.Equal("the mod's keybindings", File.ReadAllText(InstalledFile));

        manager.Uninstall(mod.Id);
        Assert.False(File.Exists(InstalledFile));
    }

    [Fact]
    public void A_readme_beside_the_file_is_not_installed()
    {
        var source = Download();
        folder.File(@"Downloads\Keybinds\TestGame\Saved\readme.txt", "install instructions by hand");

        using var preview = manager.Inspect(source);

        Assert.Single(preview.Packages, p => p.Target == DeployTarget.InstallRoot);
        Assert.Equal(1, preview.IgnoredFileCount);
    }

    [Fact]
    public void A_download_that_is_only_that_file_can_be_installed()
    {
        var only = folder.Combine("Downloads", "OnlyKeys");
        folder.File(@"Downloads\OnlyKeys\TestGame\Saved\EnhancedInputUserSettings.sav", "the mod's keybindings");

        var mod = Install(only);

        Assert.Single(mod.Files);
        Assert.Equal(DeployTarget.InstallRoot, mod.Files[0].Target);
        Assert.Equal(ModTypes.GameFiles, mod.TypeLabel);
        Assert.Equal("the mod's keybindings", File.ReadAllText(InstalledFile));
    }
}
