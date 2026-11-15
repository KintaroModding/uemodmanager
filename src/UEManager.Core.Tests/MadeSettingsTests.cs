using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>
/// A settings file a script mod makes in ~mods while the game runs, such as Auto Collect Loot's PBZAutoCollect.txt: the
/// manager takes it into the mod, keeps it on an update, backs it up on an uninstall, and never takes another mod's file.
/// </summary>
public class MadeSettingsTests : IDisposable
{
    private const string Script = @"TestGame\Content\JavaScript\PBZAutoCollect\main.js";
    private const string Settings = "Enabled=On\nMode=Custom";

    private readonly TestFolder folder = new();
    private readonly GameInstall game;
    private ModManager manager;

    public MadeSettingsTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = Start();
    }

    public void Dispose() => folder.Dispose();

    /// <summary>The app starting again with the same data.</summary>
    private ModManager Start() => new(game, new AppPaths(folder.Combine("Data")));

    private string MadeFile => Path.Combine(game.ModsPath, "PBZAutoCollect.txt");

    private ModRecord Install(string download, string relative = Script, bool keepSettings = true)
    {
        folder.File($@"Downloads\{download}\{relative}", download);
        using var preview = manager.Inspect(folder.Combine("Downloads", download));
        var parts = preview.Packages.ToList();
        var check = manager.Check(preview, parts);
        return manager.Install(preview, parts, "Auto Collect Loot", download, check.Replaces is not null, keepSettings: keepSettings);
    }

    /// <summary>The mod's first game start: it writes its settings file, and the player changes it.</summary>
    private void GameMakesTheFile()
    {
        Directory.CreateDirectory(game.ModsPath);
        File.WriteAllText(MadeFile, Settings);
    }

    [Fact]
    public void The_file_is_taken_at_the_next_start_and_is_no_longer_unmanaged()
    {
        Install("1.7.2");
        GameMakesTheFile();

        manager = Start();

        var file = Assert.Single(manager.Mods.Single().Files, f => f.FileName == "PBZAutoCollect.txt");
        Assert.True(file.Made);
        Assert.True(file.IsSettingsFile);
        Assert.Empty(manager.UnmanagedFiles());
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(manager.Mods.Single()));
        // The mod stays a mod of game files: the file in ~mods does not make it a pak mod.
        Assert.Equal(ModTypes.GameFiles, manager.Mods.Single().TypeLabel);
    }

    [Fact]
    public void The_file_is_taken_when_the_game_closes()
    {
        Install("1.7.2");
        GameMakesTheFile();

        Assert.Equal(1, manager.TakeMadeSettings());
        Assert.Equal(0, manager.TakeMadeSettings());

        Assert.Contains(manager.Mods.Single().Files, f => f.Made && f.FileName == "PBZAutoCollect.txt");
        Assert.Contains(Start().Mods.Single().Files, f => f.Made);
    }

    [Fact]
    public void An_update_keeps_the_players_settings_in_the_game()
    {
        Install("1.7.2");
        GameMakesTheFile();
        manager = Start();
        File.WriteAllText(MadeFile, Settings + "\nRange=20");

        Install("1.7.3");

        var mod = Assert.Single(manager.Mods);
        Assert.Equal("1.7.3", mod.Version);
        Assert.Equal(Settings + "\nRange=20", File.ReadAllText(MadeFile));
        Assert.Contains(mod.Files, f => f.Made && f.FileName == "PBZAutoCollect.txt");
        Assert.Empty(manager.UnmanagedFiles());
    }

    [Fact]
    public void An_update_with_the_new_versions_defaults_leaves_the_file_to_the_mod_and_in_the_backup()
    {
        Install("1.7.2");
        GameMakesTheFile();
        manager = Start();

        Install("1.7.3", keepSettings: false);

        Assert.False(File.Exists(MadeFile));
        Assert.DoesNotContain(manager.Mods.Single().Files, f => f.FileName == "PBZAutoCollect.txt");
        Assert.Contains(Directory.EnumerateFiles(folder.Combine("Data"), "PBZAutoCollect.txt", SearchOption.AllDirectories),
            backup => File.ReadAllText(backup) == Settings);
    }

    [Fact]
    public void Turning_the_mod_off_and_on_keeps_the_file()
    {
        Install("1.7.2");
        GameMakesTheFile();
        manager = Start();
        var id = manager.Mods.Single().Id;

        manager.SetEnabled(id, false);
        Assert.False(File.Exists(MadeFile));

        manager.SetEnabled(id, true);
        Assert.Equal(Settings, File.ReadAllText(MadeFile));
    }

    [Fact]
    public void An_uninstall_removes_the_file_and_backs_it_up()
    {
        Install("1.7.2");
        GameMakesTheFile();
        manager = Start();

        var backup = manager.Uninstall(manager.Mods.Single().Id).SettingsBackup;

        Assert.False(File.Exists(MadeFile));
        Assert.NotNull(backup);
        Assert.Equal(Settings, File.ReadAllText(Path.Combine(backup!, "PBZAutoCollect.txt")));
    }

    [Fact]
    public void Reset_settings_leaves_the_file_the_mod_made()
    {
        Install("1.7.2");
        GameMakesTheFile();
        manager = Start();

        Assert.Null(manager.ResetSettings(manager.Mods.Single().Id));
        Assert.Equal(Settings, File.ReadAllText(MadeFile));
    }

    [Fact]
    public void A_disabled_mod_takes_nothing()
    {
        var mod = Install("1.7.2");
        manager.SetEnabled(mod.Id, false);
        GameMakesTheFile();

        manager = Start();

        Assert.DoesNotContain(manager.Mods.Single().Files, f => f.FileName == "PBZAutoCollect.txt");
        Assert.Single(manager.UnmanagedFiles());
    }

    [Fact]
    public void The_name_comes_from_the_mods_own_folder_even_with_subfolders()
    {
        folder.File(@"Downloads\Guide\TestGame\Content\JavaScript\Guide\UI\Widget\Panel.js", "panel");
        Install("Guide", @"TestGame\Content\JavaScript\Guide\main.js");
        Directory.CreateDirectory(game.ModsPath);
        File.WriteAllText(Path.Combine(game.ModsPath, "Guide.json"), "{}");
        File.WriteAllText(Path.Combine(game.ModsPath, "Widget.json"), "{}");

        Assert.Equal(1, manager.TakeMadeSettings());

        Assert.Equal(["Guide.json"], manager.Mods.Single().Files.Where(f => f.Made).Select(f => f.FileName));
    }

    [Fact]
    public void A_mod_that_is_no_script_takes_nothing()
    {
        // A UE4SS mod: its folder is no script mod's folder.
        Install("Foo", @"Foo\Scripts\main.lua");
        Directory.CreateDirectory(game.ModsPath);
        File.WriteAllText(Path.Combine(game.ModsPath, "Foo.txt"), "settings");
        File.WriteAllText(Path.Combine(game.ModsPath, "Scripts.txt"), "settings");

        Assert.Equal(0, manager.TakeMadeSettings());
    }

    [Fact]
    public void A_settings_file_the_download_brings_into_the_mods_folder_is_not_taken_twice()
    {
        folder.File(@"Downloads\X\TestGame\Content\Paks\~mods\X.json", "{}");
        Install("X", @"TestGame\Content\JavaScript\X\main.js");

        Assert.Equal(0, manager.TakeMadeSettings());

        Assert.Single(manager.Mods.Single().Files, f => f.FileName.EndsWith("X.json", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("ProjectPBZ/PBZModLoader/mods/PVLW_SpeakerHighlight/UI/Widget/SpeakerHighlight.js", "PVLW_SpeakerHighlight")]
    [InlineData("ProjectPBZ/Content/JavaScript/PBZModsMenu/Mods/PBZAutoCollect/main.js", "PBZAutoCollect")]
    [InlineData("ProjectPBZ/Content/JavaScript/UE4SSQuestGuide/guide/main.js", "UE4SSQuestGuide")]
    [InlineData("ProjectPBZ/Content/JavaScript/PBZModsMenu/bootstrap.js", null)]
    [InlineData("ProjectPBZ/Content/JavaScript/PBZModsMenu/Mods/readme.txt", null)]
    [InlineData("ProjectPBZ/Content/JavaScript/PBZModLoader/bootstrap.js", null)]
    [InlineData("ProjectPBZ/PBZModLoader/mods/main.js", null)]
    [InlineData("ProjectPBZ/Content/Paks/~mods/X.json", null)]
    [InlineData("platform/steam/version.dll", null)]
    public void A_script_mods_folder_is_known_by_its_place(string path, string? name)
    {
        Assert.Equal(name, PackageScanner.ScriptModFolder(path, "ProjectPBZ"));
    }
}
