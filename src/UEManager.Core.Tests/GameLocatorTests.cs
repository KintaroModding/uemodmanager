using UEManager.Core.Games;
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
}
