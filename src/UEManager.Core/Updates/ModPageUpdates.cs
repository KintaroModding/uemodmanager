using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UEManager.Core.Updates;

public enum ModPageUpdateState { Unknown, UpToDate, UpdateAvailable }

public enum ModPageUpdateFailure { None, InvalidApiKey, AccessDenied, RateLimited, Unavailable }

public sealed record ModPageUpdateResult(ModPageUpdateState State, string? LatestVersion = null,
    ModPageUpdateFailure Failure = ModPageUpdateFailure.None);

/// <summary>Official authenticated Nexus API checks, or best-effort public page checks without a key.</summary>
/// <remarks>The supplied client must disable automatic redirects: requests are restricted to Nexus pages or the official API.</remarks>
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
    private readonly bool useApi;
    private string? apiKey; // Assigned once, then released only when this instance is deactivated.
    private readonly object gate = new();
    private readonly CancellationTokenSource deactivation = new();
    private bool deactivated;
    private readonly SemaphoreSlim apiRequests = new(1, 1);
    private readonly Dictionary<string, Lazy<Task<CachedVersion>>> cache = new(StringComparer.Ordinal);
    private bool rejectedApiKey;
    private DateTimeOffset rateLimitedUntil;

    public ModPageUpdates(HttpClient http, string? nexusApiKey = null)
        : this(http, TimeProvider.System, TimeSpan.FromSeconds(12), nexusApiKey) { }

    internal ModPageUpdates(HttpClient http, TimeProvider clock, TimeSpan requestTimeout, string? nexusApiKey = null)
    {
        this.http = http;
        this.clock = clock;
        this.requestTimeout = requestTimeout;
        useApi = nexusApiKey is not null;
        // Reject controls before trimming, so a newline can never turn into an accepted header.
        if (nexusApiKey is { Length: > 0 and <= 4096 } && nexusApiKey.All(c => c >= ' ' && c <= '~'))
            apiKey = string.IsNullOrWhiteSpace(nexusApiKey) ? null : nexusApiKey.Trim();
    }

    /// <summary>Permanently retires this instance, cancels its requests and releases its stored key reference.</summary>
    public void Deactivate()
    {
        lock (gate)
        {
            if (deactivated) return;
            deactivated = true;
            apiKey = null;
            cache.Clear();
        }
        // Cancellation callbacks may need gate. Do not dispose the CTS/semaphore while waiters unwind.
        try { deactivation.Cancel(); }
        catch (AggregateException) { /* Never expose callback exceptions that could contain a request key. */ }
    }

    private bool IsDeactivated()
    {
        lock (gate) return deactivated;
    }

    /// <summary>Only numeric dotted versions are comparable. Cancelling a caller does not cancel a shared request.</summary>
    public async Task<ModPageUpdateResult> CheckAsync(string pageUrl, string? installedVersion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsDeactivated()) return new(ModPageUpdateState.Unknown, Failure: ModPageUpdateFailure.Unavailable);
        var url = NormalizeUrl(pageUrl);
        if (url is null) return new(ModPageUpdateState.Unknown);
        if (useApi)
        {
            var failure = ApiFailure();
            if (failure != ModPageUpdateFailure.None) return new(ModPageUpdateState.Unknown, Failure: failure);
        }

        Lazy<Task<CachedVersion>> entry;
        lock (gate)
        {
            if (deactivated) return new(ModPageUpdateState.Unknown, Failure: ModPageUpdateFailure.Unavailable);
            if (!cache.TryGetValue(url, out entry!)
                || (entry.IsValueCreated && entry.Value.IsCompletedSuccessfully
                    && entry.Value.Result.Expires <= clock.GetUtcNow()))
            {
                entry = new Lazy<Task<CachedVersion>>(() => FetchAsync(url), LazyThreadSafetyMode.ExecutionAndPublication);
                cache[url] = entry;
            }
        }
        var remote = await entry.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (IsDeactivated()) return new(ModPageUpdateState.Unknown, Failure: ModPageUpdateFailure.Unavailable);
        if (remote.Failure != ModPageUpdateFailure.None)
            return new(ModPageUpdateState.Unknown, Failure: remote.Failure);
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

    /// <summary>Validates this instance's key without exposing API response bodies or account details.</summary>
    public async Task<ModPageUpdateFailure> ValidateApiKeyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var failure = ApiFailure();
        if (failure != ModPageUpdateFailure.None) return failure;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deactivation.Token);
        timeout.CancelAfter(requestTimeout);
        try
        {
            var reply = await ReadApiAsync("https://api.nexusmods.com/v1/users/validate.json", true, timeout.Token)
                .WaitAsync(timeout.Token).ConfigureAwait(false);
            return IsDeactivated() ? ModPageUpdateFailure.Unavailable : reply.Failure;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !IsDeactivated())
        {
            // Do not propagate transport exceptions, which can contain sensitive request information.
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception ex) when (IsRequestFailure(ex))
        {
            return ModPageUpdateFailure.Unavailable;
        }
    }

    private ModPageUpdateFailure ApiFailure()
    {
        lock (gate)
        {
            if (deactivated) return ModPageUpdateFailure.Unavailable;
            if (apiKey is null || rejectedApiKey) return ModPageUpdateFailure.InvalidApiKey;
            return rateLimitedUntil > clock.GetUtcNow() ? ModPageUpdateFailure.RateLimited : ModPageUpdateFailure.None;
        }
    }

    private async Task<CachedVersion> FetchAsync(string url)
    {
        string? version = null;
        var failure = ModPageUpdateFailure.None;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(deactivation.Token);
            timeout.CancelAfter(requestTimeout);
            if (useApi)
            {
                var match = PageAddress.Match(url);
                var apiUrl = $"https://api.nexusmods.com/v1/games/{match.Groups["game"].Value}/mods/{match.Groups["id"].Value}.json";
                var reply = await ReadApiAsync(apiUrl, false, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
                version = reply.Version;
                failure = reply.Failure;
            }
            else
                version = await ReadPageAsync(url, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRequestFailure(ex))
        {
            // Never expose transport exceptions or error bodies (which might echo the API key).
            if (useApi) failure = ModPageUpdateFailure.Unavailable;
        }
        if (IsDeactivated()) return new(null, clock.GetUtcNow(), ModPageUpdateFailure.Unavailable);
        var expires = clock.GetUtcNow() + CacheLifetime;
        // A rate-limit response must be retryable when its advertised cooldown ends, not ten minutes later.
        if (failure == ModPageUpdateFailure.RateLimited)
            lock (gate) expires = rateLimitedUntil;
        return new(version, expires, failure);
    }

    private static bool IsRequestFailure(Exception ex) => ex is HttpRequestException or IOException
        or OperationCanceledException or RegexMatchTimeoutException or JsonException or InvalidOperationException;

    private async Task<ApiReply> ReadApiAsync(string url, bool validate, CancellationToken cancellationToken)
    {
        // Serialize API requests so queued mods cannot continue sending after a 401 or 429.
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        await apiRequests.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            request.Headers.Accept.ParseAdd("application/json");
            if (http.DefaultRequestHeaders.UserAgent.Count == 0) request.Headers.UserAgent.ParseAdd("UEManager/1.0");
            Task<HttpResponseMessage> pending;
            lock (gate)
            {
                var failure = ApiFailure();
                if (failure != ModPageUpdateFailure.None) return new(null, failure);
                cancellationToken.ThrowIfCancellationRequested();
                request.Headers.Add("apikey", apiKey!);
                // Linearize dispatch with Deactivate: no queued request can start after retirement.
                pending = http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            using var response = await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (response.RequestMessage?.RequestUri is { } final && final.AbsoluteUri != url)
                return new(null, ModPageUpdateFailure.Unavailable);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                lock (gate) rejectedApiKey = true;
                return new(null, ModPageUpdateFailure.InvalidApiKey);
            }
            if (response.StatusCode == HttpStatusCode.Forbidden) return new(null, ModPageUpdateFailure.AccessDenied);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var now = clock.GetUtcNow();
                var retry = response.Headers.RetryAfter;
                var delay = retry?.Delta ?? (retry?.Date is { } date ? date - now : TimeSpan.FromMinutes(1));
                if (delay <= TimeSpan.Zero) delay = TimeSpan.FromMinutes(1);
                // Saturate extreme Retry-After values without overflowing DateTimeOffset.
                var until = delay >= DateTimeOffset.MaxValue - now ? DateTimeOffset.MaxValue : now + delay;
                lock (gate) rateLimitedUntil = until;
                return new(null, ModPageUpdateFailure.RateLimited);
            }
            if (response.StatusCode != HttpStatusCode.OK) return new(null, ModPageUpdateFailure.Unavailable);
            var body = await ReadBodyAsync(response.Content, cancellationToken).ConfigureAwait(false);
            if (body is null) return new(null, ModPageUpdateFailure.Unavailable);
            using var json = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 32 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new(null, ModPageUpdateFailure.Unavailable);
            if (validate)
                return new(null, root.TryGetProperty("user_id", out var id) && id.ValueKind == JsonValueKind.Number
                    && id.TryGetInt64(out var userId) && userId > 0 ? ModPageUpdateFailure.None : ModPageUpdateFailure.Unavailable);
            if ((root.TryGetProperty("available", out var available) && available.ValueKind != JsonValueKind.True)
                || (root.TryGetProperty("status", out var status)
                    && (status.ValueKind != JsonValueKind.String || status.GetString() != "published"))
                || !root.TryGetProperty("version", out var value) || value.ValueKind != JsonValueKind.String)
                return new(null, ModPageUpdateFailure.Unavailable);
            var version = value.GetString()?.Trim();
            lock (gate)
            {
                if (deactivated || string.IsNullOrEmpty(version) || version.Length > 128 || version.Any(char.IsControl)
                    || version.Contains(apiKey!, StringComparison.Ordinal))
                    return new(null, ModPageUpdateFailure.Unavailable);
                return new(version, ModPageUpdateFailure.None);
            }
        }
        finally
        {
            request.Headers.Remove("apikey");
            apiRequests.Release();
        }
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

        var body = await ReadBodyAsync(response.Content, cancellationToken).ConfigureAwait(false);
        return body is null ? null : ReadVersion(body);
    }

    private static async Task<string?> ReadBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaxResponseBytes) return null;
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
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
        return Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
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

    private sealed record ApiReply(string? Version, ModPageUpdateFailure Failure);
    private sealed record CachedVersion(string? Version, DateTimeOffset Expires, ModPageUpdateFailure Failure);
}
