using UEManager.Core.Mods;

namespace UEManager.Core;

/// <summary>Downloads a file the user asked for, and checks it before anything uses it.</summary>
public static class FileDownloader
{
    /// <summary>A download that receives no data for this long stops, so a stalled server never keeps the app busy.</summary>
    public static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Downloads <paramref name="url"/> into <paramref name="folder"/> as <paramref name="fileName"/> and
    /// returns its path. A file whose size or SHA-256 differs from the expected one is deleted and refused,
    /// and so is the part of a download that failed or stalled.
    /// </summary>
    /// <param name="expectedSize">The size in bytes, or null (or 0) when it is not known.</param>
    /// <param name="what">What is downloaded, for messages: "UE4SS", or a mod's name.</param>
    /// <param name="stallTimeout">How long the download may receive no data; <see cref="DefaultStallTimeout"/> when null.</param>
    public static async Task<string> DownloadAsync(HttpClient http, string url, string fileName, long? expectedSize,
        string? expectedSha256, string folder, string what, IProgress<double>? progress = null, CancellationToken cancellation = default,
        TimeSpan? stallTimeout = null)
    {
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, FileSystem.SafeName(Path.GetFileName(fileName), "download"));
        var limit = stallTimeout ?? DefaultStallTimeout;
        // HttpClient's own timeout ends with the headers; this one also covers every read of the body.
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        try
        {
            stall.CancelAfter(limit);
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stall.Token);
            if (!response.IsSuccessStatusCode)
                throw new ModException($"{what} could not be downloaded: the server answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            var total = response.Content.Headers.ContentLength ?? expectedSize;
            await using (var input = await response.Content.ReadAsStreamAsync(stall.Token))
            await using (var output = File.Create(target))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                stall.CancelAfter(limit);
                while ((read = await input.ReadAsync(buffer, stall.Token)) > 0)
                {
                    stall.CancelAfter(limit);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
                    done += read;
                    if (total > 0)
                        progress?.Report(Math.Min(1, (double)done / total.Value));
                }
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            FileSystem.TryDeleteFile(target);
            throw new ModException($"{what} could not be downloaded: no data arrived for {limit.TotalSeconds:0} seconds. "
                + "Check the connection, then try again.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            FileSystem.TryDeleteFile(target);
            throw new ModException($"{what} could not be downloaded: {ex.Message}");
        }

        var size = new FileInfo(target).Length;
        if (expectedSize is > 0 && size != expectedSize)
        {
            FileSystem.TryDeleteFile(target);
            throw new ModException($"{what} was downloaded, but the file has {size:N0} bytes instead of {expectedSize:N0}, "
                + "so it was not installed. Try again later.");
        }
        if (!string.IsNullOrWhiteSpace(expectedSha256)
            && !string.Equals(FileSystem.Sha256(target), expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            FileSystem.TryDeleteFile(target);
            throw new ModException($"{what} was downloaded, but the file is not the expected one (its checksum differs), so it was not installed.");
        }
        Log.Info($"Downloaded '{url}' to '{target}'.");
        return target;
    }
}
