using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>A UE4SS mod's settings, its Scripts/config.lua and the data files in its folder, belong to the player once they change them.</summary>
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

    [Theory]
    [InlineData("Witch/Scripts/config.lua", true)]
    [InlineData("Witch/Scripts/config.json", true)]
    [InlineData("Witch/Scripts/Config.ini", true)]
    [InlineData("Witch/Scripts/settings.json", true)]
    [InlineData("Witch/Scripts/settings.lua", true)]
    [InlineData("Witch/Scripts/settings.txt", true)]
    [InlineData("Witch/Scripts/main.lua", false)]
    [InlineData("Witch/Scripts/config.dll", false)]
    [InlineData("Witch/Scripts/lib/config.lua", false)]
    [InlineData("Witch/config.txt", false)]
    [InlineData("Witch/enabled.txt", false)]
    // Data files anywhere in the mod's folder: a C++ mod's .ini next to main.dll, the tables a mod reads.
    [InlineData("MinimapNative/dlls/Minimap.ini", true)]
    [InlineData("MoreItemDrop/dlls/MoreItemDrop.ini", true)]
    [InlineData("CustomShellSystem/catalog/npc-appearances.css.json", true)]
    [InlineData("Witch/config.json", true)]
    [InlineData("Witch/Scripts/lib/config.json", true)]
    [InlineData("Witch/data.yaml", true)]
    [InlineData("Witch/dlls/main.dll", false)]
    [InlineData("Witch/assets/logo.png", false)]
    [InlineData("Witch/LICENSE.json", false)]
    // UE4SS's own files: loose in its Mods folder, and the libraries every mod shares.
    [InlineData("mods.json", false)]
    [InlineData("shared/Types.json", false)]
    public void A_UE4SS_mods_settings_are_its_data_files_and_config_or_settings_in_its_Scripts_folder(string path, bool expected) =>
        Assert.Equal(expected, SettingsFiles.Is(DeployTarget.Ue4ssMods, path));

    /// <summary>The layout of Better Guard And Dodge: a Lua mod that reads its settings from Scripts/config.json.</summary>
    [Fact]
    public void A_config_json_next_to_main_lua_can_be_edited_and_survives_disable_enable_and_update()
    {
        InstallUe4ss();
        string Download(string version)
        {
            var root = $@"Downloads\Guard_{version}";
            folder.File($@"{root}\BetterGuardAndDodge\enabled.txt", "1");
            folder.File($@"{root}\BetterGuardAndDodge\Scripts\main.lua", "main " + version);
            folder.File($@"{root}\BetterGuardAndDodge\Scripts\config.json", "{ \"GuardDuringDodge\": true }");
            return folder.Combine(root);
        }
        var mod = Install(Download("1.0"));
        var config = Path.Combine(Win64, "ue4ss", "Mods", "BetterGuardAndDodge", "Scripts", "config.json");
        Assert.True(Assert.Single(mod.Files, f => f.FileName.EndsWith("config.json")).IsSettingsFile);

        File.WriteAllText(config, "{ \"GuardDuringDodge\": false }");
        manager.SetEnabled(mod.Id, false);
        manager.SetEnabled(mod.Id, true);
        Assert.Equal("{ \"GuardDuringDodge\": false }", File.ReadAllText(config));

        Install(Download("1.1"), replace: true);
        Assert.Equal("{ \"GuardDuringDodge\": false }", File.ReadAllText(config));
    }

    /// <summary>
    /// The layout of Adaptive Minimap for Mortal Shell II: a C++ mod that reads dlls/Minimap.ini next to main.dll, which
    /// its comments ask the player to edit.
    /// </summary>
    [Fact]
    public void A_cpp_mods_ini_next_to_main_dll_can_be_edited_and_never_blocks_off_update_or_uninstall()
    {
        InstallUe4ss();
        string Download(string version)
        {
            var root = $@"Downloads\Minimap_{version}";
            folder.File($@"{root}\MinimapNative\enabled.txt", "");
            folder.File($@"{root}\MinimapNative\dlls\main.dll", "dll " + version);
            folder.File($@"{root}\MinimapNative\dlls\Minimap.ini", "Size=200");
            return folder.Combine(root);
        }
        var mod = Install(Download("0.2.2"));
        var dlls = Path.Combine(Win64, "ue4ss", "Mods", "MinimapNative", "dlls");
        var ini = Path.Combine(dlls, "Minimap.ini");
        Assert.True(Assert.Single(mod.Files, f => f.FileName.EndsWith("Minimap.ini")).IsSettingsFile);

        File.WriteAllText(ini, "Size=320");
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(manager.Mods[0]));
        manager.SetEnabled(mod.Id, false);
        Assert.False(File.Exists(ini));
        manager.SetEnabled(mod.Id, true);
        Assert.Equal("Size=320", File.ReadAllText(ini));

        mod = Install(Download("0.2.3"), replace: true);
        Assert.Equal("Size=320", File.ReadAllText(ini));
        Assert.Equal("dll 0.2.3", File.ReadAllText(Path.Combine(dlls, "main.dll")));

        Assert.NotNull(manager.Uninstall(mod.Id).SettingsBackup);
        Assert.False(File.Exists(ini));
        Assert.Contains(Backups(), f => File.ReadAllText(f) == "Size=320");
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

        var error = Assert.Throws<ChangedFilesException>(() => manager.SetEnabled(mod.Id, false));

        Assert.Equal([mod.Id], error.ModIds);
        Assert.True(File.Exists(Config));
        Assert.Equal("edited by hand", File.ReadAllText(Main));
    }

    [Fact]
    public void A_kept_main_lua_no_longer_stops_a_removal_and_stays_the_players()
    {
        var mod = Install(Download("1.0"));
        File.WriteAllText(Main, "edited by hand");

        Assert.Equal(["Witch/Scripts/main.lua"], manager.KeepChangedFiles(mod.Id));
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(manager.Mods.Single()));
        // A later edit needs no second keep, and the settings editor still shows only config.lua.
        File.WriteAllText(Main, "edited again, a little longer");
        Assert.Empty(manager.KeepChangedFiles(mod.Id));
        Assert.Equal(["Witch/Scripts/config.lua"], manager.Mods.Single().Files.Where(f => f.IsSettingsFile).Select(f => f.FileName));

        manager.SetEnabled(mod.Id, false);
        Assert.False(File.Exists(Main));
        manager.SetEnabled(mod.Id, true);
        Assert.Equal("edited again, a little longer", File.ReadAllText(Main));

        var backup = manager.Uninstall(mod.Id).SettingsBackup;
        Assert.NotNull(backup);
        Assert.Equal("edited again, a little longer", File.ReadAllText(Path.Combine(backup, "Witch", "Scripts", "main.lua")));
    }

    [Fact]
    public void Reset_settings_puts_back_the_mods_own_version_of_a_kept_file()
    {
        var mod = Install(Download("1.0"));
        File.WriteAllText(Main, "edited by hand");
        manager.KeepChangedFiles(mod.Id);

        Assert.NotNull(manager.ResetSettings(mod.Id));

        Assert.Equal("main 1.0", File.ReadAllText(Main));
        Assert.True(manager.Mods.Single().Files.Single(f => f.FileName.EndsWith("main.lua")).Kept);
    }

    [Fact]
    public void An_update_asks_about_a_kept_file_and_it_stays_kept()
    {
        var mod = Install(Download("1.0"));
        File.WriteAllText(Main, "edited by hand");
        manager.KeepChangedFiles(mod.Id);

        using (var preview = manager.Inspect(Download("1.1")))
        {
            Assert.Equal(["Witch/Scripts/main.lua"], manager.Check(preview, preview.Packages.ToList()).EditedSettings);
            manager.Install(preview, preview.Packages.ToList(), "Witch", "1.1", replace: true, keepSettings: true);
        }

        Assert.Equal("edited by hand", File.ReadAllText(Main));
        Assert.True(manager.Mods.Single().Files.Single(f => f.FileName.EndsWith("main.lua")).Kept);
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

        var backup = manager.Uninstall(mod.Id).SettingsBackup;

        Assert.NotNull(backup);
        Assert.Equal("speed = 5", File.ReadAllText(Path.Combine(backup, "Witch", "Scripts", "config.lua")));
        Assert.False(Directory.Exists(ModFolder));
    }

    [Fact]
    public void Uninstall_without_changes_makes_no_backup()
    {
        var mod = Install(Download("1.0"));

        Assert.Null(manager.Uninstall(mod.Id).SettingsBackup);
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
