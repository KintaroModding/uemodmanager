using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UEManager.Core.Mods;

/// <summary>A UE4SS release package: its version, where to download it, and its SHA-256.</summary>
public sealed record Ue4ssRelease(string Version, string Url, string? Sha256)
{
    public string FileName => Path.GetFileName(new Uri(Url).AbsolutePath);
}

/// <summary>
/// The metadata manifest that names the current UE4SS release per game, so a new
/// UE4SS build reaches users without a new app build:
/// <code>{ "ue4ss": { "&lt;game id&gt;": { "version": "...", "url": "https://...", "sha256": "..." } } }</code>
/// </summary>
public static partial class Ue4ssManifest
{
    /// <summary>The entry for one game, or null when the manifest has none or it is incomplete.</summary>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    public static Ue4ssRelease? Parse(string json, string gameId)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!document.RootElement.TryGetProperty("ue4ss", out var games)
            || games.ValueKind != JsonValueKind.Object
            || !games.TryGetProperty(gameId, out var entry)
            || entry.ValueKind != JsonValueKind.Object)
            return null;
        var version = Text(entry, "version");
        var url = Text(entry, "url");
        if (string.IsNullOrWhiteSpace(version) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;
        return new Ue4ssRelease(version.Trim(), url!, Text(entry, "sha256"));
    }

    /// <summary>
    /// Fetches the manifest and returns this game's entry. Fresh on every call: a new
    /// UE4SS build may matter. Offline or any other failure returns null quietly.
    /// </summary>
    public static async Task<Ue4ssRelease?> FetchAsync(HttpClient http, string url, string gameId, CancellationToken cancellation = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            using var response = await http.SendAsync(request, cancellation);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warn($"UE4SS manifest '{url}' answered {(int)response.StatusCode}.");
                return null;
            }
            var release = Parse(await response.Content.ReadAsStringAsync(cancellation), gameId);
            Log.Info(release is null
                ? $"UE4SS manifest has no entry for '{gameId}'."
                : $"UE4SS manifest: {gameId} -> {release.Version}.");
            return release;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Log.Warn($"UE4SS manifest '{url}' could not be read: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Whether <paramref name="latest"/> should be offered as an update. An unknown installed
    /// version (UE4SS installed by hand) is offered the latest one too.
    /// </summary>
    public static bool IsUpdate(string? installedVersion, Ue4ssRelease? latest) =>
        latest is not null && !string.Equals(installedVersion?.Trim(), latest.Version, StringComparison.OrdinalIgnoreCase);

    /// <summary>The version in a release file name, such as "v3.0.1-1181-g83330635" in "mortalshell2_ue4ss_v3.0.1-1181-g83330635.zip".</summary>
    public static string? VersionFromFileName(string path)
    {
        // Only an archive extension is cut off: a folder named "ue4ss_v3.0.1-7" has dots in its version.
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        if (FileSystem.HasExtension(name, FileSystem.ArchiveExtensions))
            name = Path.GetFileNameWithoutExtension(name);
        var match = FileVersion().Match(name);
        return match.Success ? match.Value : null;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    [GeneratedRegex(@"v\d+(?:\.\d+)+(?:-[0-9A-Za-z.-]+)?", RegexOptions.IgnoreCase)]
    private static partial Regex FileVersion();
}
