using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Stores;

namespace UEManager.Core.Tests;

public class GameLocatorTests
{
    [Fact]
    public void Resolves_the_profile_project_folder()
    {
        using var folder = new TestFolder();
        var root = folder.Game();

        var install = GameLocator.Resolve(TestFolder.Profile(), root, GameStore.Manual);

        Assert.NotNull(install);
        Assert.Equal("TestGame", install.ProjectFolder);
        Assert.Equal(Path.Combine(root, "TestGame", "Content", "Paks", "~mods"), install.ModsPath);
    }

    [Fact]
    public void Detects_the_project_folder_when_the_profile_guess_is_wrong()
    {
        using var folder = new TestFolder();
        var root = folder.Game("RealName");
        Directory.CreateDirectory(Path.Combine(root, "Engine", "Content", "Paks"));

        var install = GameLocator.Resolve(TestFolder.Profile("WrongGuess"), root, GameStore.Manual);

        Assert.NotNull(install);
        Assert.Equal("RealName", install.ProjectFolder);
    }

    [Fact]
    public void A_detected_project_folder_needs_a_game_executable()
    {
        using var folder = new TestFolder();
        var root = folder.Game("RealName");
        Directory.CreateDirectory(Path.Combine(root, "Aaa", "Content", "Paks"));
        Directory.CreateDirectory(Path.Combine(root, "Aaa", "Binaries", "Win64"));

        var install = GameLocator.Resolve(TestFolder.Profile("WrongGuess"), root, GameStore.Manual);

        Assert.Equal("RealName", install?.ProjectFolder);
    }

    [Fact]
    public void Rejects_a_folder_without_paks()
    {
        using var folder = new TestFolder();
        Directory.CreateDirectory(folder.Combine("NotAGame", "Stuff"));

        Assert.Null(GameLocator.Resolve(TestFolder.Profile(), folder.Combine("NotAGame"), GameStore.Manual));
    }

    [Fact]
    public void Walks_up_from_a_folder_inside_the_install()
    {
        using var folder = new TestFolder();
        var root = folder.Game();

        var install = GameLocator.FromFolder(TestFolder.Profile(), Path.Combine(root, "TestGame", "Binaries", "Win64"));

        Assert.NotNull(install);
        Assert.Equal(Path.GetFullPath(root), install.RootPath);
    }

    [Fact]
    public void Profile_paths_accept_both_slash_styles()
    {
        var path = GameProfile.ResolvePath(@"C:\Games\X", @"{Project}\Content/Paks\~mods", "Proj");

        Assert.Equal(@"C:\Games\X\Proj\Content\Paks\~mods", path);
    }

    [Fact]
    public void Finds_the_launcher_executable()
    {
        using var folder = new TestFolder();
        var root = folder.Game();
        File.WriteAllText(Path.Combine(root, "UnrealCEFSubProcess.exe"), "");

        var install = GameLocator.Resolve(TestFolder.Profile(), root, GameStore.Manual)!;

        Assert.Equal(Path.Combine(root, "TestGame.exe"), install.FindExecutable());
    }

    [Fact]
    public void Finds_a_steam_app_in_an_additional_library()
    {
        using var folder = new TestFolder();
        var steam = folder.Combine("Steam");
        var library = folder.Combine("Library");
        folder.File(@"Steam\steamapps\libraryfolders.vdf",
            $"\"libraryfolders\" {{ \"1\" {{ \"path\" \"{library.Replace(@"\", @"\\")}\" }} }}");
        folder.File(@"Library\steamapps\appmanifest_999999.acf",
            "\"AppState\" { \"appid\" \"999999\" \"installdir\" \"Test Game\" \"buildid\" \"123456\" }");
        Directory.CreateDirectory(Path.Combine(library, "steamapps", "common", "Test Game"));

        var app = SteamLibrary.FindApp("999999", [steam]);

        Assert.NotNull(app);
        Assert.Equal("123456", app.BuildId);
        Assert.Equal(Path.Combine(library, "steamapps", "common", "Test Game"), app.InstallPath);
    }

    [Fact]
    public void Reads_epic_manifests()
    {
        using var folder = new TestFolder();
        var install = folder.Combine("EpicGame");
        Directory.CreateDirectory(install);
        folder.File(@"Manifests\abc.item", $$"""
            {
              "DisplayName": "Test Game",
              "InstallLocation": "{{install.Replace(@"\", @"\\")}}",
              "AppName": "App",
              "CatalogNamespace": "ns",
              "CatalogItemId": "item",
              "AppVersionString": "1.0"
            }
            """);

        var app = EpicLauncher.FindApp("test game", folder.Combine("Manifests"));

        Assert.NotNull(app);
        Assert.Equal(install, app.InstallPath);
        Assert.Equal("com.epicgames.launcher://apps/ns%3Aitem%3AApp?action=launch&silent=true", app.LaunchUri);
    }

