using System.Net;
using System.Text;
using System.Text.Json;
using UEManager.Core.Updates;

namespace UEManager.Core.Tests;

/// <summary>The update check: one request for the page version of many mods, and how a version compares with it.</summary>
public class ModSiteVersionsTests
{
    private const string Api = "https://api.example.test/v2/graphql";

    /// <summary>A fake mod site that records each request and answers with the given text.</summary>
    private sealed class Site(Func<int, string> answer, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<(string Body, HttpRequestMessage Request)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((await request.Content!.ReadAsStringAsync(cancellationToken), request));
            return new HttpResponseMessage(status) { Content = new StringContent(answer(Requests.Count), Encoding.UTF8, "application/json") };
        }
    }

    private static string Nodes(params (object Id, string Version, string Status)[] mods) => JsonSerializer.Serialize(new
    {
        data = new { mods = new { nodes = mods.Select(m => new { modId = m.Id, version = m.Version, status = m.Status }) } },
    });

    private static Task<IReadOnlyDictionary<string, ModSitePage>?> FetchPages(Site site, params string[] ids) =>
        ModSiteVersions.FetchAsync(new HttpClient(site), Api, "10614", ids, "1.6.5", TimeSpan.FromSeconds(5));

    /// <summary>The page versions of a check: what these tests look at, as the app did before the files came along.</summary>
    private static async Task<Dictionary<string, string>?> Fetch(Site site, params string[] ids) =>
        (await FetchPages(site, ids))?.ToDictionary(p => p.Key, p => p.Value.Version!);

    [Fact]
    public async Task One_request_asks_for_every_mod_with_the_game_and_names_the_app()
    {
        var site = new Site(_ => Nodes((2, "1.6.4", "published"), (149, "0.9.3", "published")));

        var found = await Fetch(site, "2", "149");

        Assert.Equal(new Dictionary<string, string> { ["2"] = "1.6.4", ["149"] = "0.9.3" }, found);
        var (body, request) = Assert.Single(site.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("UE MM", request.Headers.GetValues("Application-Name").Single());
        Assert.Equal("1.6.5", request.Headers.GetValues("Application-Version").Single());
        // One filter per mod, joined by OR, each with the game: the site's API needs the game with every mod number.
        using var json = JsonDocument.Parse(body);
        var filter = json.RootElement.GetProperty("variables").GetProperty("filter");
        Assert.Equal("OR", filter.GetProperty("op").GetString());
        var parts = filter.GetProperty("filter").EnumerateArray().ToList();
        Assert.Equal(["2", "149"], parts.Select(p => p.GetProperty("modId")[0].GetProperty("value").GetString()));
        Assert.All(parts, p => Assert.Equal("10614", p.GetProperty("gameId")[0].GetProperty("value").GetString()));
        Assert.Equal(2, json.RootElement.GetProperty("variables").GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Many_mods_go_in_parts_of_fifty()
    {
        var site = new Site(_ => Nodes());

        await Fetch(site, Enumerable.Range(1, 120).Select(i => i.ToString()).ToArray());

        Assert.Equal(3, site.Requests.Count);
    }

    [Fact]
    public async Task A_page_that_is_not_published_has_no_version()
    {
        var site = new Site(_ => Nodes((2, "1.6.4", "published"), (7, "2.0", "hidden"), ("8", "1.1", "published")));

        var found = await Fetch(site, "2", "7", "8");

        Assert.Equal(new Dictionary<string, string> { ["2"] = "1.6.4", ["8"] = "1.1" }, found);
    }

    [Theory]
    [InlineData("""{"errors":[{"message":"gameId is required when filtering by modId"}],"data":null}""")]
    [InlineData("""{"data":{"other":1}}""")]
    [InlineData("not json")]
    public async Task An_error_or_an_unknown_answer_is_no_answer(string answer)
    {
        Assert.Null(await Fetch(new Site(_ => answer), "2"));
    }

    [Fact]
    public async Task A_failed_request_is_no_answer()
    {
        Assert.Null(await Fetch(new Site(_ => "{}", HttpStatusCode.TooManyRequests), "2"));
    }

    [Fact]
    public async Task No_mods_send_nothing()
    {
        var site = new Site(_ => Nodes());

        Assert.Empty((await Fetch(site))!);
        Assert.Empty(site.Requests);
    }

    [Theory]
    [InlineData("1.7.2", "1.7.3", ModSiteState.UpdateAvailable)]
    [InlineData("1.7.2", "1.10", ModSiteState.UpdateAvailable)]
    [InlineData("1.6", "v1.6.1", ModSiteState.UpdateAvailable)]
    [InlineData("1.7.2", "1.7.2", ModSiteState.UpToDate)]
    [InlineData("1.6", "1.6.0", ModSiteState.UpToDate)]
    [InlineData("v2.0", "2.0", ModSiteState.UpToDate)]
    // Newer than the page, as the Quest Guide's file number 10 against its page 1.2.4: nothing newer is there.
    [InlineData("10", "1.2.4", ModSiteState.UpToDate)]
    // Text after the numbers: the numbers tell, as PhantomHUD withHP's page 0.5.2.withHP against its file 0.5.1 (#22).
    [InlineData("0.5.1", "0.5.2.withHP", ModSiteState.UpdateAvailable)]
    [InlineData("0.5.3", "0.5.2.withHP", ModSiteState.UpToDate)]
    [InlineData("1.2-beta", "1.3", ModSiteState.UpdateAvailable)]
    [InlineData("v1.1", "1.2a", ModSiteState.UpdateAvailable)]
    // The same numbers: a single letter right after them counts in the alphabet's order, as a fix.
    [InlineData("1.2a", "1.2b", ModSiteState.UpdateAvailable)]
    [InlineData("1.2a", "1.2B", ModSiteState.UpdateAvailable)]
    [InlineData("1.2", "1.2a", ModSiteState.UpdateAvailable)]
    [InlineData("1.2b", "1.2a", ModSiteState.UpToDate)]
    [InlineData("1.2a", "1.2", ModSiteState.UpToDate)]
    [InlineData("1.2.0a", "1.2a", ModSiteState.UpToDate)]
    // The same numbers with any other text: only the same text tells.
    [InlineData("0.5.2.withHP", "0.5.2.withHP", ModSiteState.UpToDate)]
    [InlineData("1.2-beta", "1.2-BETA", ModSiteState.UpToDate)]
    [InlineData("0.5.2", "0.5.2.withHP", ModSiteState.Unknown)]
    [InlineData("1.2-beta", "1.2", ModSiteState.Unknown)]
    [InlineData("1.2-a", "1.2-b", ModSiteState.Unknown)]
    [InlineData("1.2a", "1.2ab", ModSiteState.Unknown)]
    // No number at the start: only the same text tells.
    [InlineData("beta", "beta", ModSiteState.UpToDate)]
    [InlineData("beta", "1.0", ModSiteState.Unknown)]
    [InlineData("1.0", "Final", ModSiteState.Unknown)]
    [InlineData("1.0", "", ModSiteState.Unknown)]
    [InlineData(null, "1.0", ModSiteState.Unknown)]
    public void The_installed_version_compares_with_the_page(string? installed, string page, ModSiteState state)
    {
        Assert.Equal(state, ModSiteVersions.Compare(installed, page));
    }
    /// <summary>An answer with the page versions and the files of each mod, as the site sends them.</summary>
    private static string Answer((int Id, string Version, string Status)[] mods, params (int Id, object[] Files)[] files)
    {
        var data = new Dictionary<string, object>
        {
            ["mods"] = new { nodes = mods.Select(m => new { modId = m.Id, version = m.Version, status = m.Status }) },
        };
        foreach (var (id, list) in files)
            data["f" + id] = list;
        return JsonSerializer.Serialize(new { data });
    }

    private static object File(int fileId, string name, string? version, string category, bool primary = false, long date = 0) =>
        new { fileId, name, version, category, primary = primary ? 1 : 0, date };

    [Fact]
    public async Task The_files_of_a_page_come_with_its_version()
    {
        var site = new Site(_ => Answer(
            [(27, "0.5.2", "published")],
            (27, [File(2127, "PBZ-SpeakerHighlight-PVLW", "0.5.0", "MAIN", primary: true, date: 100),
                  File(3006, "PBZ-SpeakerHighlight-PVLW (Experimental)", "0.5.2", "OPTIONAL", date: 200)])));

        var page = (await FetchPages(site, "27"))!["27"];

        Assert.Equal("0.5.2", page.Version);
        Assert.Equal(2, page.Files.Count);
        Assert.Equal("0.5.0", page.Files[0].Version);
        Assert.True(page.Files[0].Primary);
        Assert.Equal("OPTIONAL", page.Files[1].Category);
    }

    [Fact]
    public async Task A_page_without_files_keeps_its_version()
    {
        var site = new Site(_ => Answer([(2, "1.6.4", "published")]));

        var page = (await FetchPages(site, "2"))!["2"];

        Assert.Equal("1.6.4", page.Version);
        Assert.Empty(page.Files);
    }

    [Fact]
    public async Task The_request_asks_for_the_files_of_each_mod()
    {
        var site = new Site(_ => Answer([(27, "0.5.2", "published"), (70, "1.8.0", "published")], (27, []), (70, [])));

        await FetchPages(site, "27", "70");

        var (body, _) = Assert.Single(site.Requests);
        using var json = JsonDocument.Parse(body);
        var query = json.RootElement.GetProperty("query").GetString()!;
        Assert.Contains("modFiles(modId: 27, gameId: $gameId)", query);
        Assert.Contains("modFiles(modId: 70, gameId: $gameId)", query);
        Assert.Equal("10614", json.RootElement.GetProperty("variables").GetProperty("gameId").GetString());
    }

    private static ModSitePage Page(string? version, params ModSiteFile[] files) => new(version, files);

    private static ModSiteFile SiteFile(int id, string name, string? version, string category, bool primary = false, long date = 0) =>
        new(id, name, version, category, primary, date);

    private const string SpeakerDownload = "PBZ-SpeakerHighlight-PVLW 27 0.5.0 2026-11-06T02-19Z uKnm4GbGl.zip";
    private const string SpeakerOptionalDownload = "PBZ-SpeakerHighlight-PVLW (Experimental) 27 0.5.1 2026-11-08T02-19Z AbCdEfGhI.zip";

    [Fact]
    public void The_main_file_keeps_its_own_version_when_an_optional_file_is_newer()
    {
        var page = Page("0.5.2",
            SiteFile(2127, "PBZ-SpeakerHighlight-PVLW", "0.5.0", "MAIN", primary: true, date: 100),
            SiteFile(3006, "PBZ-SpeakerHighlight-PVLW (Experimental)", "0.5.2", "OPTIONAL", date: 200));

        var check = ModSiteVersions.Check("0.5.0", SpeakerDownload, page);

        Assert.Equal(ModSiteState.UpToDate, check.State);
        Assert.Equal("0.5.0", check.ComparedVersion);
        Assert.Equal(ModSiteMatch.File, check.Match);
    }

    [Fact]
    public void An_optional_file_follows_its_own_variant()
    {
        var page = Page("0.5.2",
            SiteFile(1768, "PBZ-SpeakerHighlight-PVLW", "0.4.0", "MAIN", primary: true, date: 100),
            SiteFile(2890, "PBZ-SpeakerHighlight-PVLW (Experimental)", "0.5.1", "OLD_VERSION", date: 150),
            SiteFile(3006, "PBZ-SpeakerHighlight-PVLW (Experimental)", "0.5.2", "OPTIONAL", date: 200));

        var check = ModSiteVersions.Check("0.5.1", SpeakerOptionalDownload, page);

        Assert.Equal(ModSiteState.UpdateAvailable, check.State);
        Assert.Equal("0.5.2", check.ComparedVersion);
        Assert.Equal(ModSiteMatch.File, check.Match);
    }

    [Fact]
    public void A_file_with_that_version_anywhere_on_the_page_is_up_to_date()
    {
        var page = Page("2.0.0",
            SiteFile(1, "Renamed file", "1.4.2", "MAIN", primary: true, date: 100),
            SiteFile(2, "Other file", "1.6.0", "OPTIONAL", date: 200));

        var check = ModSiteVersions.Check("1.6.0", "Something 27 1.6.0 2026-11-01T00-00Z abcdefghi.zip", page);

        Assert.Equal(ModSiteState.UpToDate, check.State);
        Assert.Equal(ModSiteMatch.SameVersion, check.Match);
    }

    [Fact]
    public void Without_a_matching_name_the_main_file_counts_before_the_page()
    {
        var page = Page("1.1.1",
            SiteFile(1, "PBZ Stutter Fixes 1.1.1", "1.1.1", "MAIN", primary: true, date: 300),
            SiteFile(2, "PBZ Stutter Fixes 1.1.0", "1.1.0", "ARCHIVED", date: 100));

        var check = ModSiteVersions.Check("1.1.0", "PBZ Stutter Fixes 1.1.0 1028 1.1.0 2026-11-11T21-19Z NLbS1fHMm.zip", page);

        Assert.Equal(ModSiteState.UpdateAvailable, check.State);
        Assert.Equal("1.1.1", check.ComparedVersion);
        Assert.Equal(ModSiteMatch.MainFile, check.Match);
    }

    [Fact]
    public void Files_whose_names_carry_the_version_fall_back_to_the_page()
    {
        var page = Page("1.8.6",
            SiteFile(1, "PBZMapMarkers 1.8.6 Fog", "1.8.6", "OPTIONAL", date: 300),
            SiteFile(2, "PBZMapMarkers 1.8.6 Fog Teleport", "1.8.6", "OPTIONAL", date: 400));

        var check = ModSiteVersions.Check("1.8.4", "PBZMapMarkers 1.8.4 Fog Teleport 134 1.8.4 2026-11-13T06-56Z R1oU2qvzl.zip", page);

        Assert.Equal(ModSiteState.UpdateAvailable, check.State);
        Assert.Equal("1.8.6", check.ComparedVersion);
        Assert.Equal(ModSiteMatch.Page, check.Match);
    }

    [Fact]
    public void Without_files_the_page_version_is_compared()
    {
        var check = ModSiteVersions.Check("1.0.1", "Travel Anywhere 61 1.0.1 2026-10-29T13-42Z LbtEo64mS.zip", Page("1.0.2"));

        Assert.Equal(ModSiteState.UpdateAvailable, check.State);
        Assert.Equal(ModSiteMatch.Page, check.Match);
    }

    [Fact]
    public void A_file_number_is_its_own_version()
    {
        var page = Page("1.3.0",
            SiteFile(2920, "PBZQuestGuide 1.2.6", "12", "MAIN", primary: true, date: 200),
            SiteFile(4066, "PBZQuestGuide 1.3.0", "1", "OPTIONAL", date: 300));

        var main = ModSiteVersions.Check("12", "PBZQuestGuide 1.2.6 617 12 2026-11-09T11-19Z iFHyWBeQS.7z", page);
        var optional = ModSiteVersions.Check("1", "PBZQuestGuide 1.3.0 617 1 2026-11-15T08-05Z YQ3oJLMta.zip", page);

        Assert.Equal(ModSiteState.UpToDate, main.State);
        Assert.Equal(ModSiteMatch.File, main.Match);
        Assert.Equal(ModSiteState.UpToDate, optional.State);
        Assert.Equal(ModSiteMatch.File, optional.Match);
    }
}
