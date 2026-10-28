using System.Net;
using System.Text;
using UEManager.Core.Catalog;
using UEManager.Core.Mods;

namespace UEManager.Core.Tests;

public class CatalogTests
{
    private const string Games = """
        {
          "openmods": 1,
          "futureField": { "ignored": true },
          "games": [
            { "slug": "minecraft", "name": "Minecraft", "steamAppId": null, "mods": 1,
              "list": "https://openmods.nexus/games/minecraft/mods.json" },
            { "slug": "mortal-shell-ii", "name": "Mortal Shell II", "steamAppId": "2584270", "mods": 6,
              "list": "https://openmods.nexus/games/mortal-shell-ii/mods.json" },
            { "slug": "broken", "name": "No list" }
          ]
        }
        """;

    private const string List = """
        {
          "openmods": 1,
          "game": { "slug": "mortal-shell-ii", "name": "Mortal Shell II", "steamAppId": "2584270",
                    "art": "https://cdn.example.test/header.jpg", "page": "https://openmods.nexus/games/mortal-shell-ii" },
          "mods": [
            { "name": "Dash Enhanced", "summary": "Longer dashes.", "author": "KintaroModding", "listedBy": null,
              "cover": "https://raw.githubusercontent.com/KintaroModding/mortalshell2_dashenhanced/main/docs/cover.png",
              "tags": ["gameplay"], "license": "MIT",
              "page": "https://openmods.nexus/mods/kintaromodding/mortalshell2_dashenhanced",
              "source": "https://github.com/KintaroModding/mortalshell2_dashenhanced",
              "updated": "2026-10-23T14:11:35Z", "newField": 1,
              "release": { "tag": "v1.0", "date": "2026-10-23T14:11:35Z",
                "latest": "https://github.com/KintaroModding/mortalshell2_dashenhanced/releases/latest",
                "files": [ { "name": "mortalshell2_dashenhanced_1.0.zip", "size": 17239,
                  "url": "https://github.com/KintaroModding/mortalshell2_dashenhanced/releases/download/v1.0/mortalshell2_dashenhanced_1.0.zip",
                  "sha256": "f20f89b8c6f6f2aa737fb54e76e68c818cfacd490fd9c5c712d4c438d09b1ab0" } ] } },
            { "name": "No release", "author": "KintaroModding", "cover": null, "tags": [],
              "page": "file:///C:/Windows/notepad.exe", "source": "https://github.com/KintaroModding/norelease", "release": null },
            { "name": "Empty release", "source": "https://github.com/KintaroModding/empty",
              "release": { "tag": "v1.0", "files": [] } },
            { "name": "UE4SS", "tags": ["tools", "library"], "source": "https://github.com/KintaroModding/ue4ss",
              "release": { "tag": "v3.0.1", "files": [
                { "name": "mortalshell2_ue4ss_v3.0.1.zip", "size": 10, "url": "https://example.test/a.zip" },
                { "name": "expedition33_ue4ss_v3.0.1.zip", "size": 10, "url": "https://example.test/b.zip" },
                { "name": "SHA256SUMS.txt", "size": 10, "url": "https://example.test/sums.txt" } ] } },
            { "name": "Repack", "author": "someone", "listedBy": "KintaroModding", "source": "https://github.com/Someone/Repack.git",
              "release": { "tag": "1.2", "files": [
                { "name": "Repack v1.2.zip", "size": 10, "url": "https://example.test/Repack v1.2.zip", "sha256": "not-a-checksum" },
                { "name": "readme.txt", "size": 1, "url": "https://example.test/readme.txt" } ] } },
            { "summary": "No name, so it is left out", "source": "https://github.com/x/y" }
          ]
        }
        """;

    [Fact]
    public void Finds_the_game_by_its_steam_app_id()
    {
        var games = OpenMods.ParseGames(Games);

        Assert.Equal(["minecraft", "mortal-shell-ii"], games.Select(g => g.Slug));
        var game = OpenMods.FindGame(games, "2584270");
        Assert.Equal("https://openmods.nexus/games/mortal-shell-ii/mods.json", game?.ListUrl);
        Assert.Equal(6, game?.ModCount);
        Assert.Null(OpenMods.FindGame(games, "4115450"));
        Assert.Null(OpenMods.FindGame(games, null));
    }

