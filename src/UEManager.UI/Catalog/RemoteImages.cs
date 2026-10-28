using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UEManager.Core;

namespace UEManager.UI.Catalog;

/// <summary>
/// Pictures from the internet, such as catalog covers. They are downloaded with the app's own
/// connection and kept in memory only: nothing is written outside the Data folder, as a WPF
/// image with a web address would do through the Windows internet cache.
/// </summary>
internal sealed class RemoteImages(HttpClient http)
{
    /// <summary>Covers are small; a larger file is not a cover.</summary>
    private const long MaxBytes = 10 * 1024 * 1024;

    private readonly Dictionary<string, Task<ImageSource?>> cache = new(StringComparer.Ordinal);

    /// <param name="url">An https address; anything else gives null.</param>
    /// <param name="width">The width to decode to, in pixels, which keeps large pictures small in memory.</param>
    public Task<ImageSource?> LoadAsync(string? url, int width = 480)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return Task.FromResult<ImageSource?>(null);
        if (!cache.TryGetValue(uri.AbsoluteUri, out var image))
            cache[uri.AbsoluteUri] = image = DownloadAsync(uri, width);
        return image;
    }

    private async Task<ImageSource?> DownloadAsync(Uri uri, int width)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes)
            {
                Log.Warn($"Picture '{uri}' was not loaded: {(int)response.StatusCode}, {response.Content.Headers.ContentLength} bytes.");
                return null;
            }
            var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = width;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException
                                       or FileFormatException or IOException or InvalidOperationException)
        {
            Log.Warn($"Picture '{uri}' could not be loaded: {ex.Message}");
            return null;
        }
    }
}
