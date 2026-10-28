using System.Net;
using System.Text;
using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

public class Ue4ssManifestTests : IDisposable
{
    private const string Manifest = """
        {
          "ue4ss": {
            "mortal-shell-2": {
              "version": "v3.0.1-1181-g83330635",
              "url": "https://github.com/KintaroModding/ue4ss/releases/download/v3.0.1-1181-g83330635/mortalshell2_ue4ss_v3.0.1-1181-g83330635.zip",
              "sha256": "A155C673506D34AA78E4C20579E39DAB35F62CB68EA04D4263E5EAE39066AC4A"
            },
            "plain-http": { "version": "v1", "url": "http://example.test/ue4ss.zip" }
          }
        }
        """;

    private readonly TestFolder folder = new();
    private readonly GameInstall game;
    private readonly ModManager manager;

    public Ue4ssManifestTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")));
    }

    public void Dispose() => folder.Dispose();

    private string Win64 => game.BinariesPath;

    [Fact]
    public void Reads_the_entry_for_the_game()
    {
        var release = Ue4ssManifest.Parse(Manifest, "mortal-shell-2");

        Assert.NotNull(release);
        Assert.Equal("v3.0.1-1181-g83330635", release.Version);
        Assert.Equal("mortalshell2_ue4ss_v3.0.1-1181-g83330635.zip", release.FileName);
        Assert.StartsWith("A155C673", release.Sha256);
    }

    [Theory]
    [InlineData("phantom-blade-zero")]
    [InlineData("plain-http")]
    public void Missing_or_unsafe_entries_are_ignored(string gameId)
    {
        Assert.Null(Ue4ssManifest.Parse(Manifest, gameId));
    }

    [Fact]
    public async Task Fetching_returns_the_entry_and_fails_quietly()
    {
        using var ok = new HttpClient(new FixedResponse(HttpStatusCode.OK, Manifest));
        using var missing = new HttpClient(new FixedResponse(HttpStatusCode.NotFound, ""));
        using var broken = new HttpClient(new FixedResponse(HttpStatusCode.OK, "{ not json"));

        Assert.NotNull(await Ue4ssManifest.FetchAsync(ok, "https://example.test/manifest.json", "mortal-shell-2"));
        Assert.Null(await Ue4ssManifest.FetchAsync(missing, "https://example.test/manifest.json", "mortal-shell-2"));
        Assert.Null(await Ue4ssManifest.FetchAsync(broken, "https://example.test/manifest.json", "mortal-shell-2"));
    }

    [Fact]
    public void An_update_is_offered_for_another_or_an_unknown_version()
    {
        var latest = new Ue4ssRelease("v3.0.2", "https://example.test/ue4ss.zip", null);

        Assert.True(Ue4ssManifest.IsUpdate("v3.0.1", latest));
        Assert.True(Ue4ssManifest.IsUpdate(null, latest));
        Assert.False(Ue4ssManifest.IsUpdate("V3.0.2", latest));
        Assert.False(Ue4ssManifest.IsUpdate("v3.0.1", null));
    }

    [Theory]
    [InlineData(@"C:\Downloads\mortalshell2_ue4ss_v3.0.1-1181-g83330635.zip", "v3.0.1-1181-g83330635")]
    [InlineData(@"C:\Downloads\UE4SS_v3.0.1.zip", "v3.0.1")]
    [InlineData(@"C:\Downloads\ue4ss.zip", null)]
    public void The_version_comes_from_the_file_name(string path, string? version)
    {
        Assert.Equal(version, Ue4ssManifest.VersionFromFileName(path));
    }

    private string Release(string name)
    {
        folder.File($@"{name}\dwmapi.dll");
        folder.File($@"{name}\ue4ss\UE4SS.dll");
        folder.File($@"{name}\ue4ss\UE4SS-settings.ini");
        folder.File($@"{name}\ue4ss\Mods\BPModLoaderMod\Scripts\main.lua");
        return folder.Combine(name);
    }

    [Fact]
    public void The_installed_version_is_recorded()
    {
        manager.InstallUe4ss(Release("pbz_ue4ss_v3.0.1-7-gabc"));
        Assert.Equal("v3.0.1-7-gabc", manager.InstalledUe4ssVersion);

        manager.InstallUe4ss(Release("any"), "v9.9.9");
        Assert.Equal("v9.9.9", manager.InstalledUe4ssVersion);
    }

    [Fact]
    public void Removing_clears_ue4ss_and_disables_its_mods_but_keeps_the_library()
    {
        manager.InstallUe4ss(Release("UE4SS_v3.0.1"));
        folder.File(@"Downloads\DashTest\ue4ss\Mods\DashTest\Scripts\main.lua");
        ModRecord mod;
        using (var preview = manager.Inspect(folder.Combine("Downloads", "DashTest")))
            mod = manager.Install(preview, preview.Packages.ToList(), "DashTest", null);
        Directory.CreateDirectory(Path.Combine(Win64, "ue4ss", "Mods", "ByHand"));
        File.WriteAllText(Path.Combine(Win64, "ue4ss", "Mods", "ByHand", "enabled.txt"), "");

        var result = manager.RemoveUe4ss();

        Assert.Equal(1, result.DisabledMods);
        Assert.False(Directory.Exists(Path.Combine(Win64, "ue4ss")));
        Assert.False(File.Exists(Path.Combine(Win64, "dwmapi.dll")));
        Assert.Empty(Directory.GetDirectories(Win64, "*uemm-removing*"));
        Assert.False(Ue4ssRuntime.Detect(game).Installed);
        Assert.Null(manager.InstalledUe4ssVersion);

        var disabled = Assert.Single(manager.Mods);
        Assert.False(disabled.Enabled);
        Assert.Equal(ModIntegrity.NotDeployed, ModManager.Integrity(disabled));
        Assert.True(File.Exists(Path.Combine(manager.LibraryFolderOf(mod), "Ue4ssMods", "DashTest", "Scripts", "main.lua")));

        manager.InstallUe4ss(Release("UE4SS_v3.0.1_again"));
        manager.SetEnabled(mod.Id, true);
        Assert.True(File.Exists(Path.Combine(Win64, "ue4ss", "Mods", "DashTest", "Scripts", "main.lua")));
    }

    [Fact]
    public void Removing_clears_the_older_ue4ss_2_layout()
    {
        File.WriteAllText(Path.Combine(Win64, "UE4SS.dll"), "");
        File.WriteAllText(Path.Combine(Win64, "UE4SS-settings.ini"), "");
        File.WriteAllText(Path.Combine(Win64, "xinput1_3.dll"), "");
        Directory.CreateDirectory(Path.Combine(Win64, "Mods", "OldMod"));

        manager.RemoveUe4ss();

        Assert.Equal(["TestGame-Win64-Shipping.exe"], Directory.GetFileSystemEntries(Win64).Select(Path.GetFileName));
    }

    [Fact]
    public void Without_ue4ss_nothing_is_removed()
    {
        File.WriteAllText(Path.Combine(Win64, "dwmapi.dll"), "another tool");

        Assert.Throws<ModException>(() => manager.RemoveUe4ss());

        Assert.Equal("another tool", File.ReadAllText(Path.Combine(Win64, "dwmapi.dll")));
    }

    [Fact]
    public void A_file_in_use_stops_the_removal_and_changes_nothing()
    {
        manager.InstallUe4ss(Release("UE4SS_v3.0.1"));
        var dll = Path.Combine(Win64, "dwmapi.dll");

        using (new FileStream(dll, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Throws<ModException>(() => manager.RemoveUe4ss());

        Assert.True(File.Exists(dll));
        Assert.True(File.Exists(Path.Combine(Win64, "ue4ss", "UE4SS.dll")));
        Assert.False(Directory.Exists(Path.Combine(Win64, "ue4ss.uemm-removing")));
    }

    /// <summary>A mod with a pak in ~mods and a Lua part in UE4SS's Mods folder.</summary>
    private ModRecord InstallMixed()
    {
        folder.File(@"Downloads\Mix\Mix_P.pak", "pak");
        folder.File(@"Downloads\Mix\ue4ss\Mods\Mix\Scripts\main.lua");
        using var preview = manager.Inspect(folder.Combine("Downloads", "Mix"));
        return manager.Install(preview, preview.Packages.ToList(), "Mix", null);
    }

    [Fact]
    public void A_locked_pak_of_a_mixed_mod_stops_the_removal_and_changes_nothing()
    {
        manager.InstallUe4ss(Release("UE4SS_v3.0.1"));
        InstallMixed();
        var pak = Path.Combine(game.ModsPath, "Mix_P.pak");

        using (File.Open(pak, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<ModException>(() => manager.RemoveUe4ss());

        Assert.True(File.Exists(Path.Combine(Win64, "ue4ss", "UE4SS.dll")));
        Assert.True(File.Exists(Path.Combine(Win64, "dwmapi.dll")));
        Assert.True(File.Exists(Path.Combine(Win64, "ue4ss", "Mods", "Mix", "Scripts", "main.lua")));
        Assert.True(File.Exists(pak));
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(Assert.Single(manager.Mods)));

        manager.RemoveUe4ss();
        Assert.False(File.Exists(pak));
        Assert.False(Assert.Single(manager.Mods).Enabled);
    }

    [Fact]
    public void A_removal_that_cannot_be_saved_changes_nothing()
    {
        manager.InstallUe4ss(Release("UE4SS_v3.0.1"));
        InstallMixed();
        var list = new AppPaths(folder.Combine("Data")).StateFile(game.Profile.Id);

        using (File.Open(list, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<ModException>(() => manager.RemoveUe4ss());

        Assert.True(File.Exists(Path.Combine(Win64, "ue4ss", "UE4SS.dll")));
        Assert.True(File.Exists(Path.Combine(game.ModsPath, "Mix_P.pak")));
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(Assert.Single(manager.Mods)));
        Assert.Equal("v3.0.1", manager.InstalledUe4ssVersion);
    }

    private sealed class FixedResponse(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8) });
    }
}
