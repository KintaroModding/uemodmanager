using System.IO;
using System.Net;
using System.Net.Http;
using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;
using UEManager.Core.Updates;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;
using L = UEManager.UI.Localizer;

namespace UEManager.UI.Tests;

public class ModPageUpdateRowTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "uemm-mod-update-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    private ModItemViewModel Row(string? version = "1.0", string? source = "Travel Anywhere 61 1.0.1 2026-10-29T13-42Z LbtEo64mS.zip", bool recommended = false)
    {
        var paths = new AppPaths(Path.Combine(folder, "Data"));
        paths.EnsureWritable();
        var profile = new GameProfile
        {
            Id = "test", DisplayName = "Test",
            ModPageUrl = "https://www.nexusmods.com/phantombladezero/mods/{id}",
        };
        var model = new MainViewModel(profile, paths, new ArtSet(null, null));
        return new ModItemViewModel(model, new ModRecord
        {
            Id = "a", Name = "Travel Anywhere", Version = version,
            SourceFileName = source, Recommended = recommended,
        }, Path.Combine(folder, "library"));
    }

    [Fact]
    public void A_newer_page_version_marks_even_a_disabled_mod_and_notifies_bindings() => UiThread.Run(() =>
    {
        var handler = new PageHandler();
        using var http = new HttpClient(handler);
        var row = Row();
        var notifications = 0;
        row.PropertyChanged += (_, _) => notifications++;
        row.CheckForUpdateAsync(new ModPageUpdates(http)).GetAwaiter().GetResult();
        Assert.False(row.Enabled);
        Assert.True(row.IsModUpdateAvailable);
        Assert.Equal("warn", row.ModUpdateTone);
        Assert.Contains("1.2", row.ModUpdateText);
        Assert.True(notifications >= 2);
    });

    [Fact]
    public void The_same_or_an_older_page_version_has_no_update_badge() => UiThread.Run(() =>
    {
        using var http = new HttpClient(new PageHandler());
        var row = Row("1.2.0");
        row.CheckForUpdateAsync(new ModPageUpdates(http)).GetAwaiter().GetResult();
        Assert.False(row.IsModUpdateAvailable);
        Assert.Equal(L.T("No newer page version"), row.ModUpdateText);
    });

    [Fact]
    public void A_failed_check_is_not_reported_as_up_to_date() => UiThread.Run(() =>
    {
        using var http = new HttpClient(new PageHandler { Status = HttpStatusCode.Forbidden });
        var row = Row();
        row.CheckForUpdateAsync(new ModPageUpdates(http)).GetAwaiter().GetResult();
        Assert.False(row.IsModUpdateAvailable);
        Assert.Equal(L.T("Could not check for updates"), row.ModUpdateText);
    });

    [Fact]
    public void An_unknown_installed_version_is_not_guessed() => UiThread.Run(() =>
    {
        using var http = new HttpClient(new PageHandler());
        var row = Row(version: null);
        row.CheckForUpdateAsync(new ModPageUpdates(http)).GetAwaiter().GetResult();
        Assert.False(row.IsModUpdateAvailable);
        Assert.Equal(L.T("Could not check for updates"), row.ModUpdateText);
    });

    [Fact]
    public void Missing_links_and_recommended_mods_do_not_send_requests() => UiThread.Run(() =>
    {
        var handler = new PageHandler();
        using var http = new HttpClient(handler);
        var updates = new ModPageUpdates(http);
        foreach (var row in new[] { Row(source: null), Row(recommended: true) })
        {
            row.CheckForUpdateAsync(updates).GetAwaiter().GetResult();
            Assert.Null(row.ModUpdateText);
            Assert.False(row.IsModUpdateAvailable);
        }
        Assert.Equal(0, handler.Requests);
    });

    [Fact]
    public void A_cancelled_row_waits_for_real_http_completion_but_does_not_apply_its_result() => UiThread.Run(() =>
    {
        var handler = new DelayedPageHandler();
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var row = Row();
        var check = row.CheckForUpdateAsync(new ModPageUpdates(http), cancellation.Token);
        Assert.Equal(L.T("Checking for mod updates…"), row.ModUpdateText);
        cancellation.Cancel();
        Assert.False(check.IsCompleted);
        handler.Answer.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<div id='file-version'>1.2</div>"),
        });
        Assert.ThrowsAny<OperationCanceledException>(() => check.GetAwaiter().GetResult());
        Assert.Null(row.ModUpdateText);
        Assert.False(row.IsModUpdateAvailable);
    });

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Nexus Mods API key is invalid or expired. Replace it in Settings.")]
    [InlineData(HttpStatusCode.Forbidden, "Nexus Mods API access was denied. Check your account permissions.")]
    [InlineData(HttpStatusCode.TooManyRequests, "Nexus Mods API rate limit reached. Try again later.")]
    public void Authenticated_rows_distinguish_key_permissions_and_quota_failures(HttpStatusCode status, string text) => UiThread.Run(() =>
    {
        using var http = new HttpClient(new PageHandler { Status = status });
        var row = Row();
        row.CheckForUpdateAsync(new ModPageUpdates(http, "synthetic-api-key")).GetAwaiter().GetResult();
        Assert.False(row.IsModUpdateAvailable);
        Assert.Equal(L.T(text), row.ModUpdateText);
    });

    private sealed class DelayedPageHandler : HttpMessageHandler
    {
        public TaskCompletionSource<HttpResponseMessage> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Answer.Task;
    }

    private sealed class PageHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                RequestMessage = request,
                Content = new StringContent("<div class='statitem' id='file-version'><div class='titlestat'>Version</div><div class='stat'>1.2</div></div>"),
            });
        }
    }
}
