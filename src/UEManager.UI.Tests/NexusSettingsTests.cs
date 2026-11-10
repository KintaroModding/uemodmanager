using System.IO;
using System.Net;
using System.Net.Http;
using UEManager.Core.Games;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;
using L = UEManager.UI.Localizer;

namespace UEManager.UI.Tests;

public class NexusSettingsTests : IDisposable
{
    private const string FakeKey = "synthetic-test-key-not-a-real-credential";
    private readonly string folder = Path.Combine(Path.GetTempPath(), "uemm-nexus-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private MainViewModel Create(HttpClient http) => new(
        new GameProfile { Id = "test", DisplayName = "Test" },
        new AppPaths(folder), new ArtSet(null, null), nexusHttp: http);

    [Fact]
    public void Saving_testing_and_clearing_do_not_put_the_key_in_settings_or_status_text() => UiThread.Run(() =>
    {
        var handler = new Answers(HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        var model = Create(http);
        Assert.False(model.HasNexusApiKey);
        Assert.True(model.SaveNexusApiKeyAsync(FakeKey).GetAwaiter().GetResult());
        Assert.True(model.HasNexusApiKey);
        Assert.Equal(L.T("Nexus Mods API key is valid. Mod update checks use the official API."), model.NexusApiKeyStatus);
        Assert.DoesNotContain(FakeKey, model.NexusApiKeyStatus);
        Assert.False(File.Exists(Path.Combine(folder, "settings.json")));
        Assert.Equal(FakeKey, new NexusApiKeyStore(new AppPaths(folder)).Load());
        var restarted = Create(http);
        Assert.True(restarted.HasNexusApiKey);
        Assert.DoesNotContain(FakeKey, restarted.NexusApiKeyStatus);
        restarted.ClearNexusApiKeyCommand.Execute(null);
        Assert.False(restarted.HasNexusApiKey);
        Assert.Null(new NexusApiKeyStore(new AppPaths(folder)).Load());
        Assert.Equal(1, handler.Count);
    });

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Nexus Mods API key is invalid or expired. Replace it in Settings.")]
    [InlineData(HttpStatusCode.Forbidden, "Nexus Mods API access was denied. Check your account permissions.")]
    [InlineData(HttpStatusCode.TooManyRequests, "Nexus Mods API rate limit reached. Try again later.")]
    public void Authentication_and_quota_failures_are_distinct_and_never_echo_the_response(HttpStatusCode status, string text) => UiThread.Run(() =>
    {
        using var http = new HttpClient(new Answers(status));
        var model = Create(http);
        Assert.True(model.SaveNexusApiKeyAsync(FakeKey).GetAwaiter().GetResult());
        Assert.True(model.HasNexusApiKey);
        Assert.Equal(L.T(text), model.NexusApiKeyStatus);
        Assert.DoesNotContain(FakeKey, model.NexusApiKeyStatus);
        Assert.True(model.CanEditNexusApiKey);
    });

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("key with spaces")]
    [InlineData("key\r\nheader: injection")]
    public void Invalid_input_is_not_saved_and_never_reaches_http(string input) => UiThread.Run(() =>
    {
        var handler = new Answers(HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        var model = Create(http);
        Assert.False(model.SaveNexusApiKeyAsync(input).GetAwaiter().GetResult());
        Assert.False(model.HasNexusApiKey);
        Assert.Equal(0, handler.Count);
        Assert.Null(new NexusApiKeyStore(new AppPaths(folder)).Load());
    });

    [Fact]
    public void A_corrupt_credential_can_be_cleared_without_exposing_or_decrypting_it() => UiThread.Run(() =>
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "nexus-api-key.dat"), "corrupt protected value");
        using var http = new HttpClient(new Answers(HttpStatusCode.OK));
        var model = Create(http);
        Assert.False(model.HasNexusApiKey);
        Assert.Equal(L.T("Could not read or save the protected API key. Re-enter it, or clear the saved key."), model.NexusApiKeyStatus);
        model.ClearNexusApiKeyCommand.Execute(null);
        Assert.False(File.Exists(Path.Combine(folder, "nexus-api-key.dat")));
    });

    [Fact]
    public void Reset_credential_stage_clears_live_state_even_when_later_settings_reset_fails() => UiThread.Run(() =>
    {
        using var http = new HttpClient(new Answers(HttpStatusCode.OK));
        var model = Create(http);
        Assert.True(model.SaveNexusApiKeyAsync(FakeKey).GetAwaiter().GetResult());
        // Simulate the next reset stage failing, without restarting the test runner.
        Directory.CreateDirectory(Path.Combine(folder, "settings.json"));
        model.ClearStoredNexusApiKey();
        Assert.Throws<UnauthorizedAccessException>(() => File.Delete(Path.Combine(folder, "settings.json")));
        Assert.False(model.HasNexusApiKey);
        Assert.Null(new NexusApiKeyStore(new AppPaths(folder)).Load());
    });

    [Fact]
    public void Reset_during_validation_does_not_restore_the_old_key_or_status() => UiThread.Run(() =>
    {
        using var http = new HttpClient(new WaitingValidation());
        var model = Create(http);
        var save = model.SaveNexusApiKeyAsync(FakeKey);
        Assert.True(model.HasNexusApiKey);
        Assert.False(model.CanEditNexusApiKey);
        model.ClearStoredNexusApiKey();
        Assert.True(save.Wait(TimeSpan.FromSeconds(5)), "Revoked validation did not finish.");
        save.GetAwaiter().GetResult();
        Assert.False(model.HasNexusApiKey);
        Assert.True(model.CanEditNexusApiKey);
        Assert.Null(new NexusApiKeyStore(new AppPaths(folder)).Load());
        Assert.Equal(L.T("No API key saved. Mod updates use public pages, which may be blocked by site protection."), model.NexusApiKeyStatus);
    });

    private sealed class WaitingValidation : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class Answers(HttpStatusCode status) : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            Assert.Equal("https://api.nexusmods.com/v1/users/validate.json", request.RequestUri!.AbsoluteUri);
            Assert.Equal(FakeKey, Assert.Single(request.Headers.GetValues("apikey")));
            return Task.FromResult(new HttpResponseMessage(status)
            {
                RequestMessage = request,
                Content = new StringContent(status == HttpStatusCode.OK ? "{\"user_id\":123}" : FakeKey),
            });
        }
    }
}