    [Fact]
    public void A_folder_chosen_by_hand_in_an_epic_install_starts_through_epic()
    {
        using var folder = new TestFolder();
        var root = folder.Game();
        folder.File(@"Manifests\abc.item", $$"""
            {
              "DisplayName": "Test Game",
              "InstallLocation": "{{root.Replace(@"\", @"\\")}}\\",
              "AppName": "App",
              "CatalogNamespace": "ns",
              "CatalogItemId": "item",
              "AppVersionString": "1.2.3"
            }
            """);
        var profile = new GameProfile { Id = "test-game", DisplayName = "Test Game", EpicDisplayName = "Test Game", ProjectFolder = "TestGame" };

        var install = GameLocator.FromFolder(profile, Path.Combine(root, "TestGame"), folder.Combine("Manifests"));

        Assert.NotNull(install);
        Assert.Equal(GameStore.Epic, install.Store);
        Assert.Equal("1.2.3", install.BuildId);
        Assert.StartsWith("com.epicgames.launcher://", install.LaunchUri);
    }

    /// <summary>An Epic manifest for a game installed in <paramref name="installFolder"/>.</summary>
    private static void EpicManifest(TestFolder folder, string displayName, string installFolder) =>
        folder.File(@"Manifests\abc.item", $$"""
            {
              "DisplayName": "{{displayName}}",
              "InstallLocation": "{{installFolder.Replace(@"\", @"\\")}}",
              "AppName": "App",
              "CatalogNamespace": "ns",
              "CatalogItemId": "item",
              "AppVersionString": "1.2.3"
            }
            """);

