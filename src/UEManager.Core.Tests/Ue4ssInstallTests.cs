using System.IO.Compression;
using System.Net;
using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

public class Ue4ssInstallTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly GameInstall game;
    private readonly ModManager manager;

    public Ue4ssInstallTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")));
    }

    public void Dispose() => folder.Dispose();

    private string Win64 => game.BinariesPath;

    /// <summary>A release package laid out like the UE4SS 3.x zip: dwmapi.dll and the ue4ss folder.</summary>
    private string Release(string name = "UE4SS_v3", string version = "new")
    {
        folder.File($@"{name}\dwmapi.dll", "proxy " + version);
        folder.File($@"{name}\ue4ss\UE4SS.dll", "runtime " + version);
        folder.File($@"{name}\ue4ss\UE4SS-settings.ini", "settings " + version);
        folder.File($@"{name}\ue4ss\Mods\mods.txt", "BPModLoaderMod : 1");
        folder.File($@"{name}\ue4ss\Mods\BPModLoaderMod\Scripts\main.lua", "loader " + version);
        folder.File($@"{name}\ue4ss\Mods\BPModLoaderMod\load_order.txt", "");
        return folder.Combine(name);
    }

    [Fact]
    public void Installs_the_release_into_win64()
    {
        var result = manager.InstallUe4ss(Release());

        Assert.False(result.Updated);
        Assert.Equal(6, result.FilesWritten);
        Assert.True(File.Exists(Path.Combine(Win64, "dwmapi.dll")));
        Assert.True(File.Exists(Path.Combine(Win64, "ue4ss", "Mods", "BPModLoaderMod", "Scripts", "main.lua")));
        Assert.True(Ue4ssRuntime.Detect(game).Installed);
    }

    [Fact]
    public void A_zipped_release_with_a_top_folder_installs_too()
    {
        var zip = folder.Combine("UE4SS_v3.zip");
        ZipFile.CreateFromDirectory(Release(), zip, CompressionLevel.Fastest, includeBaseDirectory: true);

        manager.InstallUe4ss(zip);

        Assert.True(File.Exists(Path.Combine(Win64, "ue4ss", "UE4SS.dll")));
    }

    [Fact]
    public void An_update_keeps_the_users_settings()
    {
        manager.InstallUe4ss(Release("Old", "old"));
        File.WriteAllText(Path.Combine(Win64, "ue4ss", "UE4SS-settings.ini"), "my settings");
        File.WriteAllText(Path.Combine(Win64, "ue4ss", "Mods", "BPModLoaderMod", "load_order.txt"), "MyBlueprintMod");

        var result = manager.InstallUe4ss(Release("New", "new"));

        Assert.True(result.Updated);
        Assert.Equal("my settings", File.ReadAllText(Path.Combine(Win64, "ue4ss", "UE4SS-settings.ini")));
        Assert.Equal("MyBlueprintMod", File.ReadAllText(Path.Combine(Win64, "ue4ss", "Mods", "BPModLoaderMod", "load_order.txt")));
        Assert.Equal("BPModLoaderMod : 1", File.ReadAllText(Path.Combine(Win64, "ue4ss", "Mods", "mods.txt")));
        Assert.Equal("runtime new", File.ReadAllText(Path.Combine(Win64, "ue4ss", "UE4SS.dll")));
        Assert.Contains("ue4ss/UE4SS-settings.ini", result.KeptFiles);
    }

    [Fact]
    public void A_proxy_dll_from_another_tool_stops_the_install()
    {
        File.WriteAllText(Path.Combine(Win64, "dwmapi.dll"), "someone else's");

        Assert.Throws<ModException>(() => manager.InstallUe4ss(Release()));

        Assert.Equal("someone else's", File.ReadAllText(Path.Combine(Win64, "dwmapi.dll")));
        Assert.False(Directory.Exists(Path.Combine(Win64, "ue4ss")));
    }

    [Fact]
    public void Without_the_games_exe_ue4ss_is_not_installed()
    {
        File.Delete(game.FindShippingExecutable()!);

        Assert.Throws<ModException>(() => manager.InstallUe4ss(Release()));
        Assert.False(File.Exists(Path.Combine(Win64, "dwmapi.dll")));
    }

    [Fact]
    public void A_package_without_ue4ss_is_refused()
    {
        folder.File(@"NotUe4ss\dwmapi.dll");

        Assert.Throws<ModException>(() => manager.InstallUe4ss(folder.Combine("NotUe4ss")));
    }

    [Fact]
    public void Files_of_managed_mods_are_never_overwritten()
    {
        manager.InstallUe4ss(Release("Old", "old"));
        folder.File(@"Downloads\BPModLoaderMod\Scripts\main.lua", "managed copy");
        using (var preview = manager.Inspect(folder.Combine("Downloads")))
        {
            Assert.Throws<ModException>(() => manager.Install(preview, preview.Packages.ToList(), "Loader", null));
        }
        File.Delete(Path.Combine(Win64, "ue4ss", "Mods", "BPModLoaderMod", "Scripts", "main.lua"));
        using (var preview = manager.Inspect(folder.Combine("Downloads")))
            manager.Install(preview, preview.Packages.ToList(), "Loader", null);

        var result = manager.InstallUe4ss(Release("New", "new"));

        Assert.Equal("managed copy", File.ReadAllText(Path.Combine(Win64, "ue4ss", "Mods", "BPModLoaderMod", "Scripts", "main.lua")));
        Assert.Contains("ue4ss/Mods/BPModLoaderMod/Scripts/main.lua", result.KeptFiles);
    }

    [Fact]
    public async Task Downloads_the_release_file()
    {
        var bytes = "zip bytes"u8.ToArray();
        using var http = new HttpClient(new FixedResponse(HttpStatusCode.OK, bytes));

        var file = await Ue4ssDownloader.DownloadAsync(http, "https://example.test/releases/download/v1/UE4SS_v3.zip",
            Sha256(bytes), folder.Combine("Download"));

        Assert.Equal("UE4SS_v3.zip", Path.GetFileName(file));
        Assert.Equal(bytes, File.ReadAllBytes(file));
    }

    [Fact]
    public async Task A_download_with_another_checksum_is_refused_and_removed()
    {
        using var http = new HttpClient(new FixedResponse(HttpStatusCode.OK, "changed"u8.ToArray()));

        await Assert.ThrowsAsync<ModException>(() => Ue4ssDownloader.DownloadAsync(http,
            "https://example.test/UE4SS_v3.zip", Sha256("original"u8.ToArray()), folder.Combine("Download")));

        Assert.Empty(Directory.GetFiles(folder.Combine("Download")));
    }

    [Fact]
    public async Task A_failed_download_is_reported()
    {
        using var http = new HttpClient(new FixedResponse(HttpStatusCode.NotFound, []));

        var error = await Assert.ThrowsAsync<ModException>(() => Ue4ssDownloader.DownloadAsync(http,
            "https://example.test/UE4SS_v3.zip", null, folder.Combine("Download")));

        Assert.Contains("404", error.Message);
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));

    private sealed class FixedResponse(HttpStatusCode status, byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
    }
}
