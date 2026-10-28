using System.Text.Json;
using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

public class Ue4ssTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly GameInstall game;
    private readonly ModManager manager;

    public Ue4ssTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")));
    }

    public void Dispose() => folder.Dispose();

    private string Win64 => game.BinariesPath;

    private void InstallUe4ss()
    {
        Directory.CreateDirectory(Path.Combine(Win64, "ue4ss", "Mods"));
        File.WriteAllText(Path.Combine(Win64, "ue4ss", "UE4SS.dll"), "");
        File.WriteAllText(Path.Combine(Win64, "dwmapi.dll"), "");
    }

    /// <summary>The layout of a typical Lua mod download, mirroring Binaries\Win64.</summary>
    private string LuaDownload(string name, bool withEnabledMarker = true)
    {
        folder.File($@"Downloads\{name}_0.2\ue4ss\Mods\{name}\Scripts\main.lua", "print('hello')");
        if (withEnabledMarker)
            folder.File($@"Downloads\{name}_0.2\ue4ss\Mods\{name}\enabled.txt", "");
        return folder.Combine("Downloads", $"{name}_0.2");
    }

    private ModRecord Install(string source)
    {
        using var preview = manager.Inspect(source);
        return manager.Install(preview, preview.Packages.ToList(), preview.SuggestedName, null);
    }

    [Fact]
    public void Recognises_a_lua_mod_folder()
    {
        using var preview = manager.Inspect(LuaDownload("DashTest"));

        var package = Assert.Single(preview.Packages);
        Assert.Equal("DashTest", package.Label);
        Assert.Equal(PackageKind.Ue4ss, package.Kind);
        Assert.Equal(DeployTarget.Ue4ssMods, package.Target);
        Assert.Equal("UE4SS Lua", package.TypeLabel);
        Assert.Equal(
            new[] { "DashTest/Scripts/main.lua", "DashTest/enabled.txt" }.Order(StringComparer.Ordinal),
            package.RelativePaths.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Recognises_a_dll_mod_folder()
    {
        folder.File(@"Downloads\Native\NativeMod\dlls\main.dll");

        using var preview = manager.Inspect(folder.Combine("Downloads", "Native"));

        Assert.Equal("UE4SS DLL", Assert.Single(preview.Packages).TypeLabel);
    }

    [Fact]
    public void Installs_into_the_ue4ss_mods_folder_and_removes_it_again()
    {
        InstallUe4ss();
        var mod = Install(LuaDownload("DashTest"));
        var modFolder = Path.Combine(Win64, "ue4ss", "Mods", "DashTest");

        Assert.True(File.Exists(Path.Combine(modFolder, "Scripts", "main.lua")));
        Assert.True(File.Exists(Path.Combine(modFolder, "enabled.txt")));
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(manager.Mods[0]));
        Assert.Equal("UE4SS Lua", mod.TypeLabel);

        manager.SetEnabled(mod.Id, false);
        Assert.False(Directory.Exists(modFolder));
        Assert.True(Directory.Exists(Path.Combine(Win64, "ue4ss", "Mods")));

        manager.SetEnabled(mod.Id, true);
        Assert.True(File.Exists(Path.Combine(modFolder, "Scripts", "main.lua")));

        manager.Uninstall(mod.Id);
        Assert.False(Directory.Exists(modFolder));
    }

    [Fact]
    public void Packaged_mods_and_installed_ue4ss_need_no_guide()
    {
        folder.File(@"Downloads\Plain\Plain_P.pak");
        using (var preview = manager.Inspect(folder.Combine("Downloads", "Plain")))
            Assert.False(manager.Check(preview, preview.Packages.ToList()).NeedsUe4ss);

        InstallUe4ss();
        using (var preview = manager.Inspect(LuaDownload("DashTest")))
            Assert.False(manager.Check(preview, preview.Packages.ToList()).NeedsUe4ss);
    }

    [Fact]
    public void Finds_the_shipping_executable()
    {
        File.WriteAllText(Path.Combine(Win64, "TestGame-Win64-Shipping.exe"), "");

        Assert.Equal(Path.Combine(Win64, "TestGame-Win64-Shipping.exe"), game.FindShippingExecutable());
    }

    [Fact]
    public void Adds_enabled_txt_when_the_mod_has_none()
    {
        InstallUe4ss();

        var mod = Install(LuaDownload("NoMarker", withEnabledMarker: false));

        Assert.True(File.Exists(Path.Combine(Win64, "ue4ss", "Mods", "NoMarker", "enabled.txt")));
        Assert.Contains(mod.Files, f => f.FileName == "NoMarker/enabled.txt");
    }

    [Fact]
    public void Uses_the_older_layout_of_ue4ss_2()
    {
        File.WriteAllText(Path.Combine(Win64, "UE4SS.dll"), "");

        Install(LuaDownload("DashTest"));

        Assert.True(File.Exists(Path.Combine(Win64, "Mods", "DashTest", "Scripts", "main.lua")));
    }

    [Fact]
    public void Warns_when_ue4ss_is_missing_but_still_installs()
    {
        using (var preview = manager.Inspect(LuaDownload("DashTest")))
        {
            var check = manager.Check(preview, preview.Packages.ToList());
            Assert.True(check.CanInstall);
            Assert.True(check.NeedsUe4ss);
            Assert.Contains(check.Warnings, w => w.Contains("UE4SS is not installed"));
        }

        Install(LuaDownload("DashTest"));

        Assert.True(File.Exists(Path.Combine(Win64, "ue4ss", "Mods", "DashTest", "Scripts", "main.lua")));
    }

    [Fact]
    public void A_hand_installed_ue4ss_mod_is_never_overwritten()
    {
        InstallUe4ss();
        var mine = Path.Combine(Win64, "ue4ss", "Mods", "DashTest", "Scripts", "main.lua");
        Directory.CreateDirectory(Path.GetDirectoryName(mine)!);
        File.WriteAllText(mine, "installed by hand");
        using var preview = manager.Inspect(LuaDownload("DashTest"));

        Assert.False(manager.Check(preview, preview.Packages.ToList()).CanInstall);
        Assert.Throws<ModException>(() => manager.Install(preview, preview.Packages.ToList(), "DashTest", null));
        Assert.Equal("installed by hand", File.ReadAllText(mine));
    }

    [Fact]
    public void The_version_comes_from_the_ue4ss_log_when_the_dll_has_none()
    {
        InstallUe4ss();
        File.WriteAllLines(Path.Combine(Win64, "ue4ss", "UE4SS.log"),
        [
            "[2026-10-27 14:02:11.0412345] Console created",
            "[2026-10-27 14:02:11.0412678] UE4SS - v3.0.1 Beta #0 - Git SHA #d935b5b",
            "[2026-10-27 14:02:11.0413000] Accessing UE4SS-settings.ini",
        ]);

        var runtime = Ue4ssRuntime.Detect(game);

        Assert.True(runtime.Installed);
        Assert.Equal("3.0.1 Beta", runtime.Version);
    }

    [Fact]
    public void The_version_stays_unknown_without_a_log()
    {
        InstallUe4ss();

        Assert.Null(Ue4ssRuntime.Detect(game).Version);
    }

    [Fact]
    public void The_ue4ss_runtime_itself_is_refused()
    {
        folder.File(@"Downloads\UE4SS_v3\dwmapi.dll");
        folder.File(@"Downloads\UE4SS_v3\ue4ss\UE4SS.dll");
        folder.File(@"Downloads\UE4SS_v3\ue4ss\Mods\BPModLoaderMod\Scripts\main.lua");
        using var preview = manager.Inspect(folder.Combine("Downloads", "UE4SS_v3"));

        var check = manager.Check(preview, preview.Packages.ToList());

        Assert.False(check.CanInstall);
        Assert.Contains(check.Problems, p => p.Contains("UE4SS runtime"));
    }

    [Fact]
    public void Blueprint_paks_go_to_logic_mods()
    {
        folder.File(@"Downloads\BpMod\Content\Paks\LogicMods\BpMod_P.pak");

        var mod = Install(folder.Combine("Downloads", "BpMod"));

        Assert.True(File.Exists(Path.Combine(game.LogicModsPath, "BpMod_P.pak")));
        Assert.False(File.Exists(Path.Combine(game.ModsPath, "BpMod_P.pak")));
        Assert.Equal("Blueprint", mod.TypeLabel);
    }

    [Fact]
    public void Records_from_before_ue4ss_support_load_as_packaged_files()
    {
        const string json = """{ "mods": [ { "id": "a", "name": "Old", "files": [ { "fileName": "Old_P.pak", "size": 1, "sha256": "x" } ] } ] }""";

        var state = JsonSerializer.Deserialize<ModState>(json, JsonFile.Options)!;

        Assert.Equal(DeployTarget.Mods, state.Mods[0].Files[0].Target);
        Assert.Equal("PAK", state.Mods[0].TypeLabel);
    }
}
