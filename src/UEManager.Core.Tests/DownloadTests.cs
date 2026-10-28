using System.Diagnostics;
using System.Net;
using UEManager.Core.Mods;

namespace UEManager.Core.Tests;

public class DownloadTests
{
    [Fact]
    public async Task A_server_that_stops_sending_ends_the_download()
    {
        using var folder = new TestFolder();
        using var http = new HttpClient(new StallingServer());
        var watch = Stopwatch.StartNew();

        var error = await Assert.ThrowsAsync<ModException>(() => FileDownloader.DownloadAsync(http, "https://example.test/mod.zip",
            "mod.zip", null, null, folder.Combine("downloads"), "Mod", stallTimeout: TimeSpan.FromMilliseconds(200)));

        Assert.Contains("no data arrived", error.Message);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");
        Assert.Empty(Directory.GetFiles(folder.Combine("downloads")));
    }

    /// <summary>Answers with headers and a few bytes, then sends nothing more until the request is cancelled.</summary>
    private sealed class StallingServer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) });
    }

    private sealed class StallingStream : Stream
    {
        private bool sent;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!sent)
            {
                sent = true;
                buffer.Span[0] = 1;
                return 1;
            }
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
