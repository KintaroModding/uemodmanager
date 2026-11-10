using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UEManager.Core.Updates;

public enum ModPageUpdateState { Unknown, UpToDate, UpdateAvailable }

public sealed record ModPageUpdateResult(ModPageUpdateState State, string? LatestVersion = null);

/// <summary>Best-effort public Nexus page checks, without an API key. Unknown is not a successful update check.</summary>
/// <remarks>The supplied client must disable automatic redirects: only Nexus mod pages may be requested.</remarks>
public sealed class ModPageUpdates
{
    private const int MaxResponseBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly Regex PageAddress = new(
        @"\Ahttps://(?:www\.)?nexusmods\.com/(?<game>[a-z0-9][a-z0-9_-]*)/mods/(?<id>[0-9]+)/?(?:\?[^#\s\\]*)?(?:#[^\s\\]*)?\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex Tags = new(@"<(?<tag>[a-z][a-z0-9:-]*)\b(?<attributes>[^<>]*)>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex ElementTags = new(@"<(?<close>/)?(?<tag>[a-z][a-z0-9:-]*)\b[^<>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex Attributes = new(
        """(?<name>[^\s=/>]+)\s*=\s*(?:"(?<value>[^"]*)"|'(?<value>[^']*)'|(?<value>[^\s>]+))""",
        RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex Markup = new(@"<[^>]*>", RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex NumericVersion = new(@"\A[vV]?[0-9]+(?:\.[0-9]+)*\z",
        RegexOptions.CultureInvariant, RegexTimeout);

    private readonly HttpClient http;
    private readonly TimeProvider clock;
    private readonly TimeSpan requestTimeout;
    private readonly object gate = new();
    private readonly Dictionary<string, Lazy<Task<CachedVersion>>> cache = new(StringComparer.Ordinal);

    public ModPageUpdates(HttpClient http) : this(http, TimeProvider.System, TimeSpan.FromSeconds(12)) { }

    internal ModPageUpdates(HttpClient http, TimeProvider clock, TimeSpan requestTimeout)
    {
        this.http = http;
        this.clock = clock;
        this.requestTimeout = requestTimeout;
    }

    /// <summary>Only numeric dotted versions are comparable. Cancelling a caller does not cancel a shared request.</summary>
    public async Task<ModPageUpdateResult> CheckAsync(string pageUrl, string? installedVersion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var url = NormalizeUrl(pageUrl);
        if (url is null) return new(ModPageUpdateState.Unknown);

        Lazy<Task<CachedVersion>> entry;
        lock (gate)
        {
            if (!cache.TryGetValue(url, out entry!)
                || (entry.IsValueCreated && entry.Value.IsCompletedSuccessfully
                    && entry.Value.Result.Expires <= clock.GetUtcNow()))
            {
                entry = new Lazy<Task<CachedVersion>>(() => FetchAsync(url), LazyThreadSafetyMode.ExecutionAndPublication);
                cache[url] = entry;
            }
        }
        var remote = await entry.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        var localParts = VersionParts(installedVersion);
        var remoteParts = VersionParts(remote.Version);
        if (localParts is null || remoteParts is null)
            return new(ModPageUpdateState.Unknown, remote.Version);

        for (var i = 0; i < Math.Max(localParts.Length, remoteParts.Length); i++)
        {
            var local = i < localParts.Length ? localParts[i] : "0";
            var latest = i < remoteParts.Length ? remoteParts[i] : "0";
            var comparison = latest.Length.CompareTo(local.Length);
            if (comparison == 0) comparison = string.CompareOrdinal(latest, local);
            if (comparison != 0)
                return new(comparison > 0 ? ModPageUpdateState.UpdateAvailable : ModPageUpdateState.UpToDate, remote.Version);
        }
        return new(ModPageUpdateState.UpToDate, remote.Version);
    }

    private static string? NormalizeUrl(string? url)
    {
        if (string.IsNullOrEmpty(url) || url.Length > 2048 || url.Any(char.IsControl)) return null;
        var match = PageAddress.Match(url);
        if (!match.Success) return null;
        // Drop query/fragment (files tabs, tracking, etc.) and share apex/www cache entries.
        return $"https://www.nexusmods.com/{match.Groups["game"].Value.ToLowerInvariant()}/mods/{match.Groups["id"].Value}";
    }

    private async Task<CachedVersion> FetchAsync(string url)
    {
        string? version = null;
        try
        {
            using var timeout = new CancellationTokenSource(requestTimeout);
            version = await ReadPageAsync(url, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException
            or RegexMatchTimeoutException or JsonException)
        {
            // Blocked pages, timeouts and changed markup are all unknown, and are cached too.
        }
        return new(version, clock.GetUtcNow() + CacheLifetime);
    }

    private async Task<string?> ReadPageAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (http.DefaultRequestHeaders.UserAgent.Count == 0) request.Headers.UserAgent.ParseAdd("UEManager/1.0");
        request.Headers.Accept.ParseAdd("text/html");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode
            || (response.RequestMessage?.RequestUri is { } final && NormalizeUrl(final.AbsoluteUri) != url)
            || response.Content.Headers.ContentLength > MaxResponseBytes) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, MaxResponseBytes + 1 - (int)bytes.Length)),
                cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            bytes.Write(buffer, 0, count);
            if (bytes.Length > MaxResponseBytes) return null;
        }
        return ReadVersion(Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length));
    }

    private static string? ReadVersion(string html)
    {
        var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match tag in Tags.Matches(html))
        {
            var attributes = Attributes.Matches(tag.Groups["attributes"].Value).Cast<Match>()
                .GroupBy(a => a.Groups["name"].Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => WebUtility.HtmlDecode(g.First().Groups["value"].Value), StringComparer.OrdinalIgnoreCase);
            var isFileVersion = (attributes.TryGetValue("id", out var id) && id == "file-version")
                || (attributes.TryGetValue("class", out var classes)
                    && classes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains("file-version"));
            var isJson = tag.Groups["tag"].Value.Equals("script", StringComparison.OrdinalIgnoreCase)
                && attributes.TryGetValue("type", out var type) && type.Equals("application/ld+json", StringComparison.OrdinalIgnoreCase);
            if (!isFileVersion && !isJson) continue;
            var content = ElementContent(html, tag, isJson ? MaxResponseBytes : 4096);
            if (content is null) continue;
            if (isFileVersion) ReadFileVersions(content, versions);
            if (isJson)
            {
                try
                {
                    using var json = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 32 });
                    ReadSoftwareVersions(json.RootElement, versions);
                }
                catch (JsonException) { /* An unrelated broken JSON-LD block need not hide a valid version. */ }
            }
        }
        // Never guess which of several contradictory page versions describes the installed file.
        return versions.Count == 1 ? versions.Single() : null;
    }

    private static string? ElementContent(string html, Match opening, int maxLength)
    {
        var start = opening.Index + opening.Length;
        var depth = 1;
        for (var token = ElementTags.Match(html, start); token.Success && token.Index - start <= maxLength; token = token.NextMatch())
        {
            if (!token.Groups["tag"].Value.Equals(opening.Groups["tag"].Value, StringComparison.OrdinalIgnoreCase)) continue;
            if (token.Groups["close"].Success) depth--;
            else if (!token.Value.EndsWith("/>", StringComparison.Ordinal)) depth++;
            if (depth == 0) return html[start..token.Index];
        }
        return null;
    }

    private static void ReadFileVersions(string content, HashSet<string> versions)
    {
        // Legacy Nexus pages wrap the label and .stat value in the same #file-version element.
        // Only inspect .stat descendants of that element, never unrelated numbers elsewhere on the page.
        var foundStat = false;
        foreach (Match child in Tags.Matches(content))
        {
            var isStat = Attributes.Matches(child.Groups["attributes"].Value).Cast<Match>().Any(a =>
                a.Groups["name"].Value.Equals("class", StringComparison.OrdinalIgnoreCase)
                && WebUtility.HtmlDecode(a.Groups["value"].Value).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains("stat"));
            if (!isStat) continue;
            foundStat = true;
            if (ElementContent(content, child, 512) is { } value)
                AddVersion(versions, WebUtility.HtmlDecode(Markup.Replace(value, "")).Trim());
        }
        if (!foundStat && content.Length <= 512)
        {
            var direct = WebUtility.HtmlDecode(Markup.Replace(content, "")).Trim();
            // A label such as "Version 9.0" is not itself a trustworthy version field.
            if (VersionParts(direct) is not null) AddVersion(versions, direct);
        }
    }

    private static void ReadSoftwareVersions(JsonElement element, HashSet<string> versions)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("softwareVersion") && property.Value.ValueKind == JsonValueKind.String)
                    AddVersion(versions, property.Value.GetString()?.Trim());
                else if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                    ReadSoftwareVersions(property.Value, versions);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) ReadSoftwareVersions(item, versions);
    }

    private static void AddVersion(HashSet<string> versions, string? version)
    {
        if (!string.IsNullOrWhiteSpace(version) && version.Length <= 128) versions.Add(version);
    }

    private static string[]? VersionParts(string? version)
    {
        version = version?.Trim();
        if (string.IsNullOrEmpty(version) || version.Length > 128 || !NumericVersion.IsMatch(version)) return null;
        if (version[0] is 'v' or 'V') version = version[1..];
        return version.Split('.').Select(p => p.TrimStart('0') is { Length: > 0 } digits ? digits : "0").ToArray();
    }

    private sealed record CachedVersion(string? Version, DateTimeOffset Expires);
}
