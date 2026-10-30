using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>
/// A folder for UE4SS's Mods folder without code, laid out as in the AutoSwitchWeapon download: a Blueprint mod in
/// LogicMods and ue4ss/Mods/AutoSwitchWeapon/modinfo.ini, the folder where the mod writes the settings ModConfigUI shows.
/// </summary>
public class Ue4ssFolderTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly GameInstall game;
    private readonly ModManager manager;

    public Ue4ssFolderTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")), _ => false);
        File.WriteAllText(Path.Combine(game.BinariesPath, "dwmapi.dll"), "proxy");
        Directory.CreateDirectory(Path.Combine(game.BinariesPath, "ue4ss", "Mods"));
        File.WriteAllText(Path.Combine(game.BinariesPath, "ue4ss", "UE4SS.dll"), "runtime");
    }

    public void Dispose() => folder.Dispose();

    private string ModFolder => Path.Combine(Ue4ssRuntime.Detect(game).ModsPath, "AutoSwitchWeapon");

    private string Download()
    {
        const string top = @"Downloads\AutoSwitchWeapon";
        folder.File($@"{top}\modinfo.ini", "[ModInfo]");
        foreach (var extension in new[] { ".pak", ".utoc", ".ucas" })
            folder.File($@"{top}\ProjectPBZ\Content\Paks\LogicMods\AutoSwitchWeapon{extension}", extension);
        folder.File($@"{top}\platform\steam\ue4ss\Mods\AutoSwitchWeapon\modinfo.ini", "[ModInfo]\nname=AutoSwitchWeapon");
        return folder.Combine("Downloads", "AutoSwitchWeapon");
    }

    private ModRecord Install()
    {
        using var preview = manager.Inspect(Download());
        return manager.Install(preview, preview.Packages.ToList(), "AutoSwitchWeapon", "1.0");
    }

    [Fact]
    public void The_folder_is_a_part_of_its_own_next_to_the_Blueprint_mod()
    {
        using var preview = manager.Inspect(Download());

        Assert.Equal(2, preview.Packages.Count);
        var blueprint = preview.Packages.Single(p => p.Target == DeployTarget.LogicMods);
        Assert.Equal(PackageKind.IoStore, blueprint.Kind);
        var settingsFolder = preview.Packages.Single(p => p.Target == DeployTarget.Ue4ssMods);
        Assert.Equal(PackageKind.Ue4ssFolder, settingsFolder.Kind);
        Assert.Equal(["AutoSwitchWeapon/modinfo.ini"], settingsFolder.RelativePaths);
        Assert.Equal(ModTypes.Ue4ssFiles, settingsFolder.TypeLabel);
        Assert.Equal(1, preview.IgnoredFileCount);
    }

    [Fact]
    public void Both_go_into_the_game_as_one_Blueprint_mod_without_enabled_txt()
    {
        var mod = Install();

        Assert.Equal("Blueprint", mod.TypeLabel);
        Assert.True(File.Exists(Path.Combine(game.LogicModsPath, "AutoSwitchWeapon.utoc")));
        Assert.True(File.Exists(Path.Combine(ModFolder, "modinfo.ini")));
        Assert.False(File.Exists(Path.Combine(ModFolder, "enabled.txt")));
    }

    [Fact]
    public void Settings_the_mod_writes_into_the_folder_stay_when_it_leaves()
    {
        var mod = Install();
        var settings = Path.Combine(ModFolder, "AutoSwitchWeapon.config.json");
        File.WriteAllText(settings, "{\"Enabled\": true}");

        manager.SetEnabled(mod.Id, false);
        Assert.False(File.Exists(Path.Combine(ModFolder, "modinfo.ini")));
        Assert.True(File.Exists(settings));

        manager.SetEnabled(mod.Id, true);
        Assert.True(File.Exists(Path.Combine(ModFolder, "modinfo.ini")));
        manager.Uninstall(mod.Id);
        Assert.Equal("{\"Enabled\": true}", File.ReadAllText(settings));
    }

    [Fact]
    public void Files_loose_in_the_Mods_folder_and_a_shared_folder_without_a_Lua_mod_stay_out()
    {
        folder.File(@"Downloads\Loose\ue4ss\Mods\mods.txt", "AutoSwitchWeapon : 1");
        folder.File(@"Downloads\Loose\ue4ss\Mods\shared\lib\lib.lua", "return {}");
        folder.File(@"Downloads\Loose\~mods\Loose_P.pak", "pak");

        using var preview = manager.Inspect(folder.Combine("Downloads", "Loose"));

        Assert.Equal(["Loose_P.pak"], Assert.Single(preview.Packages).RelativePaths);
        Assert.Equal(2, preview.IgnoredFileCount);
    }
}
