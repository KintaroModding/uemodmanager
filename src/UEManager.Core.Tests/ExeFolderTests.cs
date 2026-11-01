using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>
/// A download that goes next to the game's executable, which content packages cannot stand in for: a proxy DLL such
/// as dxgi.dll, a ReShade add-on, an .asi loader. Phantom Blade Zero keeps its exe in platform/steam.
/// </summary>
public class ExeFolderTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly ModManager manager;
    private readonly GameInstall game;

    public ExeFolderTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")));
    }

    public void Dispose() => folder.Dispose();

    private string ExeFile(string name) => Path.Combine(game.BinariesPath, name);

    /// <summary>A ReShade-style download: the proxy DLL, its add-on, its settings and a readme.</summary>
    private string Download(string name = "ReShade")
    {
        folder.File($@"Downloads\{name}\dxgi.dll", "the proxy dll");
        folder.File($@"Downloads\{name}\renodx-mfgunlock.addon64", "the add-on");
        folder.File($@"Downloads\{name}\ReShade.ini", "the settings");
        folder.File($@"Downloads\{name}\README.txt", "install instructions by hand");
        return folder.Combine("Downloads", name);
    }

    private (InstallCheck Check, InstallPreview Preview) Inspect(string source)
    {
        var preview = manager.Inspect(source);
        return (manager.Check(preview, preview.Packages.ToList()), preview);
    }

    private ModRecord Install(string source)
    {
        using var preview = manager.Inspect(source);
        return manager.Install(preview, preview.Packages.ToList(), preview.SuggestedName, preview.SuggestedVersion);
    }

    [Fact]
    public void A_proxy_dll_and_the_files_that_load_with_it_are_one_part()
    {
        var (_, preview) = Inspect(Download());
        using var _preview = preview;

        var part = Assert.Single(preview.Packages, p => p.Target == DeployTarget.ExeFolder);
        Assert.Equal(PackageKind.ExeFiles, part.Kind);
        Assert.Equal(["dxgi.dll", "renodx-mfgunlock.addon64", "ReShade.ini"], part.RelativePaths);
        Assert.Equal(ModTypes.ExeFiles, part.TypeLabel);
        Assert.Equal(1, preview.IgnoredFileCount);
    }

    [Fact]
    public void Installing_puts_the_files_next_to_the_exe()
    {
        var mod = Install(Download());

        Assert.Equal(DeployTarget.ExeFolder, mod.Files[0].Target);
        Assert.Equal(ModTypes.ExeFiles, mod.TypeLabel);
        Assert.Equal("the proxy dll", File.ReadAllText(ExeFile("dxgi.dll")));
        Assert.Equal("the add-on", File.ReadAllText(ExeFile("renodx-mfgunlock.addon64")));
        Assert.Equal("the settings", File.ReadAllText(ExeFile("ReShade.ini")));
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(mod));
    }

    [Fact]
    public void A_file_of_another_tool_is_saved_and_put_back()
    {
        // A ReShade build that was installed by hand earlier.
        folder.File($@"Game\TestGame\Binaries\Win64\dxgi.dll", "the hand-installed ReShade");

        var mod = Install(Download());
        Assert.Equal("the proxy dll", File.ReadAllText(ExeFile("dxgi.dll")));

        manager.Uninstall(mod.Id);

        Assert.Equal("the hand-installed ReShade", File.ReadAllText(ExeFile("dxgi.dll")));
        Assert.False(File.Exists(ExeFile("renodx-mfgunlock.addon64")));
    }

    [Fact]
    public void Disabling_takes_the_files_out_and_enabling_puts_them_back()
    {
        var mod = Install(Download());

        manager.SetEnabled(mod.Id, false);
        Assert.False(File.Exists(ExeFile("dxgi.dll")));

        manager.SetEnabled(mod.Id, true);
        Assert.Equal("the proxy dll", File.ReadAllText(ExeFile("dxgi.dll")));
    }

    [Fact]
    public void A_download_that_names_the_exe_folder_keeps_its_paths()
    {
        var exeFolder = Path.GetRelativePath(game.RootPath, game.BinariesPath).Replace('\\', '/');
        folder.File($@"Downloads\Payload\payload\{exeFolder}\dxgi.dll", "the proxy dll");
        folder.File($@"Downloads\Payload\payload\{exeFolder}\ReShade.ini", "the settings");

        using var preview = manager.Inspect(folder.Combine("Downloads", "Payload"));
        var part = Assert.Single(preview.Packages, p => p.Target == DeployTarget.InstallRoot);
        Assert.Equal([$"{exeFolder}/dxgi.dll", $"{exeFolder}/ReShade.ini"], part.RelativePaths);

        var mod = manager.Install(preview, preview.Packages.ToList(), preview.SuggestedName, preview.SuggestedVersion);
        Assert.Equal("the proxy dll", File.ReadAllText(ExeFile("dxgi.dll")));
        Assert.Equal(2, mod.Files.Count);
    }

    [Fact]
    public void A_file_of_the_installed_ue4ss_is_refused()
    {
        var record = Path.Combine(folder.Combine("Data"), "games", "test-game", "ue4ss.json");
        Directory.CreateDirectory(Path.GetDirectoryName(record)!);
        File.WriteAllText(record, """{"Version":"v3.0.1","Files":{"dwmapi.dll":"0000"}}""");
        folder.File(@"Downloads\Ue4ss\ue4ss\dwmapi.dll", "UE4SS's own proxy");

        using var preview = manager.Inspect(folder.Combine("Downloads", "Ue4ss"));
        var check = manager.Check(preview, preview.Packages.ToList());

        Assert.False(check.CanInstall);
        Assert.Contains(check.Problems, p => p.ToString().Contains("belongs to UE4SS"));
    }
}
