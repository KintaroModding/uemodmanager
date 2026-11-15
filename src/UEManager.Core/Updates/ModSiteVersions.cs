using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using UEManager.Core.Mods;

namespace UEManager.Core.Updates;

/// <summary>The update state of a mod with a mod page, from the versions of the files on its page.</summary>
public enum ModSiteState
{
    /// <summary>Not checked, or the versions cannot be compared: the box stays empty.</summary>
    Unknown,
    UpToDate,
    UpdateAvailable,
}

/// <summary>
/// Which version a check compared the installed one with: the file the download's name points at, any file with that
/// same version, the page's main file, or else the version the author set on the page.
/// </summary>
public enum ModSiteMatch
{
    File,
    SameVersion,
    MainFile,
    Page,
}

/// <summary>
/// One file on a mod page: <paramref name="Category"/> is MAIN, UPDATE, OPTIONAL, OLD_VERSION, MISCELLANEOUS, REMOVED
/// or ARCHIVED as the site names them, and <paramref name="Date"/> is when it was uploaded, in seconds since 1970.
/// </summary>
public sealed record ModSiteFile(int FileId, string Name, string? Version, string Category, bool Primary, long Date);

/// <summary>
/// A mod page for the update check: the version the author set on the page, and the files it holds. The files are empty
/// when the answer did not carry them, such as an older site or a mod whose files are hidden.
/// </summary>
public sealed record ModSitePage(string? Version, IReadOnlyList<ModSiteFile> Files);

/// <summary>The result of one mod's check: the state, the version it compared with, and how that version was found.</summary>
public sealed record ModSiteCheck(ModSiteState State, string? ComparedVersion, ModSiteMatch Match);

/// <summary>
/// The versions of the files of each installed mod with a mod page, read with one request for all of them from the
/// site's public API. It sends only the game's number and the mods' numbers, and needs no account. The app only shows
/// the result; the player downloads a newer version on the mod page.
/// </summary>
public static partial class ModSiteVersions
{
    /// <summary>The most mods one request asks for; more go in further requests, one after another.</summary>
    public const int PerRequest = 50;

    /// <summary>An answer larger than this is no answer to our question.</summary>
    private const int MaxAnswerBytes = 4 * 1024 * 1024;

    private const string QueryPrefix =
        "query($filter: ModsFilter, $count: Int, $gameId: ID!) { mods(filter: $filter, count: $count) { nodes { modId version status } }";

    private const string FileFields = "fileId name version category primary date";

    /// <summary>The categories that are no file of the page any more: replaced, removed, or taken down.</summary>
    private static readonly string[] PastCategories = ["OLD_VERSION", "REMOVED", "ARCHIVED"];