    [Theory]
    [InlineData("""{ "openmods": 2, "games": [] }""", "newer")]
    [InlineData("""{ "games": [] }""", "not an openmods list")]
    [InlineData("""[ 1, 2 ]""", "not an openmods list")]
    public void Refuses_other_formats(string json, string message)
    {
        var error = Assert.Throws<ModException>(() => OpenMods.ParseGames(json));

        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void Reads_the_mods_of_a_game()
    {
        var list = OpenMods.ParseList(List);

        Assert.Equal("https://cdn.example.test/header.jpg", list.Game.Art);
        Assert.Equal(["Dash Enhanced", "No release", "Empty release", "UE4SS", "Repack"], list.Mods.Select(m => m.Name));

        var dash = list.Mods[0];
        Assert.Equal("kintaromodding/mortalshell2_dashenhanced", dash.Id);
        Assert.Equal("1.0", dash.Release?.Version);
        Assert.Equal(["gameplay"], dash.Tags);
        Assert.Equal(17239, dash.InstallFile?.Size);
        Assert.Equal("f20f89b8c6f6f2aa737fb54e76e68c818cfacd490fd9c5c712d4c438d09b1ab0", dash.InstallFile?.Sha256);
        Assert.False(dash.IsUe4ss);
    }

    [Fact]
    public void Mods_without_a_single_download_get_no_install_file()
    {
        var mods = OpenMods.ParseList(List).Mods;

        Assert.Null(mods[1].Release);
        Assert.Null(mods[1].InstallFile);
        Assert.Null(mods[2].InstallFile);
        Assert.True(mods[3].IsUe4ss);
        Assert.Null(mods[3].InstallFile);
    }

    [Fact]
    public void Only_https_links_are_kept()
    {
        var noRelease = OpenMods.ParseList(List).Mods[1];

        Assert.Null(noRelease.Page);
        Assert.Null(noRelease.Cover);
    }

    [Fact]
    public void Another_authors_project_keeps_its_own_id_and_file()
    {
        var repack = OpenMods.ParseList(List).Mods[4];

        Assert.Equal("someone/repack", repack.Id);
        Assert.Equal("KintaroModding", repack.ListedBy);
        Assert.Equal("1.2", repack.Release?.Version);
        Assert.Equal("Repack v1.2.zip", repack.InstallFile?.Name);
        Assert.Equal("https://example.test/Repack%20v1.2.zip", repack.InstallFile?.Url);
        Assert.Null(repack.InstallFile?.Sha256);
    }

    [Theory]
    [InlineData("https://github.com/KintaroModding/Repo", "kintaromodding/repo")]
    [InlineData("https://github.com/KintaroModding/repo.git", "kintaromodding/repo")]
    [InlineData("https://github.com/KintaroModding/repo/tree/main", "kintaromodding/repo")]
    [InlineData("https://gitlab.com/Someone/Repo/", "https://gitlab.com/someone/repo")]
    public void The_mod_id_comes_from_the_source(string source, string id)
    {
        Assert.Equal(id, OpenMods.IdFromSource(source));
    }

    [Theory]
    [InlineData("1.0", "1.1", true)]
    [InlineData("1.9", "1.10", true)]
    [InlineData("1.10", "1.9", false)]
    [InlineData("1.0", "v1.0", false)]
    [InlineData("1", "1.0", false)]
    [InlineData(null, "1.0", true)]
    [InlineData("beta", "beta", false)]
    [InlineData("beta", "rc", true)]
    public void Offers_only_newer_versions(string? installed, string latest, bool update)
    {
        Assert.Equal(update, ModVersion.IsUpdate(installed, latest));
    }

    [Fact]
    public async Task Fetches_the_list_of_the_game()
    {
        using var http = new HttpClient(new Pages(new()
        {
            ["https://openmods.nexus/games/mods.json"] = Games,
            ["https://openmods.nexus/games/mortal-shell-ii/mods.json"] = List,
        }));

        var list = await OpenMods.FetchAsync(http, "https://openmods.nexus/games/mods.json", "2584270");
        var none = await OpenMods.FetchAsync(http, "https://openmods.nexus/games/mods.json", "4115450");

        Assert.Equal(5, list?.Mods.Count);
        Assert.Null(none);
    }

    [Fact]
    public async Task A_list_that_cannot_be_loaded_is_reported()
    {
        using var http = new HttpClient(new Pages([]));

        var error = await Assert.ThrowsAsync<ModException>(() => OpenMods.FetchAsync(http, "https://openmods.nexus/games/mods.json", "2584270"));

        Assert.Contains("404", error.Message);
    }

    [Fact]
    public async Task A_download_with_another_size_or_checksum_is_refused_and_removed()
    {
        using var folder = new TestFolder();
        using var http = new HttpClient(new Pages(new() { ["https://example.test/mod.zip"] = "zip bytes" }));
        var sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("zip bytes"u8.ToArray()));

        var file = await FileDownloader.DownloadAsync(http, "https://example.test/mod.zip", "mod.zip", 9, sha256, folder.Combine("a"), "Mod");
        var size = await Assert.ThrowsAsync<ModException>(() =>
            FileDownloader.DownloadAsync(http, "https://example.test/mod.zip", "mod.zip", 10, null, folder.Combine("b"), "Mod"));
        var checksum = await Assert.ThrowsAsync<ModException>(() =>
            FileDownloader.DownloadAsync(http, "https://example.test/mod.zip", "mod.zip", 9, new string('0', 64), folder.Combine("c"), "Mod"));

        Assert.Equal("zip bytes", File.ReadAllText(file));
        Assert.Contains("bytes instead of", size.Message);
        Assert.Contains("checksum", checksum.Message);
        Assert.Empty(Directory.GetFiles(folder.Combine("b")));
        Assert.Empty(Directory.GetFiles(folder.Combine("c")));
    }

    /// <summary>Answers known URLs with their text, every other URL with 404.</summary>
    private sealed class Pages(Dictionary<string, string> pages) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(pages.TryGetValue(request.RequestUri!.AbsoluteUri, out var text)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
