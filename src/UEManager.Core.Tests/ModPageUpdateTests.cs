using System.Diagnostics;
using System.Net;
using System.Text;
using UEManager.Core.Updates;

namespace UEManager.Core.Tests;

public class ModPageUpdateTests
{
    private const string Page = "https://www.nexusmods.com/phantombladezero/mods/149";

    [Theory]
    [InlineData("1.2", "1.3", ModPageUpdateState.UpdateAvailable)]
    [InlineData("1.10", "1.9", ModPageUpdateState.UpToDate)]
    [InlineData("1.2", "1.2", ModPageUpdateState.UpToDate)]
    [InlineData("v1.2", "V1.2.0.0", ModPageUpdateState.UpToDate)]
    [InlineData(" 1.02.0 ", "1.2", ModPageUpdateState.UpToDate)]
    [InlineData("1", "1.0.1", ModPageUpdateState.UpdateAvailable)]
    [InlineData("999999999999999999999999", "1000000000000000000000000", ModPageUpdateState.UpdateAvailable)]
    [InlineData("0.0", "0", ModPageUpdateState.UpToDate)]
    public async Task Numeric_versions_are_compared_without_guessing(string installed, string latest, ModPageUpdateState state)
    {
        using var http = new HttpClient(new Answers((_, _) => Task.FromResult(Html(FileVersion(latest)))));
        var result = await new ModPageUpdates(http).CheckAsync(Page, installed);
        Assert.Equal(state, result.State);
        Assert.Equal(latest, result.LatestVersion);
    }

    [Theory]
    [InlineData(null, "2.0")]
    [InlineData("", "2.0")]
    [InlineData("Unknown", "2.0")]
    [InlineData("1.0-beta", "2.0")]
    [InlineData("1..0", "2.0")]
    [InlineData("1.0", "latest")]
    [InlineData("1.0", "2.0rc1")]
    [InlineData("1.0", "1.0+build.2")]
    [InlineData("1.0", "2,0")]
    [InlineData("1.0", "２.０")]
    [InlineData("1.0", "2.0\n3.0")]
    public async Task Incomparable_versions_are_unknown(string? installed, string latest)
    {
        using var http = new HttpClient(new Answers((_, _) => Task.FromResult(Html(FileVersion(latest)))));
        Assert.Equal(ModPageUpdateState.Unknown, (await new ModPageUpdates(http).CheckAsync(Page, installed)).State);
    }

