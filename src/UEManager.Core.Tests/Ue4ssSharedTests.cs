using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>
/// Lua libraries a UE4SS mod brings for UE4SS's shared folder, laid out as in the ModConfigUI download:
/// ue4ss/Mods/ModConfigUI/Scripts/main.lua next to ue4ss/Mods/shared/json/json.lua.
/// </summary>
public class Ue4ssSharedTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly GameInstall game;
    private readonly ModManager manager;

    public Ue4ssSharedTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")), _ => false);
        // UE4SS 3.x with one of its own shared libraries.
        File.WriteAllText(Path.Combine(game.BinariesPath, "dwmapi.dll"), "proxy");
        Directory.CreateDirectory(Path.Combine(game.BinariesPath, "ue4ss", "Mods", "shared", "UEHelpers"));
        File.WriteAllText(Path.Combine(game.BinariesPath, "ue4ss", "UE4SS.dll"), "runtime");
        File.WriteAllText(UeHelpers, "ue4ss helpers");
    }

    public void Dispose() => folder.Dispose();

    private string ModsPath => Ue4ssRuntime.Detect(game).ModsPath;

    private string Json => Path.Combine(ModsPath, "shared", "json", "json.lua");

    private string UeHelpers => Path.Combine(game.BinariesPath, "ue4ss", "Mods", "shared", "UEHelpers", "UEHelpers.lua");

    /// <summary>A download with a Lua mod and the json library it loads, and optionally one of UE4SS's own libraries.</summary>
    private string Download(string name, string json = "json 1", string? helpers = null)
    {
        var mods = $@"Downloads\{name}\platform\steam\ue4ss\Mods";
        folder.File($@"Downloads\{name}\modinfo.ini", "[ModInfo]");
        folder.File($@"{mods}\{name}\Scripts\main.lua", "local json = require('json')");
        folder.File($@"{mods}\{name}\enabled.txt", "");
        folder.File($@"{mods}\shared\json\json.lua", json);
        if (helpers is not null)
            folder.File($@"{mods}\shared\UEHelpers\UEHelpers.lua", helpers);
        return folder.Combine("Downloads", name);
    }

    private ModRecord Install(string name, string json = "json 1", string? helpers = null)
    {
        using var preview = manager.Inspect(Download(name, json, helpers));
        return manager.Install(preview, preview.Packages.ToList(), name, null);
    }

    [Fact]
    public void A_mod_brings_its_shared_libraries_and_takes_them_out_again()
    {
        using (var preview = manager.Inspect(Download("Witch")))
        {
            var package = Assert.Single(preview.Packages);
            Assert.Equal(["shared/json/json.lua", "Witch/enabled.txt", "Witch/Scripts/main.lua"], package.RelativePaths);
            Assert.Equal(1, preview.IgnoredFileCount);
        }
        var witch = Install("Witch");

        Assert.Equal("json 1", File.ReadAllText(Json));
        manager.SetEnabled(witch.Id, false);
        Assert.False(File.Exists(Json));
        Assert.False(Directory.Exists(Path.GetDirectoryName(Json)));
        Assert.Equal("ue4ss helpers", File.ReadAllText(UeHelpers));

        manager.SetEnabled(witch.Id, true);
        Assert.True(File.Exists(Json));
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(manager.Mods.Single()));
    }

    [Fact]
    public void Two_mods_share_one_copy_and_it_goes_with_the_last_of_them()
    {
        var witch = Install("Witch");
        using (var preview = manager.Inspect(Download("Hunter")))
            Assert.Null(manager.Check(preview, preview.Packages.ToList()).Replaces);
        var hunter = Install("Hunter");

        manager.SetEnabled(witch.Id, false);
        Assert.True(File.Exists(Json));
        manager.Uninstall(witch.Id);
        Assert.True(File.Exists(Json));
        manager.SetEnabled(hunter.Id, false);
        Assert.False(File.Exists(Json));
    }

    [Fact]
    public void Turning_both_off_and_on_together_works()
    {
        Install("Witch");
        Install("Hunter");

        Assert.Equal(2, manager.TurnOffForTest());
        Assert.False(File.Exists(Json));
        Assert.Equal(2, manager.TurnBackOn());
        Assert.Equal("json 1", File.ReadAllText(Json));
    }

    [Fact]
    public void Another_version_in_use_by_an_enabled_mod_is_refused()
    {
        var witch = Install("Witch", json: "json 1");

        using (var preview = manager.Inspect(Download("Hunter", json: "json 2")))
        {
            var check = manager.Check(preview, preview.Packages.ToList());
            Assert.Contains(check.Problems, p => p.ToString().Contains("\"Witch\" uses it in another version"));
        }
        Assert.Throws<ModException>(() => Install("Hunter", json: "json 2"));
        Assert.Equal("json 1", File.ReadAllText(Json));

        manager.SetEnabled(witch.Id, false);
        Install("Hunter", json: "json 2");
        Assert.Equal("json 2", File.ReadAllText(Json));
        Assert.Throws<ModException>(() => manager.SetEnabled(witch.Id, true));
        Assert.Equal("json 2", File.ReadAllText(Json));
    }

    [Fact]
    public void A_library_UE4SS_already_has_is_used_and_never_removed()
    {
        var witch = Install("Witch", helpers: "ue4ss helpers");

        Assert.True(manager.Mods.Single().Files.Single(f => f.FileName == "shared/UEHelpers/UEHelpers.lua").Borrowed);
        // A UE4SS update may change its own file; the mod still comes out cleanly.
        File.WriteAllText(UeHelpers, "ue4ss helpers, newer");
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(manager.Mods.Single()));
        manager.Uninstall(witch.Id);

        Assert.Equal("ue4ss helpers, newer", File.ReadAllText(UeHelpers));
        Assert.False(File.Exists(Json));
    }

    [Fact]
    public void Another_version_of_a_library_the_manager_did_not_place_is_refused()
    {
        using var preview = manager.Inspect(Download("Witch", helpers: "old helpers"));

        var check = manager.Check(preview, preview.Packages.ToList());

        Assert.Contains(check.Problems, p => p.ToString().Contains("shared/UEHelpers/UEHelpers.lua is already in"));
        Assert.Equal("ue4ss helpers", File.ReadAllText(UeHelpers));
    }
}
