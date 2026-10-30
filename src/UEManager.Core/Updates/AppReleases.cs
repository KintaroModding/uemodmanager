using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using UEManager.Core.Mods;

namespace UEManager.Core.Updates;

/// <summary>A release file of the app: its download link, size and, when GitHub gives one, its SHA-256.</summary>
public sealed record AppReleaseFile(string Name, string Url, long Size, string? Sha256);

/// <summary>A release of the app on GitHub.</summary>
/// <param name="Version">The version without the tag's "v", such as "1.1.0".</param>
/// <param name="File">The zip of this build for this game, or null when the release has none.</param>
public sealed record AppRelease(string Version, string? PageUrl, DateTimeOffset? Date, string? Notes, AppReleaseFile? File);

/// <summary>
/// Reads the app's newest release from GitHub's API ("releases/latest"). Every release holds one zip
/// per build and game, named "&lt;build&gt;_&lt;game id&gt;_&lt;version&gt;.zip"; the caller names the
/// start of the one it needs.
/// </summary>
public static class AppReleases
{
    /// <summary>Reads GitHub's answer for one release.</summary>
    /// <param name="filePrefix">The start of the zip name of this build for this game, such as "uemm_phantom-blade-zero_".</param>
    /// <param name="allowHttp">Accept http links too; only for a test release list given on the command line.</param>
    /// <exception cref="ModException">The answer names no version.</exception>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    public static AppRelease Parse(string json, string filePrefix, bool allowHttp = false)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tag = Text(root, "tag_name") ?? throw new ModException($"The newest release names no version.");

        AppReleaseFile? file = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = Text(asset, "name");
                var url = Link(asset, "browser_download_url", allowHttp);
                if (name is null || url is null
                    || !name.StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase)
                    || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    continue;
                var size = asset.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out var bytes) ? bytes : 0;
                // GitHub gives "sha256:<hex>".
                var digest = Text(asset, "digest");
                var sha256 = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                    && digest.Length == 71 && digest[7..].All(char.IsAsciiHexDigit)
                    ? digest[7..]
                    : null;
                file = new AppReleaseFile(name, url, size, sha256);
                break;
            }
        }

        var date = Text(root, "published_at") is { } text
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var published)
                ? published
                : (DateTimeOffset?)null;
        return new AppRelease(ModVersion.FromTag(tag), Link(root, "html_url", allowHttp), date, Text(root, "body"), file);
    }

    /// <summary>Fetches the newest release.</summary>
    /// <exception cref="ModException">GitHub could not be reached or gave no usable answer.</exception>
    public static async Task<AppRelease> FetchLatestAsync(HttpClient http, string url, string filePrefix, CancellationToken cancellation = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new ModException($"No release is published yet.");
            if (!response.IsSuccessStatusCode)
                throw new ModException($"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}. Try again later.");
            var release = Parse(await response.Content.ReadAsStringAsync(timeout.Token), filePrefix,
                allowHttp: new Uri(url).Scheme == Uri.UriSchemeHttp);
            Log.Info($"Newest release: {release.Version}; file for this build: {release.File?.Name ?? "none"}.");
            return release;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
        {
            throw new ModException($"The newest release could not be read: {ex.Message}");
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    /// <summary>An absolute https link (http too when allowed), else null: it is downloaded or opened in the browser.</summary>
    private static string? Link(JsonElement element, string name, bool allowHttp) =>
        Text(element, name) is { } text && Uri.TryCreate(text, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || (allowHttp && uri.Scheme == Uri.UriSchemeHttp))
            ? uri.AbsoluteUri
            : null;
}