    [Theory]
    [InlineData("<div id='file-version' class='stat'> 2.0 </div>")]
    [InlineData("<div class='stat file-version'><span>v2.0</span></div>")]
    [InlineData("<DIV ID=file-version>2.0</DIV>")]
    [InlineData("<div class='statitem' id='file-version'><div class='titlestat'>Version</div><div class='stat'>2.0</div></div>")]
    [InlineData("<div class='statitem' id='file-version'><div class='titlestat'>Version</div><div class='stat'><span>2.0</span></div></div><div class='stat'>999.0</div>")]
    [InlineData("<div id='file-version'>&#50;.0</div>")]
    [InlineData("<script type='application/ld+json'>{\"@type\":\"SoftwareApplication\",\"softwareVersion\":\"2.0\"}</script>")]
    [InlineData("<script type='application/ld+json'>{\"@graph\":[{\"softwareVersion\":\"2.0\"}]}</script>")]
    [InlineData("<script type='application/ld+json'>[{\"softwareVersion\":\"2.0\"}]</script>")]
    [InlineData("<script type='application/ld+json'>broken</script><div id='file-version'>2.0</div>")]
    public async Task Reads_only_recognizable_public_version_structures(string html)
    {
        using var http = new HttpClient(new Answers((_, _) => Task.FromResult(Html(html))));
        Assert.Equal(ModPageUpdateState.UpdateAvailable, (await new ModPageUpdates(http).CheckAsync(Page, "1.0")).State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<title>A mod 9.0</title><div>Version 9.0</div>")]
    [InlineData("<div id='file-version'></div>")]
    [InlineData("<div id='file-version'>Version 9.0</div>")]
    [InlineData("<div id='file-version'><div class='titlestat'>Version</div><div class='stat'></div></div><div class='stat'>9.0</div>")]
    [InlineData("<div id='file-version'><div class='titlestat'>Version 9.0</div></div>")]
    [InlineData("<div id='file-version'><div class='stat'>2.0</div><div class='stat'>3.0</div></div>")]
    [InlineData("<div class='not-file-version'>9.0</div>")]
    [InlineData("<script>{\"softwareVersion\":\"9.0\"}</script>")]
    [InlineData("<script type='application/ld+json'>{\"version\":\"9.0\"}</script>")]
    [InlineData("<script type='application/ld+json'>{\"softwareVersion\":9}</script>")]
    [InlineData("<script type='application/ld+json'>{\"softwareVersion\":</script>")]
    [InlineData("<div id='file-version'>2.0</div><div id='file-version'>3.0</div>")]
    public async Task Missing_invalid_or_conflicting_markup_is_unknown(string html)
    {
        using var http = new HttpClient(new Answers((_, _) => Task.FromResult(Html(html))));
        var result = await new ModPageUpdates(http).CheckAsync(Page, "1.0");
        Assert.Equal(new ModPageUpdateResult(ModPageUpdateState.Unknown), result);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Redirect)]
    public async Task Http_failures_are_unknown_and_cached(HttpStatusCode status)
    {
        var handler = new Answers((_, _) => Task.FromResult(Html(FileVersion("2.0"), status)));
        using var http = new HttpClient(handler);
        var updates = new ModPageUpdates(http);
        Assert.Equal(ModPageUpdateState.Unknown, (await updates.CheckAsync(Page, "1.0")).State);
        Assert.Equal(ModPageUpdateState.Unknown, (await updates.CheckAsync(Page, "1.0")).State);
        Assert.Equal(1, handler.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Network_errors_and_timeouts_are_unknown_and_cached(bool timeout)
    {
        var handler = new Answers(async (_, token) =>
        {
            if (timeout) await Task.Delay(Timeout.Infinite, token);
            throw new HttpRequestException("offline");
        });
        using var http = new HttpClient(handler);
        var updates = new ModPageUpdates(http, TimeProvider.System, TimeSpan.FromMilliseconds(100));
        var watch = Stopwatch.StartNew();
        Assert.Equal(ModPageUpdateState.Unknown, (await updates.CheckAsync(Page, "1.0")).State);
        Assert.Equal(ModPageUpdateState.Unknown, (await updates.CheckAsync(Page, "1.0")).State);
        Assert.Equal(1, handler.Count);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Shared_remote_cache_is_compared_again_after_installing_an_update()
    {
        var handler = new Answers((_, _) => Task.FromResult(Html(FileVersion("2.0"))));
        using var http = new HttpClient(handler);
        var updates = new ModPageUpdates(http);
        Assert.Equal(ModPageUpdateState.Unknown, (await updates.CheckAsync(Page, null)).State);
        Assert.Equal(ModPageUpdateState.UpdateAvailable, (await updates.CheckAsync(Page, "1.0")).State);
        Assert.Equal(ModPageUpdateState.UpToDate, (await updates.CheckAsync(Page, "2.0")).State);
        Assert.Equal(1, handler.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Successes_and_failures_expire_after_ten_minutes(bool success)
    {
        var clock = new Clock();
        var handler = new Answers((_, _) => Task.FromResult(Html(success ? FileVersion("2.0") : "blocked")));
        using var http = new HttpClient(handler);
        var updates = new ModPageUpdates(http, clock, TimeSpan.FromSeconds(12));
        await updates.CheckAsync(Page, "1.0");
        clock.Now += TimeSpan.FromMinutes(10) - TimeSpan.FromTicks(1);
        await updates.CheckAsync(Page, "1.0");
        Assert.Equal(1, handler.Count);
        clock.Now += TimeSpan.FromTicks(1);
        await updates.CheckAsync(Page, "1.0");
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task Concurrent_requests_share_one_fetch_and_cancelling_one_waiter_does_not_cancel_the_rest()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Answers(async (_, token) =>
        {
            await release.Task.WaitAsync(token);
            return Html(FileVersion("2.0"));
        });
        using var http = new HttpClient(handler);
        var updates = new ModPageUpdates(http);
        using var cancellation = new CancellationTokenSource();
        var cancelled = updates.CheckAsync(Page, "1.0", cancellation.Token);
        var others = Enumerable.Range(0, 32).Select(_ => updates.CheckAsync(Page, "1.0")).ToArray();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        release.SetResult();
        var results = await Task.WhenAll(others);
        Assert.All(results, result => Assert.Equal(ModPageUpdateState.UpdateAvailable, result.State));
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task Page_aliases_and_tab_queries_share_the_canonical_cache()
    {
        var handler = new Answers((request, _) =>
        {
            Assert.Equal(Page, request.RequestUri!.AbsoluteUri);
            return Task.FromResult(Html(FileVersion("2.0")));
        });
        using var http = new HttpClient(handler);
        var updates = new ModPageUpdates(http);
        await updates.CheckAsync("https://nexusmods.com/phantombladezero/mods/149/?tab=files#top", "1.0");
        await updates.CheckAsync(Page, "1.0");
        Assert.Equal(1, handler.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/phantombladezero/mods/149")]
    [InlineData("http://www.nexusmods.com/phantombladezero/mods/149")]
    [InlineData("https://nexusmods.com.evil.test/game/mods/1")]
    [InlineData("https://evil.test@nexusmods.com/game/mods/1")]
    [InlineData("https://nexusmods.com@evil.test/game/mods/1")]
    [InlineData("https://api.nexusmods.com/game/mods/1")]
    [InlineData("https://nexusmods.com:443/game/mods/1")]
    [InlineData("https://nexusmods.com/game/mods/not-a-number")]
    [InlineData("https://nexusmods.com/game/mods/1/files")]
    [InlineData("https://nexusmods.com/game/../mods/1")]
    [InlineData("https://nexusmods.com/game/mods/%31")]
    [InlineData("https://nexusmods.com/game/mods/1\\@evil.test")]
    [InlineData("https://nexusmods.com/game/mods/1\n")]
    [InlineData("file:///game/mods/1")]
    public async Task Unsupported_or_unsafe_addresses_never_make_a_request(string? address)
    {
        var handler = new Answers((_, _) => throw new InvalidOperationException("must not request"));
        using var http = new HttpClient(handler);
        Assert.Equal(ModPageUpdateState.Unknown, (await new ModPageUpdates(http).CheckAsync(address!, "1.0")).State);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task A_redirected_response_from_another_host_is_not_trusted()
    {
        using var http = new HttpClient(new Answers((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.test/game/mods/1"),
            Content = new StringContent(FileVersion("9.0"))
        })));
        Assert.Equal(ModPageUpdateState.Unknown, (await new ModPageUpdates(http).CheckAsync(Page, "1.0")).State);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Oversized_pages_with_or_without_content_length_are_unknown(bool knownLength)
    {
        using var http = new HttpClient(new Answers((_, _) =>
        {
            var bytes = Encoding.UTF8.GetBytes(FileVersion("9.0") + new string(' ', 4 * 1024 * 1024));
            HttpContent content = knownLength ? new ByteArrayContent(bytes) : new StreamContent(new ForwardStream(bytes));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }));
        Assert.Equal(ModPageUpdateState.Unknown, (await new ModPageUpdates(http).CheckAsync(Page, "1.0")).State);
    }

    [Fact]
    public async Task A_stalled_response_body_times_out_as_unknown()
    {
        using var http = new HttpClient(new Answers((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new ForwardStream(null))
        })));
        var updates = new ModPageUpdates(http, TimeProvider.System, TimeSpan.FromMilliseconds(100));
        var result = await updates.CheckAsync(Page, "1.0").WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ModPageUpdateState.Unknown, result.State);
    }

    [Theory]
    [InlineData(null, "UEManager/1.0")]
    [InlineData("UEModManager/1.6.4", "UEModManager/1.6.4")]
    public async Task Uses_the_callers_user_agent_and_only_supplies_a_fallback_when_missing(string? configured, string expected)
    {
        using var http = new HttpClient(new Answers((request, _) =>
        {
            Assert.Equal(expected, request.Headers.UserAgent.ToString());
            return Task.FromResult(Html(FileVersion("2.0")));
        }));
        if (configured is not null) http.DefaultRequestHeaders.UserAgent.ParseAdd(configured);
        await new ModPageUpdates(http).CheckAsync(Page, "1.0");
    }

    private static string FileVersion(string version) => $"<div class='stat' id='file-version'>{version}</div>";
    private static HttpResponseMessage Html(string html, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(html, Encoding.UTF8, "text/html") };

    private sealed class Answers(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        private int count;
        public int Count => Volatile.Read(ref count);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref count);
            return answer(request, cancellationToken);
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ForwardStream(byte[]? bytes) : Stream
    {
        private int offset;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (bytes is null)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return 0;
            }
            var length = Math.Min(buffer.Length, bytes.Length - offset);
            bytes.AsMemory(offset, length).CopyTo(buffer);
            offset += length;
            return length;
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