    [Fact]
    public void An_epic_install_chosen_by_hand_is_found_by_its_folder_whatever_its_name()
    {
        using var folder = new TestFolder();
        var root = folder.Game();
        EpicManifest(folder, "Test Game: Deluxe Edition", root);
        var profile = new GameProfile { Id = "test-game", DisplayName = "Test Game", EpicDisplayName = "Test Game", ProjectFolder = "TestGame" };

        var install = GameLocator.FromFolder(profile, root, folder.Combine("Manifests"));

        Assert.NotNull(install);
        Assert.Equal(GameStore.Epic, install.Store);
        Assert.Equal("1.2.3", install.BuildId);
        Assert.Equal("com.epicgames.launcher://apps/ns%3Aitem%3AApp?action=launch&silent=true", install.LaunchUri);
        Assert.Equal("Test Game: Deluxe Edition", EpicLauncher.FindAppIn(root + @"\", folder.Combine("Manifests"))?.DisplayName);
        Assert.Null(EpicLauncher.FindApp("Test Game", folder.Combine("Manifests")));
    }

    [Fact]
    public void An_epic_install_in_another_folder_is_not_used()
    {
        using var folder = new TestFolder();
        var root = folder.Game();
        EpicManifest(folder, "Test Game", folder.Game(name: "EpicCopy"));
        var profile = new GameProfile { Id = "test-game", DisplayName = "Test Game", EpicDisplayName = "Test Game", ProjectFolder = "TestGame" };

        var install = GameLocator.FromFolder(profile, root, folder.Combine("Manifests"));

        Assert.NotNull(install);
        Assert.Equal(GameStore.Manual, install.Store);
        Assert.Null(install.LaunchUri);
    }

    [Fact]
    public void Another_unreal_game_chosen_by_hand_is_refused()
    {
        using var folder = new TestFolder();
        var root = folder.Game("b1");

        // A store's install may have its project folder detected; a folder chosen by hand may not.
        Assert.Equal("b1", GameLocator.Resolve(TestFolder.Profile(), root, GameStore.Manual)?.ProjectFolder);
        Assert.Null(GameLocator.FromFolder(TestFolder.Profile(), root, folder.Combine("Manifests")));
    }

    [Fact]
    public void A_folder_chosen_by_hand_needs_the_engine_folder()
    {
        using var folder = new TestFolder();
        var root = folder.Game();
        Directory.Delete(Path.Combine(root, "Engine"), recursive: true);

        Assert.Null(GameLocator.FromFolder(TestFolder.Profile(), root, folder.Combine("Manifests")));
    }

    [Fact]
    public void A_folder_chosen_by_hand_needs_the_games_exe()
    {
        using var folder = new TestFolder();
        var root = folder.Game();
        File.Delete(Path.Combine(root, "TestGame", "Binaries", "Win64", "TestGame-Win64-Shipping.exe"));

        Assert.Null(GameLocator.FromFolder(TestFolder.Profile(), root, folder.Combine("Manifests")));
    }

    /// <summary>A game whose exe is in a folder per store, as platform\steam, started by steam\Game.exe.</summary>
    private static GameProfile StoreFolderProfile() => new()
    {
        Id = "test-game",
        DisplayName = "Test Game",
        ProjectFolder = "TestGame",
        BinariesPath = "platform/*",
        ShippingExecutable = "Game.exe",
        Executable = "steam/Game.exe",
    };

    [Fact]
    public void Finds_the_exe_in_a_folder_per_store()
    {
        using var folder = new TestFolder();
        var root = folder.Game();
        folder.File(@"Game\TestGame\Binaries\Win64\CheckEnv.exe", "");
        folder.File(@"Game\platform\steam\Game.exe", "");
        folder.File(@"Game\steam\Game.exe", "");

        var install = GameLocator.FromFolder(StoreFolderProfile(), root, folder.Combine("Manifests"));

        Assert.NotNull(install);
        Assert.Equal(Path.Combine(root, "platform", "steam"), install.BinariesPath);
        Assert.Equal(Path.Combine(root, "platform", "steam", "Game.exe"), install.FindShippingExecutable());
        Assert.Equal(Path.Combine(root, "steam", "Game.exe"), install.FindExecutable());
    }

    [Fact]
    public void The_stores_own_folder_comes_first()
    {
        using var folder = new TestFolder();
        var root = folder.Game();
        folder.File(@"Game\platform\epic\Game.exe", "");
        folder.File(@"Game\platform\steam\Game.exe", "");
        folder.File(@"Game\platform\tools\readme.txt", "");

        Assert.Equal(Path.Combine(root, "platform", "steam"), GameLocator.Resolve(StoreFolderProfile(), root, GameStore.Steam)?.BinariesPath);
        Assert.Equal(Path.Combine(root, "platform", "epic"), GameLocator.Resolve(StoreFolderProfile(), root, GameStore.Epic)?.BinariesPath);
        Assert.Equal(Path.Combine(root, "platform", "epic"), GameLocator.Resolve(StoreFolderProfile(), root, GameStore.Manual)?.BinariesPath);
    }

    [Fact]
    public void Without_its_exe_a_store_folder_game_still_has_its_paks()
    {
        using var folder = new TestFolder();
        var root = folder.Game();

        var install = GameLocator.Resolve(StoreFolderProfile(), root, GameStore.Epic);

        Assert.NotNull(install);
        Assert.Equal(Path.Combine(root, "platform"), install.BinariesPath);
        Assert.Null(install.FindShippingExecutable());
        Assert.Null(GameLocator.FromFolder(StoreFolderProfile(), root, folder.Combine("Manifests")));
    }

    [Fact]
    public void Reads_the_phantom_blade_zero_steam_install()
    {
        using var folder = new TestFolder();
        // The parts of the Steam install that matter, as the game shipped it.
        folder.File(@"PBZ\Engine\Binaries\Win64\EpicWebHelper.exe", "");
        folder.File(@"PBZ\platform\steam\PBZ.exe", "");
        folder.File(@"PBZ\platform\steam\D3D12\x64\D3D12Core.dll", "");
        folder.File(@"PBZ\ProjectPBZ\Binaries\Win64\CheckEnv.exe", "");
        folder.File(@"PBZ\ProjectPBZ\Binaries\Win64\crashpad_handler.exe", "");
        folder.File(@"PBZ\ProjectPBZ\Content\Paks\pakchunk0-Windows.pak", "");
        folder.File(@"PBZ\ProjectPBZ\Saved\SaveGames\Steam\76561190000000000\1RealTime\save.sav", "");
        folder.File(@"PBZ\steam\PBZ.exe", "");
        folder.File(@"PBZ\steam_api64.dll", "");
        var root = folder.Combine("PBZ");
        var profile = Assert.Single(GameProfile.BuiltIn(), p => p.Id == "phantom-blade-zero");

        var install = GameLocator.FromFolder(profile, Path.Combine(root, "ProjectPBZ", "Binaries", "Win64"), folder.Combine("Manifests"));

        Assert.NotNull(install);
        Assert.Equal(root, install.RootPath);
        Assert.Equal("ProjectPBZ", install.ProjectFolder);
        Assert.Equal(Path.Combine(root, "ProjectPBZ", "Content", "Paks", "~mods"), install.ModsPath);
        Assert.Equal(Path.Combine(root, "ProjectPBZ", "Content", "Paks", "LogicMods"), install.LogicModsPath);
        Assert.Equal(Path.Combine(root, "platform", "steam"), install.BinariesPath);
        Assert.Equal(Path.Combine(root, "platform", "steam", "ue4ss", "Mods"), Ue4ssRuntime.Detect(install).ModsPath);
        Assert.Equal(Path.Combine(root, "steam", "PBZ.exe"), install.FindExecutable());
        Assert.Equal(Path.Combine(root, "ProjectPBZ", "Saved", "SaveGames"), SaveBackups.Folder(profile, install, null));
    }
}
