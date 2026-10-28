using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>A UE4SS mod's Scripts/config.lua belongs to the player once they change it.</summary>
public class SettingsFileTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly GameInstall game;
    private readonly ModManager manager;

    public SettingsFileTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")));
    }

    public void Dispose() => folder.Dispose();

    private string Win64 => game.BinariesPath;

    private string ModFolder => Path.Combine(Win64, "ue4ss", "Mods", "Witch");

    private string Config => Path.Combine(ModFolder, "Scripts", "config.lua");

    private string Main => Path.Combine(ModFolder, "Scripts", "main.lua");

    /// <summary>The layout of a KintaroModding UE4SS zip: ue4ss/Mods/&lt;folder&gt;/, settings in Scripts/config.lua.</summary>
    private string Download(string version, string config = "speed = 1")
    {
        var root = $@"Downloads\Witch_{version}";
        folder.File($@"{root}\ue4ss\Mods\Witch\enabled.txt", "");
        folder.File($@"{root}\ue4ss\Mods\Witch\Scripts\main.lua", "main " + version);
        folder.File($@"{root}\ue4ss\Mods\Witch\Scripts\config.lua", config);
        return folder.Combine(root);
    }

    private ModRecord Install(string source, bool replace = false, bool keepSettings = true)
    {
        using var preview = manager.Inspect(source);
        return manager.Install(preview, preview.Packages.ToList(), "Witch", preview.SuggestedVersion, replace, keepSettings: keepSettings);
    }

    private string[] Backups()
    {
        var backups = folder.Combine("Data", "games", "test-game", "backups");
        return Directory.Exists(backups) ? Directory.GetFiles(backups, "*", SearchOption.AllDirectories) : [];
    }

    private void InstallUe4ss()
    {
        Directory.CreateDirectory(Path.Combine(Win64, "ue4ss", "Mods"));
        File.WriteAllText(Path.Combine(Win64, "ue4ss", "UE4SS.dll"), "");
        File.WriteAllText(Path.Combine(Win64, "dwmapi.dll"), "");
    }

    [Fact]
    public void Changed_settings_are_no_problem_and_survive_disable_and_enable()
    {
        var mod = Install(Download("1.0"));
        File.WriteAllText(Config, "speed = 5");

        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(manager.Mods[0]));
        manager.SetEnabled(mod.Id, false);
        Assert.False(File.Exists(Config));
        manager.SetEnabled(mod.Id, true);

        Assert.Equal("speed = 5", File.ReadAllText(Config));
    }

    [Fact]
    public void Other_changed_files_still_stop_a_removal()
    {
        var mod = Install(Download("1.0"));
        File.WriteAllText(Main, "edited by hand");

        Assert.Throws<ModException>(() => manager.SetEnabled(mod.Id, false));

        Assert.True(File.Exists(Config));
        Assert.Equal("edited by hand", File.ReadAllText(Main));
    }

    [Fact]
    public void An_update_keeps_the_players_settings_and_backs_them_up()
    {
        Install(Download("1.0"));
        File.WriteAllText(Config, "speed = 5");

        using (var preview = manager.Inspect(Download("1.1", "speed = 1, new = true")))
        {
            var check = manager.Check(preview, preview.Packages.ToList());
            Assert.Equal(["Witch/Scripts/config.lua"], check.EditedSettings);
            manager.Install(preview, preview.Packages.ToList(), "Witch", "1.1", replace: true, keepSettings: true);
        }

        Assert.Equal("main 1.1", File.ReadAllText(Main));
        Assert.Equal("speed = 5", File.ReadAllText(Config));
        Assert.Equal("speed = 5", File.ReadAllText(Assert.Single(Backups())));
        // Still the player's own settings, compared with the defaults of 1.1.
        using (var next = manager.Inspect(Download("1.2")))
            Assert.True(manager.Check(next, next.Packages.ToList()).HasEditedSettings);
    }

    [Fact]
    public void An_update_can_take_the_new_defaults()
    {
        Install(Download("1.0"));
        File.WriteAllText(Config, "speed = 5");

        Install(Download("1.1", "speed = 1, new = true"), replace: true, keepSettings: false);

        Assert.Equal("speed = 1, new = true", File.ReadAllText(Config));
        Assert.Equal("speed = 5", File.ReadAllText(Assert.Single(Backups())));
    }

    [Fact]
    public void Unchanged_settings_take_the_new_defaults_without_a_question()
    {
        Install(Download("1.0"));

        using (var preview = manager.Inspect(Download("1.1", "speed = 2")))
        {
            Assert.False(manager.Check(preview, preview.Packages.ToList()).HasEditedSettings);
            manager.Install(preview, preview.Packages.ToList(), "Witch", "1.1", replace: true);
        }

        Assert.Equal("speed = 2", File.ReadAllText(Config));
        Assert.Empty(Backups());
    }

    [Fact]
    public void Uninstall_backs_up_changed_settings()
    {
        var mod = Install(Download("1.0"));
        File.WriteAllText(Config, "speed = 5");

        var backup = manager.Uninstall(mod.Id);

        Assert.NotNull(backup);
        Assert.Equal("speed = 5", File.ReadAllText(Path.Combine(backup, "Witch", "Scripts", "config.lua")));
        Assert.False(Directory.Exists(ModFolder));
    }

    [Fact]
    public void Uninstall_without_changes_makes_no_backup()
    {
        var mod = Install(Download("1.0"));

        Assert.Null(manager.Uninstall(mod.Id));
        Assert.Empty(Backups());
    }

    [Fact]
    public void Removing_ue4ss_keeps_changed_settings_in_the_library()
    {
        InstallUe4ss();
        var mod = Install(Download("1.0"));
        File.WriteAllText(Config, "speed = 5");

        manager.RemoveUe4ss();
        InstallUe4ss();
        manager.SetEnabled(mod.Id, true);

        Assert.Equal("speed = 5", File.ReadAllText(Config));
    }

    [Fact]
    public void Reset_puts_back_the_shipped_settings_after_a_backup()
    {
        var mod = Install(Download("1.0"));
        File.WriteAllText(Config, "speed = 5");
        Assert.True(ModManager.HasEditedSettings(manager.Mods[0]));

        var backup = manager.ResetSettings(mod.Id);

        Assert.Equal("speed = 1", File.ReadAllText(Config));
        Assert.NotNull(backup);
        Assert.Equal("speed = 5", File.ReadAllText(Path.Combine(backup, "Witch", "Scripts", "config.lua")));
        Assert.False(ModManager.HasEditedSettings(manager.Mods[0]));
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(manager.Mods[0]));
        manager.SetEnabled(mod.Id, false);
        manager.SetEnabled(mod.Id, true);
        Assert.Equal("speed = 1", File.ReadAllText(Config));
    }

    [Fact]
    public void Reset_after_an_update_that_kept_the_settings_takes_the_new_defaults()
    {
        Install(Download("1.0"));
        File.WriteAllText(Config, "speed = 5");
        var updated = Install(Download("2.0", config: "speed = 2"), replace: true, keepSettings: true);
        Assert.Equal("speed = 5", File.ReadAllText(Config));

        manager.ResetSettings(updated.Id);

        Assert.Equal("speed = 2", File.ReadAllText(Config));
    }

    [Fact]
    public void Reset_works_on_a_disabled_mod_and_does_nothing_without_changes()
    {
        var mod = Install(Download("1.0"));
        Assert.Null(manager.ResetSettings(mod.Id));
        File.WriteAllText(Config, "speed = 5");
        manager.SetEnabled(mod.Id, false);

        Assert.NotNull(manager.ResetSettings(mod.Id));
        manager.SetEnabled(mod.Id, true);

        Assert.Equal("speed = 1", File.ReadAllText(Config));
        Assert.Single(Backups());
    }

    [Fact]
    public void A_reset_that_cannot_be_saved_changes_nothing()
    {
        var mod = Install(Download("1.0"));
        File.WriteAllText(Config, "speed = 5");
        var list = new AppPaths(folder.Combine("Data")).StateFile(game.Profile.Id);
        var libraryCopy = Path.Combine(manager.LibraryFolderOf(mod), "Ue4ssMods", "Witch", "Scripts", "config.lua");

        using (File.Open(list, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<ModException>(() => manager.ResetSettings(mod.Id));

        Assert.Equal("speed = 5", File.ReadAllText(Config));
        Assert.Equal("speed = 5", File.ReadAllText(libraryCopy));
        Assert.True(ModManager.HasEditedSettings(manager.Mods[0]));
        Assert.True(ModManager.HasEditedSettings(new ModManager(game, new AppPaths(folder.Combine("Data"))).Mods[0]));

        manager.ResetSettings(mod.Id);
        Assert.Equal("speed = 1", File.ReadAllText(Config));
    }
}