    /// <summary>
    /// The versions per mod number: the page's version and its files, for the mods whose page is published. Null when a
    /// request failed: then the app knows nothing new, and says so in the log only.
    /// </summary>
    /// <param name="apiUrl">The site's API address, from the game profile.</param>
    /// <param name="gameId">The game's number on the site, from the game profile.</param>
    /// <param name="appVersion">Sent as Application-Version, with Application-Name "UE MM", as the site's rules ask.</param>
    public static async Task<IReadOnlyDictionary<string, ModSitePage>?> FetchAsync(HttpClient http, string apiUrl, string gameId,
        IReadOnlyCollection<string> modIds, string appVersion, TimeSpan timeout, CancellationToken cancellation = default)
    {
        var found = new Dictionary<string, ModSitePage>(StringComparer.Ordinal);
        foreach (var part in modIds.Distinct(StringComparer.Ordinal).Chunk(PerRequest))
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            limit.CancelAfter(timeout);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl)
                {
                    Content = new StringContent(Body(gameId, part), Encoding.UTF8, "application/json"),
                };
                request.Headers.Add("Application-Name", "UE MM");
                request.Headers.Add("Application-Version", appVersion);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Warn($"The mod site's API answered {(int)response.StatusCode} to the update check.");
                    return null;
                }
                if (response.Content.Headers.ContentLength > MaxAnswerBytes)
                {
                    Log.Warn("The mod site's API sent too large an answer to the update check.");
                    return null;
                }
                var answer = await ReadAsync(response.Content, limit.Token);
                if (answer is null || !Read(answer, part, found))
                    return null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException or InvalidOperationException)
            {
                Log.Warn($"The update check could not reach the mod site's API: {ex.Message}");
                return null;
            }
        }
        Log.Info($"Update check: the mod site has the pages of {found.Count} of {modIds.Count} mod(s).");
        return found;
    }

    /// <summary>
    /// The request: one filter per mod for the page version, joined by OR as the site's API needs the game with every
    /// mod number, and one alias per mod for its files. A mod number that is not a number, or one that a further request
    /// already asked for, gets no filter of its own.
    /// </summary>
    internal static string Body(string gameId, IReadOnlyCollection<string> modIds)
    {
        var ids = modIds.Where(IsNumber).Distinct(StringComparer.Ordinal).ToList();
        var filters = new JsonArray();
        foreach (var id in ids)
        {
            filters.Add(new JsonObject
            {
                ["gameId"] = new JsonArray(new JsonObject { ["value"] = gameId, ["op"] = "EQUALS" }),
                ["modId"] = new JsonArray(new JsonObject { ["value"] = id, ["op"] = "EQUALS" }),
            });
        }
        var query = new StringBuilder(QueryPrefix);
        foreach (var id in ids)
            query.Append(", f").Append(id).Append(": modFiles(modId: ").Append(id).Append(", gameId: $gameId) { ")
                .Append(FileFields).Append(" }");
        query.Append(" }");
        return new JsonObject
        {
            ["query"] = query.ToString(),
            ["variables"] = new JsonObject
            {
                ["gameId"] = gameId,
                ["count"] = ids.Count,
                ["filter"] = new JsonObject { ["op"] = "OR", ["filter"] = filters },
            },
        }.ToJsonString();
    }

    /// <summary>The mod numbers are numbers: the API takes them as such, and a name that is not one cannot be asked for.</summary>
    private static bool IsNumber(string id) => id.Length is > 0 and <= 18 && id.All(char.IsAsciiDigit);

    private static async Task<string?> ReadAsync(HttpContent content, CancellationToken cancellation)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellation);
        using var bytes = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellation)) > 0)
        {
            bytes.Write(buffer, 0, count);
            if (bytes.Length > MaxAnswerBytes)
            {
                Log.Warn("The mod site's API sent too large an answer to the update check.");
                return null;
            }
        }
        return Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
    }

    /// <summary>
    /// Adds the page versions and files of an answer; false when it is an error, as the API reports one beside no data.
    /// A mod whose page is not published is left out, as before; a mod whose files the answer does not carry keeps its
    /// page version alone, and the check falls back to it.
    /// </summary>
    internal static bool Read(string answer, IReadOnlyCollection<string> asked, Dictionary<string, ModSitePage> found)
    {
        using var json = JsonDocument.Parse(answer, new JsonDocumentOptions { MaxDepth = 24 });
        var root = json.RootElement;
        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            Log.Warn($"The mod site's API reported an error to the update check: {Shorten(errors.GetRawText())}");
            return false;
        }
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("mods", out var mods) || mods.ValueKind != JsonValueKind.Object
            || !mods.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            Log.Warn("The mod site's API sent an answer the update check does not know.");
            return false;
        }
        var pages = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in nodes.EnumerateArray())
        {
            // A hidden or removed page has no version to offer.
            if (node.ValueKind != JsonValueKind.Object
                || (node.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String && status.GetString() != "published"))
                continue;
            var id = IdOf(node);
            var version = node.TryGetProperty("version", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(version) && version.Length <= 64)
                pages[id] = version;
        }
        foreach (var id in asked.Where(IsNumber))
        {
            if (!pages.TryGetValue(id, out var version))
                continue;
            found[id] = new ModSitePage(version, FilesOf(data, id));
        }
        return true;
    }

    private static string? IdOf(JsonElement node) => node.TryGetProperty("modId", out var modId) ? modId.ValueKind switch
    {
        JsonValueKind.Number when modId.TryGetInt64(out var number) => number.ToString(CultureInfo.InvariantCulture),
        JsonValueKind.String => modId.GetString(),
        _ => null,
    } : null;

    /// <summary>The files an answer carries for one mod, in the order the site lists them; empty without them.</summary>
    private static List<ModSiteFile> FilesOf(JsonElement data, string id)
    {
        var files = new List<ModSiteFile>();
        if (!data.TryGetProperty("f" + id, out var list) || list.ValueKind != JsonValueKind.Array)
            return files;
        foreach (var file in list.EnumerateArray())
        {
            if (file.ValueKind != JsonValueKind.Object)
                continue;
            var fileId = file.TryGetProperty("fileId", out var fileIdValue) && fileIdValue.TryGetInt32(out var number) ? number : 0;
            var name = file.TryGetProperty("name", out var nameValue) && nameValue.ValueKind == JsonValueKind.String ? nameValue.GetString()?.Trim() : null;
            if (string.IsNullOrEmpty(name))
                continue;
            var version = file.TryGetProperty("version", out var versionValue) && versionValue.ValueKind == JsonValueKind.String
                ? versionValue.GetString()?.Trim() : null;
            var category = file.TryGetProperty("category", out var categoryValue) && categoryValue.ValueKind == JsonValueKind.String
                ? categoryValue.GetString()?.Trim().ToUpperInvariant() : null;
            var primary = file.TryGetProperty("primary", out var primaryValue) && primaryValue.ValueKind == JsonValueKind.Number
                && primaryValue.TryGetInt32(out var primaryNumber) && primaryNumber != 0;
            var date = file.TryGetProperty("date", out var dateValue) && dateValue.ValueKind == JsonValueKind.Number
                && dateValue.TryGetInt64(out var dateNumber) ? dateNumber : 0;
            files.Add(new ModSiteFile(fileId, name, version, category ?? "", primary, date));
        }
        return files;
    }

    /// <summary>
    /// The installed version against the version of the file the player has, as far as the page tells it: the file whose
    /// name the download carries; else any current file with that same version; else the page's main file; else the
    /// version the author set on the page. A file the page no longer lists is no current file: its variant's newest file
    /// counts, so an optional file keeps its own version apart from the main one.
    /// </summary>
    public static ModSiteCheck Check(string? installed, string? sourceFileName, ModSitePage page)
    {
        if (string.IsNullOrWhiteSpace(installed))
            return new ModSiteCheck(ModSiteState.Unknown, null, ModSiteMatch.Page);
        var current = page.Files.Where(f => !PastCategories.Contains(f.Category, StringComparer.OrdinalIgnoreCase)).ToList();
        if (current.Count == 0)
            return Verdict(installed, page.Version, ModSiteMatch.Page);
        if (ModNaming.SiteFileName(sourceFileName) is { } name)
        {
            var sameName = current.Where(f => SameName(f.Name, name)).ToList();
            if (sameName.Count > 0)
            {
                // The newest upload of this file's variant: an author who replaces a file gives it a new entry.
                var newest = sameName.OrderByDescending(f => f.Date).ThenByDescending(f => f.FileId).First();
                return Verdict(installed, newest.Version, ModSiteMatch.File);
            }
        }
        if (current.FirstOrDefault(f => f.Version is { Length: > 0 } version && SameVersion(version, installed)) is { } exact)
            return new ModSiteCheck(ModSiteState.UpToDate, exact.Version!.Trim(), ModSiteMatch.SameVersion);
        if ((current.FirstOrDefault(f => f.Primary) ?? current.FirstOrDefault(f => f.Category == "MAIN")) is { } main)
            return Verdict(installed, main.Version, ModSiteMatch.MainFile);
        return Verdict(installed, page.Version, ModSiteMatch.Page);
    }

    private static ModSiteCheck Verdict(string installed, string? compared, ModSiteMatch match) =>
        new(Compare(installed, compared), compared, match);

    private static bool SameName(string a, string b) =>
        string.Equals(Whitespace().Replace(a, " ").Trim(), Whitespace().Replace(b, " ").Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameVersion(string a, string b) =>
        string.Equals(ModVersion.FromTag(a.Trim()), ModVersion.FromTag(b.Trim()), StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// The installed version against the page's: newer on the page is an update; the same, or older, is up to date, as
    /// nothing newer is there. A version with text after its numbers, such as "0.5.2.withHP" or "1.2-beta", compares by
    /// those numbers. With the same numbers, a second pass reads a single letter right after them in the alphabet's
    /// order, as a fix: "1.2b" after "1.2a", and "1.2a" after "1.2". Any other text tells only when it is the same, as
    /// "1.2-beta" comes before "1.2" and "withHP" is a kind, not an order. A version that does not start with a number
    /// counts only when it is the same text.
    /// </summary>
    public static ModSiteState Compare(string? installed, string? page)
    {
        if (string.IsNullOrWhiteSpace(installed) || string.IsNullOrWhiteSpace(page))
            return ModSiteState.Unknown;
        var mine = ModVersion.FromTag(installed);
        var theirs = ModVersion.FromTag(page);
        if (string.Equals(mine, theirs, StringComparison.OrdinalIgnoreCase))
            return ModSiteState.UpToDate;
        var myNumbers = LeadingNumbers().Match(mine);
        var theirNumbers = LeadingNumbers().Match(theirs);
        if (!myNumbers.Success || !theirNumbers.Success || ModVersion.CompareNumbers(myNumbers.Value, theirNumbers.Value) is not { } order)
            return ModSiteState.Unknown;
        if (order != 0)
            return order < 0 ? ModSiteState.UpdateAvailable : ModSiteState.UpToDate;
        var myRest = mine[myNumbers.Length..];
        var theirRest = theirs[theirNumbers.Length..];
        if (!IsFixLetter(myRest) || !IsFixLetter(theirRest))
            return ModSiteState.Unknown;
        return string.Compare(myRest, theirRest, StringComparison.OrdinalIgnoreCase) < 0 ? ModSiteState.UpdateAvailable : ModSiteState.UpToDate;
    }

    /// <summary>What may follow a version's numbers for the second pass: nothing, or one letter, such as the "b" of "1.2b".</summary>
    private static bool IsFixLetter(string rest) => rest.Length == 0 || (rest.Length == 1 && char.IsAsciiLetter(rest[0]));

    /// <summary>The dotted number a version starts with, such as "0.5.2" in "0.5.2.withHP".</summary>
    [GeneratedRegex(@"^[0-9]+(?:\.[0-9]+)*")]
    private static partial Regex LeadingNumbers();

    private static string Shorten(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
