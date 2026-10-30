using System.Globalization;
using System.Text.Json;
using UEManager.Core.Mods;

namespace UEManager.Core.Catalog;

/// <summary>A game in the openmods games list.</summary>
/// <param name="ListUrl">The URL of the game's own mod list.</param>
public sealed record CatalogGame(string Slug, string Name, string? SteamAppId, int ModCount, string ListUrl);

/// <summary>The game part of a mod list: the game's name and pictures, and its page on openmods.</summary>
/// <param name="Art">A header picture, used as the cover of a mod that has none.</param>
public sealed record CatalogGameInfo(string Name, string? Art, string? Page);

/// <summary>A release file: a direct download link, its size in bytes, and its SHA-256 when the list gives one.</summary>
public sealed record CatalogFile(string Name, long Size, string Url, string? Sha256);

/// <summary>The newest release of a mod.</summary>
/// <param name="LatestUrl">The newest release page on GitHub.</param>
public sealed record CatalogRelease(string Tag, DateTimeOffset? Date, string? LatestUrl, IReadOnlyList<CatalogFile> Files)
{
    /// <summary>The version without the tag's "v": "1.10" for "v1.10".</summary>
    public string Version => ModVersion.FromTag(Tag);
}

/// <summary>One mod in a game's list. Every link is an absolute https URL or null.</summary>
/// <param name="Id">The mod's id for the manager: "owner/repo" of its source, in lower case.</param>
/// <param name="ListedBy">Set when openmods lists another author's project; <paramref name="Author"/> and <paramref name="Source"/> then point to that author.</param>
public sealed record CatalogMod(
    string Id,
    string Name,
    string? Summary,
    string? Author,
    string? ListedBy,
    string? Cover,
    IReadOnlyList<string> Tags,
    string? License,
    string? Page,
    string? Source,
    DateTimeOffset? Updated,
    CatalogRelease? Release)
{
    /// <summary>The file a one-click install uses: the release's only zip, 7z or rar file. Null when there is none or several.</summary>
    public CatalogFile? InstallFile => Release?.Files.Where(f => FileSystem.HasExtension(f.Name, FileSystem.ArchiveExtensions)).ToList() is [var file]
        ? file
        : null;

    /// <summary>UE4SS itself, which the manager installs its own way.</summary>
    public bool IsUe4ss => Id.EndsWith("/ue4ss", StringComparison.OrdinalIgnoreCase) || Name.Equals("UE4SS", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A game's mod list, as recently updated mods first.</summary>
public sealed record CatalogList(CatalogGameInfo Game, IReadOnlyList<CatalogMod> Mods);

/// <summary>
/// Reads the read-only mod lists of openmods (https://openmods.nexus): a games list, and a list of
/// mods per game. openmods only lists the mods; the files come straight from GitHub releases.
/// </summary>
public static class OpenMods
{
    /// <summary>The list format this app reads. Within a format openmods only adds fields; a breaking change gets a new number.</summary>
    public const int Format = 1;

    /// <summary>The games list. A game is found in it by its Steam app id, so no game profile names it.</summary>
    public const string GamesListUrl = "https://openmods.nexus/games/mods.json";

    /// <summary>openmods refreshes its lists about every 10 minutes and asks managers not to read them more often.</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(10);

    /// <summary>Reads the games list. Entries without a slug, a name or a list URL are left out.</summary>
    /// <exception cref="ModException">The text is not an openmods list of a known format.</exception>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    public static IReadOnlyList<CatalogGame> ParseGames(string json)
    {
        using var document = Open(json);
        var games = new List<CatalogGame>();
        foreach (var game in Items(document.RootElement, "games"))
        {
            var slug = Text(game, "slug");
            var name = Text(game, "name");
            var list = Link(game, "list");
            if (slug is null || name is null || list is null)
                continue;
            var count = game.TryGetProperty("mods", out var mods) && mods.ValueKind == JsonValueKind.Number && mods.TryGetInt32(out var n) ? n : 0;
            games.Add(new CatalogGame(slug, name, Text(game, "steamAppId"), count, list));
        }
        return games;
    }

    /// <summary>The game with this Steam app id, or null.</summary>
    public static CatalogGame? FindGame(IEnumerable<CatalogGame> games, string? steamAppId) =>
        string.IsNullOrWhiteSpace(steamAppId)
            ? null
            : games.FirstOrDefault(g => string.Equals(g.SteamAppId, steamAppId.Trim(), StringComparison.Ordinal));

    /// <summary>Reads a game's mod list. Mods without a name or a source are left out.</summary>
    /// <exception cref="ModException">The text is not an openmods list of a known format.</exception>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    public static CatalogList ParseList(string json)
    {
        using var document = Open(json);
        var root = document.RootElement;
        var game = root.TryGetProperty("game", out var g) && g.ValueKind == JsonValueKind.Object ? g : default;
        var info = game.ValueKind == JsonValueKind.Object
            ? new CatalogGameInfo(Text(game, "name") ?? "", Link(game, "art"), Link(game, "page"))
            : new CatalogGameInfo("", null, null);

        var mods = new List<CatalogMod>();
        foreach (var mod in Items(root, "mods"))
        {
            var name = Text(mod, "name");
            var source = Link(mod, "source");
            if (name is null || source is null)
                continue;
            mods.Add(new CatalogMod(
                IdFromSource(source),
                name,
                Text(mod, "summary"),
                Text(mod, "author"),
                Text(mod, "listedBy"),
                Link(mod, "cover"),
                Items(mod, "tags").Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString()!).ToList(),
                Text(mod, "license"),
                Link(mod, "page"),
                source,
                Date(mod, "updated"),
                ReadRelease(mod)));
        }
        return new CatalogList(info, mods);
    }

    /// <summary>
    /// Reads the games list, finds the game by its Steam app id, and reads its mod list.
    /// Returns null when openmods lists no mods for the game.
    /// </summary>
    /// <exception cref="ModException">A list could not be loaded or read.</exception>
    public static async Task<CatalogList?> FetchAsync(HttpClient http, string gamesUrl, string? steamAppId, CancellationToken cancellation = default)
    {
        try
        {
            var game = FindGame(ParseGames(await GetAsync(http, gamesUrl, cancellation)), steamAppId);
            if (game is null)
            {
                Log.Info($"The openmods list '{gamesUrl}' has no mods for Steam app {steamAppId}.");
                return null;
            }
            var list = ParseList(await GetAsync(http, game.ListUrl, cancellation));
            Log.Info($"Catalog: {list.Mods.Count} mod(s) for '{game.Slug}' from '{game.ListUrl}'.");
            return list;
        }
        catch (JsonException ex)
        {
            throw ModException.Localized($"The openmods list could not be read: {ex.Message}");
        }
    }

    /// <summary>"owner/repo" in lower case for a GitHub source; otherwise the source URL itself, in lower case.</summary>
    public static string IdFromSource(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
                return $"{parts[0]}/{repo}".ToLowerInvariant();
            }
        }
        return source.Trim().TrimEnd('/').ToLowerInvariant();
    }

    private static async Task<string> GetAsync(HttpClient http, string url, CancellationToken cancellation)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var response = await http.GetAsync(url, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw ModException.Localized($"The openmods list could not be loaded: {url} answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            return await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            throw ModException.Localized($"The openmods list could not be loaded: {ex.Message}");
        }
    }

    /// <summary>Parses a list and checks its format number.</summary>
    private static JsonDocument Open(string json)
    {
        var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
        var root = document.RootElement;
        var format = root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("openmods", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : 0;
        if (format == Format)
            return document;
        document.Dispose();
        throw format > Format
            ? ModException.Localized($"openmods uses a newer list format ({format}). Update the app to read it.")
            : new ModException("This is not an openmods list.");
    }

    private static CatalogRelease? ReadRelease(JsonElement mod)
    {
        if (!mod.TryGetProperty("release", out var release) || release.ValueKind != JsonValueKind.Object || Text(release, "tag") is not { } tag)
            return null;
        var files = new List<CatalogFile>();
        foreach (var file in Items(release, "files"))
        {
            var name = Text(file, "name");
            var url = Link(file, "url");
            if (name is null || url is null)
                continue;
            var size = file.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out var bytes) ? bytes : 0;
            var sha256 = Text(file, "sha256") is { Length: 64 } hash && hash.All(char.IsAsciiHexDigit) ? hash : null;
            files.Add(new CatalogFile(name, size, url, sha256));
        }
        return new CatalogRelease(tag, Date(release, "date"), Link(release, "latest"), files);
    }

    private static IEnumerable<JsonElement> Items(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray()
            : [];

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    /// <summary>
    /// An absolute https URL, else null. The links of a list are opened in the browser or downloaded,
    /// so nothing else, such as a file path or another protocol, is let through.
    /// </summary>
    private static string? Link(JsonElement element, string name) =>
        Text(element, name) is { } text && Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri.AbsoluteUri
            : null;

    private static DateTimeOffset? Date(JsonElement element, string name) =>
        Text(element, name) is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;
}
