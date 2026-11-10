using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using UEManager.Core.Updates;

namespace UEManager.Core.Tests;

public class NexusApiUpdateTests
{
    private const string Page = "https://www.nexusmods.com/phantombladezero/mods/149";
    private const string Api = "https://api.nexusmods.com/v1/games/phantombladezero/mods/149.json";
    private const string Validate = "https://api.nexusmods.com/v1/users/validate.json";
    private const string Key = "test-secret-never-include-in-results";

    [Fact]
    public async Task Uses_only_the_official_get_endpoint_and_a_request_scoped_header()
    {
        var handler = new Answers((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(Api, request.RequestUri!.AbsoluteUri);
            Assert.Equal(Key, Assert.Single(request.Headers.GetValues("apikey")));
            Assert.Equal("application/json", request.Headers.Accept.ToString());
            Assert.Equal("UEManager/1.0", request.Headers.UserAgent.ToString());
            Assert.Null(request.Content);
            Assert.DoesNotContain(Key, request.RequestUri.AbsoluteUri);
            return Task.FromResult(Version("2.0"));
        });
        using var http = new HttpClient(handler);
        var updates = new ModPageUpdates(http, " " + Key + " ");
        var result = await updates.CheckAsync("https://nexusmods.com/PhantomBladeZero/mods/149/?tab=files#top", "1.0");
        Assert.Equal(new ModPageUpdateResult(ModPageUpdateState.UpdateAvailable, "2.0"), result);
        Assert.DoesNotContain(Key, result.ToString());
        Assert.False(http.DefaultRequestHeaders.Contains("apikey"));
        await updates.CheckAsync(Page, "1.0");
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task Validation_uses_the_official_endpoint_and_does_not_return_user_details()
    {
        using var http = new HttpClient(new Answers((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(Validate, request.RequestUri!.AbsoluteUri);
            Assert.Equal(Key, Assert.Single(request.Headers.GetValues("apikey")));
            Assert.Equal("Caller/2.0", request.Headers.UserAgent.ToString());
            return Task.FromResult(Json("{\"user_id\":42,\"name\":\"private\",\"key\":\"" + Key + "\"}"));
        }));
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Caller/2.0");
        Assert.Equal(ModPageUpdateFailure.None, await new ModPageUpdates(http, Key).ValidateApiKeyAsync());
        Assert.False(http.DefaultRequestHeaders.Contains("apikey"));
    }

    [Theory]
    [InlineData("{\"user_id\":1}", ModPageUpdateFailure.None)]
    [InlineData("{\"user_id\":0}", ModPageUpdateFailure.Unavailable)]
    [InlineData("{\"user_id\":-1}", ModPageUpdateFailure.Unavailable)]
    [InlineData("{\"user_id\":\"1\"}", ModPageUpdateFailure.Unavailable)]
    [InlineData("{\"user_id\":1.5}", ModPageUpdateFailure.Unavailable)]
    [InlineData("{\"user_id\":null}", ModPageUpdateFailure.Unavailable)]
    [InlineData("{}", ModPageUpdateFailure.Unavailable)]
    [InlineData("[]", ModPageUpdateFailure.Unavailable)]
    [InlineData("null", ModPageUpdateFailure.Unavailable)]
    [InlineData("broken", ModPageUpdateFailure.Unavailable)]
    public async Task Validation_requires_a_successful_object_with_a_positive_user_id(string json, ModPageUpdateFailure expected)
    {
        using var http = new HttpClient(new Answers((_, _) => Task.FromResult(Json(json))));
        Assert.Equal(expected, await new ModPageUpdates(http, Key).ValidateApiKeyAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("key\r\ninjected: value")]
    [InlineData("key\n")]
    [InlineData("\tkey")]
    [InlineData("key\0")]
    [InlineData("key\u007f")]
    [InlineData("key\u0080")]
    public async Task Invalid_keys_never_make_requests_or_fall_back_to_html(string key)
    {
        var handler = new Answers((_, _) => throw new InvalidOperationException("Must not request"));
        using var http = new HttpClient(handler);
        var updates = new ModPageUpdates(http, key);
        Assert.Equal(ModPageUpdateFailure.InvalidApiKey, await updates.ValidateApiKeyAsync());
        AssertFailure(ModPageUpdateFailure.InvalidApiKey, await updates.CheckAsync(Page, "1.0"));
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task Missing_key_cannot_be_validated_and_overlong_keys_are_rejected()
    {
        var handler = new Answers((_, _) => throw new InvalidOperationException("Must not request"));
        using var http = new HttpClient(handler);
        Assert.Equal(ModPageUpdateFailure.InvalidApiKey, await new ModPageUpdates(http).ValidateApiKeyAsync());
        var overlong = new ModPageUpdates(http, new string('a', 4097));
        Assert.Equal(ModPageUpdateFailure.InvalidApiKey, await overlong.ValidateApiKeyAsync());
        AssertFailure(ModPageUpdateFailure.InvalidApiKey, await overlong.CheckAsync(Page, "1.0"));
        Assert.Equal(0, handler.Count);
    }

    [Theory]
    [InlineData("1.0", "2.0", ModPageUpdateState.UpdateAvailable)]
    [InlineData("2.0", "1.9", ModPageUpdateState.UpToDate)]
    [InlineData("v1.02", "V1.2.0.0", ModPageUpdateState.UpToDate)]
    [InlineData(null, "2.0", ModPageUpdateState.Unknown)]
    [InlineData("unknown", "2.0", ModPageUpdateState.Unknown)]
    [InlineData("1.0", "2.0-beta", ModPageUpdateState.Unknown)]
    public async Task Api_versions_reuse_strict_numeric_comparison(string? installed, string latest, ModPageUpdateState state)
    {
        using var http = new HttpClient(new Answers((_, _) => Task.FromResult(Version(latest))));
        Assert.Equal(new ModPageUpdateResult(state, latest), await new ModPageUpdates(http, Key).CheckAsync(Page, installed));
    }

    [Theory]
    [InlineData("{\"version\":2}")]
    [InlineData("{\"version\":null}")]
    [InlineData("{\"version\":\"\"}")]
    [InlineData("{\"version\":\"2.0\",\"available\":false}")]
    [InlineData("{\"version\":\"2.0\",\"available\":null}")]
    [InlineData("{\"version\":\"2.0\",\"available\":\"true\"}")]
    [InlineData("{\"version\":\"2.0\",\"status\":\"hidden\"}")]
    [InlineData("{\"version\":\"2.0\",\"status\":\"deleted\"}")]
    [InlineData("{\"version\":\"2.0\",\"status\":null}")]
    [InlineData("{\"softwareVersion\":\"2.0\"}")]
    [InlineData("[{\"version\":\"2.0\"}]")]
    [InlineData("null")]
    [InlineData("malformed")]
    [InlineData("<div id='file-version'>2.0</div>")]
    public async Task Unavailable_or_non_string_versions_are_unknown_without_html_fallback(string json)
    {
        var handler = new Answers((request, _) =>
        {
            Assert.Equal(Api, request.RequestUri!.AbsoluteUri);
            return Task.FromResult(Json(json));
        });
        using var http = new HttpClient(handler);
        var updates = new ModPageUpdates(http, Key);
        AssertFailure(ModPageUpdateFailure.Unavailable, await updates.CheckAsync(Page, "2.0"));
        AssertFailure(ModPageUpdateFailure.Unavailable, await updates.CheckAsync(Page, "2.0"));
        Assert.Equal(1, handler.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ModPageUpdateFailure.InvalidApiKey)]
    [InlineData(HttpStatusCode.Forbidden, ModPageUpdateFailure.AccessDenied)]
    [InlineData(HttpStatusCode.TooManyRequests, ModPageUpdateFailure.RateLimited)]
    [InlineData(HttpStatusCode.NotFound, ModPageUpdateFailure.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, ModPageUpdateFailure.Unavailable)]
    [InlineData(HttpStatusCode.Redirect, ModPageUpdateFailure.Unavailable)]
    [InlineData(HttpStatusCode.Created, ModPageUpdateFailure.Unavailable)]
    public async Task Http_failures_map_to_safe_results_for_checks_and_validation(HttpStatusCode status, ModPageUpdateFailure failure)
    {
        var handler = new Answers((_, _) => Task.FromResult(Json(Key, status)));
        using var http = new HttpClient(handler);
        var updates = new ModPageUpdates(http, Key);
        var result = await updates.CheckAsync(Page, "1.0");
        AssertFailure(failure, result);
        Assert.DoesNotContain(Key, result.ToString());
        Assert.Equal(failure, await new ModPageUpdates(http, Key).ValidateApiKeyAsync());
        Assert.Equal(2, handler.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unauthorized_short_circuits_all_mods_and_validation_for_this_instance(bool validationFirst)
    {
        var clock = new Clock();
        var handler = new Answers((_, _) => Task.FromResult(Json(Key, HttpStatusCode.Unauthorized)));
        using var http = new HttpClient(handler);
        var updates = new ModPageUpdates(http, clock, TimeSpan.FromSeconds(12), Key);
        if (validationFirst) Assert.Equal(ModPageUpdateFailure.InvalidApiKey, await updates.ValidateApiKeyAsync());
        else AssertFailure(ModPageUpdateFailure.InvalidApiKey, await updates.CheckAsync(Page, "1.0"));
        clock.Now += TimeSpan.FromDays(1);
        AssertFailure(ModPageUpdateFailure.InvalidApiKey, await updates.CheckAsync(Page + "0", "1.0"));
        Assert.Equal(ModPageUpdateFailure.InvalidApiKey, await updates.ValidateApiKeyAsync());
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task Forbidden_is_cached_for_only_that_mod_and_does_not_reject_the_key()
    {
        var handler = new Answers((request, _) => Task.FromResult(request.RequestUri!.AbsoluteUri == Api
            ? Json(Key, HttpStatusCode.Forbidden) : Json("{\"user_id\":1,\"version\":\"2.0\"}")));
        using var http = new HttpClient(handler);
        var updates = new ModPageUpdates(http, Key);
        AssertFailure(ModPageUpdateFailure.AccessDenied, await updates.CheckAsync(Page, "1.0"));
        AssertFailure(ModPageUpdateFailure.AccessDenied, await updates.CheckAsync(Page, "1.0"));
        Assert.Equal(ModPageUpdateState.UpdateAvailable, (await updates.CheckAsync(Page + "0", "1.0")).State);
        Assert.Equal(ModPageUpdateFailure.None, await updates.ValidateApiKeyAsync());
        Assert.Equal(3, handler.Count);
    }

    [Theory]
    [InlineData("default", 60)]
    [InlineData("delta", 120)]
    [InlineData("date", 180)]
    [InlineData("past", 60)]
    public async Task Rate_limit_stops_all_requests_until_retry_after_then_allows_retry(string mode, int seconds)
    {
        var clock = new Clock();
        var first = true;
        var handler = new Answers((_, _) =>
        {
            if (!first) return Task.FromResult(Json("{\"user_id\":1,\"version\":\"2.0\"}"));
            first = false;
            var response = Json(Key, HttpStatusCode.TooManyRequests);
            if (mode == "delta") response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            if (mode == "date") response.Headers.RetryAfter = new RetryConditionHeaderValue(clock.Now.AddSeconds(seconds));
            if (mode == "past") response.Headers.RetryAfter = new RetryConditionHeaderValue(clock.Now.AddSeconds(-1));
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        var updates = new ModPageUpdates(http, clock, TimeSpan.FromSeconds(12), Key);
        AssertFailure(ModPageUpdateFailure.RateLimited, await updates.CheckAsync(Page, "1.0"));
        clock.Now += TimeSpan.FromSeconds(seconds) - TimeSpan.FromTicks(1);
        AssertFailure(ModPageUpdateFailure.RateLimited, await updates.CheckAsync(Page + "0", "1.0"));
        Assert.Equal(ModPageUpdateFailure.RateLimited, await updates.ValidateApiKeyAsync());
        Assert.Equal(1, handler.Count);
        clock.Now += TimeSpan.FromTicks(1);
        Assert.Equal(ModPageUpdateState.UpdateAvailable, (await updates.CheckAsync(Page, "1.0")).State);
        Assert.Equal(ModPageUpdateState.UpdateAvailable, (await updates.CheckAsync(Page + "0", "1.0")).State);
        Assert.Equal(ModPageUpdateFailure.None, await updates.ValidateApiKeyAsync());
        Assert.Equal(4, handler.Count);
    }

    [Fact]
    public async Task Validation_rate_limits_mod_checks_and_a_new_key_gets_a_fresh_instance()
    {
        var clock = new Clock();
        var first = true;
        var handler = new Answers((request, _) =>
        {
            if (first)
            {
                first = false;
                Assert.Equal(Validate, request.RequestUri!.AbsoluteUri);
                return Task.FromResult(Json(Key, HttpStatusCode.TooManyRequests));
            }
            return Task.FromResult(Version("2.0"));
        });
        using var http = new HttpClient(handler);
        var updates = new ModPageUpdates(http, clock, TimeSpan.FromSeconds(12), Key);
        Assert.Equal(ModPageUpdateFailure.RateLimited, await updates.ValidateApiKeyAsync());
        AssertFailure(ModPageUpdateFailure.RateLimited, await updates.CheckAsync(Page, "1.0"));
        Assert.Equal(1, handler.Count);
        Assert.Equal(ModPageUpdateState.UpdateAvailable, (await new ModPageUpdates(http, "changed-key").CheckAsync(Page, "1.0")).State);
        clock.Now += TimeSpan.FromMinutes(1);
        Assert.Equal(ModPageUpdateState.UpdateAvailable, (await updates.CheckAsync(Page, "1.0")).State);
        Assert.Equal(3, handler.Count);
    }

    [Fact]
    public async Task Replacing_a_rejected_key_does_not_reuse_its_error_cache()
    {
        var handler = new Answers((request, _) => Task.FromResult(request.Headers.GetValues("apikey").Single() == Key
            ? Json(Key, HttpStatusCode.Unauthorized) : Version("2.0")));
        using var http = new HttpClient(handler);
        var rejected = new ModPageUpdates(http, Key);
        AssertFailure(ModPageUpdateFailure.InvalidApiKey, await rejected.CheckAsync(Page, "1.0"));
        Assert.Equal(ModPageUpdateState.UpdateAvailable, (await new ModPageUpdates(http, "changed-key").CheckAsync(Page, "1.0")).State);
        AssertFailure(ModPageUpdateFailure.InvalidApiKey, await rejected.CheckAsync(Page, "1.0"));
        Assert.Equal(2, handler.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ModPageUpdateFailure.InvalidApiKey)]
    [InlineData(HttpStatusCode.TooManyRequests, ModPageUpdateFailure.RateLimited)]
    public async Task Already_queued_mods_stop_sending_after_a_global_failure(HttpStatusCode status, ModPageUpdateFailure failure)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Answers(async (_, token) =>
        {
            await release.Task.WaitAsync(token);
            return Json(Key, status);
        });
        using var http = new HttpClient(handler);
        var updates = new ModPageUpdates(http, Key);
        var checks = Enumerable.Range(0, 20).Select(i => updates.CheckAsync(Page + i, "1.0")).ToArray();
        Assert.Equal(1, handler.Count);
        release.SetResult();
        Assert.All(await Task.WhenAll(checks), result => AssertFailure(failure, result));
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task Shared_api_fetch_recompares_local_versions_and_waiter_cancellation_is_independent()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Answers(async (_, token) =>
        {
            await release.Task.WaitAsync(token);
            return Version("2.0");
        });
        using var http = new HttpClient(handler);
        var updates = new ModPageUpdates(http, Key);
        using var cancellation = new CancellationTokenSource();
        var cancelled = updates.CheckAsync(Page, "1.0", cancellation.Token);
        var checks = Enumerable.Range(0, 20).Select(_ => updates.CheckAsync(Page, "1.0")).ToArray();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        release.SetResult();
        Assert.All(await Task.WhenAll(checks), result => Assert.Equal(ModPageUpdateState.UpdateAvailable, result.State));
        Assert.Equal(ModPageUpdateState.UpToDate, (await updates.CheckAsync(Page, "2.0")).State);
        Assert.Equal(ModPageUpdateState.Unknown, (await updates.CheckAsync(Page, null)).State);
        Assert.Equal(1, handler.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Ordinary_api_results_expire_after_ten_minutes(HttpStatusCode status)
    {
        var clock = new Clock();
        var handler = new Answers((_, _) => Task.FromResult(Json("{\"version\":\"2.0\"}", status)));
        using var http = new HttpClient(handler);
        var updates = new ModPageUpdates(http, clock, TimeSpan.FromSeconds(12), Key);
        await updates.CheckAsync(Page, "1.0");
        clock.Now += TimeSpan.FromMinutes(10) - TimeSpan.FromTicks(1);
        await updates.CheckAsync(Page, "1.0");
        Assert.Equal(1, handler.Count);
        clock.Now += TimeSpan.FromTicks(1);
        await updates.CheckAsync(Page, "1.0");
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task Different_keys_and_html_mode_do_not_share_caches_or_headers_on_one_client()
    {
        var handler = new Answers((request, _) =>
        {
            if (request.Headers.TryGetValues("apikey", out var keys))
                return Task.FromResult(Version(keys.Single() == Key ? "2.0" : "3.0"));
            Assert.Equal(Page, request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<div id='file-version'>4.0</div>")
            });
        });
        using var http = new HttpClient(handler);
        var first = new ModPageUpdates(http, Key);
        Assert.Equal("2.0", (await first.CheckAsync(Page, "1.0")).LatestVersion);
        Assert.Equal("3.0", (await new ModPageUpdates(http, "another-key").CheckAsync(Page, "1.0")).LatestVersion);
        Assert.Equal("4.0", (await new ModPageUpdates(http).CheckAsync(Page, "1.0")).LatestVersion);
        Assert.Equal("2.0", (await first.CheckAsync(Page, "1.0")).LatestVersion);
        Assert.False(http.DefaultRequestHeaders.Contains("apikey"));
        Assert.Equal(3, handler.Count);
    }

    [Theory]
    [InlineData("http://nexusmods.com/game/mods/1")]
    [InlineData("https://nexusmods.com.evil.test/game/mods/1")]
    [InlineData("https://nexusmods.com@evil.test/game/mods/1")]
    [InlineData("https://api.nexusmods.com/v1/games/game/mods/1.json")]
    [InlineData("https://nexusmods.com/game/mods/1/files")]
    [InlineData("https://nexusmods.com/game/mods/%31")]
    [InlineData("https://nexusmods.com:443/game/mods/1")]
    public async Task Api_mode_still_rejects_unsafe_or_unsupported_page_urls(string url)
    {
        var handler = new Answers((_, _) => throw new InvalidOperationException("Must not request"));
        using var http = new HttpClient(handler);
        Assert.Equal(new ModPageUpdateResult(ModPageUpdateState.Unknown), await new ModPageUpdates(http, Key).CheckAsync(url, "1.0"));
        Assert.Equal(0, handler.Count);
    }

    [Theory]
    [InlineData("https://evil.test/version.json")]
    [InlineData("https://www.nexusmods.com/phantombladezero/mods/149")]
    [InlineData("https://api.nexusmods.com/v1/games/other/mods/149.json")]
    [InlineData("https://api.nexusmods.com/v1/games/phantombladezero/mods/149.json?redirected=1")]
    public async Task Final_redirected_responses_are_not_trusted_even_on_the_api_host(string final)
    {
        using var http = new HttpClient(new Answers((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, final),
            Content = new StringContent("{\"user_id\":1,\"version\":\"2.0\"}")
        })));
        var updates = new ModPageUpdates(http, Key);
        AssertFailure(ModPageUpdateFailure.Unavailable, await updates.CheckAsync(Page, "1.0"));
        Assert.Equal(ModPageUpdateFailure.Unavailable, await updates.ValidateApiKeyAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bounded_api_bodies_reject_oversized_data_with_or_without_content_length(bool knownLength)
    {
        var bytes = Encoding.UTF8.GetBytes("{\"user_id\":1,\"version\":\"2.0\"}" + new string(' ', 4 * 1024 * 1024));
        using var http = new HttpClient(new Answers((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = knownLength ? new ByteArrayContent(bytes) : new StreamContent(new ForwardStream(bytes))
        })));
        var updates = new ModPageUpdates(http, Key);
        AssertFailure(ModPageUpdateFailure.Unavailable, await updates.CheckAsync(Page, "1.0"));
        Assert.Equal(ModPageUpdateFailure.Unavailable, await updates.ValidateApiKeyAsync());
    }

    [Theory]
    [InlineData("network")]
    [InlineData("timeout")]
    [InlineData("body")]
    public async Task Transport_failures_and_timeouts_are_safe_unknowns_and_cached(string mode)
    {
        var handler = new Answers(async (_, token) =>
        {
            if (mode == "timeout") await Task.Delay(Timeout.Infinite, token);
            if (mode == "body") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new ForwardStream(null)) };
            throw new HttpRequestException(Key);
        });
        using var http = new HttpClient(handler);
        var updates = new ModPageUpdates(http, TimeProvider.System, TimeSpan.FromMilliseconds(100), Key);
        var result = await updates.CheckAsync(Page, "1.0").WaitAsync(TimeSpan.FromSeconds(5));
        AssertFailure(ModPageUpdateFailure.Unavailable, result);
        Assert.DoesNotContain(Key, result.ToString());
        AssertFailure(ModPageUpdateFailure.Unavailable, await updates.CheckAsync(Page, "1.0"));
        Assert.Equal(1, handler.Count);
        Assert.Equal(ModPageUpdateFailure.Unavailable, await updates.ValidateApiKeyAsync().WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Api_versions_cannot_echo_the_secret_into_results()
    {
        using var http = new HttpClient(new Answers((_, _) => Task.FromResult(Version("echo-" + Key))));
        var result = await new ModPageUpdates(http, Key).CheckAsync(Page, "1.0");
        AssertFailure(ModPageUpdateFailure.Unavailable, result);
        Assert.DoesNotContain(Key, result.ToString());
    }

    [Fact]
    public async Task Error_bodies_are_never_read()
    {
        using var http = new HttpClient(new Answers((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StreamContent(new ForwardStream(null))
        })));
        var updates = new ModPageUpdates(http, Key);
        AssertFailure(ModPageUpdateFailure.AccessDenied, await updates.CheckAsync(Page, "1.0").WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(ModPageUpdateFailure.AccessDenied, await updates.ValidateApiKeyAsync().WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Caller_cancellation_of_validation_throws_only_a_safe_cancellation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Answers(async (_, token) =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { throw new OperationCanceledException(Key, token); }
            return Json("{}");
        }));
        using var cancellation = new CancellationTokenSource();
        var pending = new ModPageUpdates(http, Key).ValidateApiKeyAsync(cancellation.Token);
        await started.Task;
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.DoesNotContain(Key, error.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deactivate_cancels_in_flight_and_queued_requests_and_permanently_rejects_the_old_instance(bool validationFirst)
    {
        var transportCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Answers(async (request, token) =>
        {
            if (request.Headers.GetValues("apikey").Single() != Key)
                return Json("{\"user_id\":1,\"version\":\"2.0\"}");
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException)
            {
                transportCancelled.TrySetResult();
                throw new OperationCanceledException(Key, token);
            }
            return Version("9.0");
        });
        using var http = new HttpClient(handler);
        var updates = new ModPageUpdates(http, Key);
        Task<ModPageUpdateFailure>? validation = validationFirst ? updates.ValidateApiKeyAsync() : null;
        var checks = Enumerable.Range(0, 3).Select(i => updates.CheckAsync(Page + i, "1.0")).ToArray();
        validation ??= updates.ValidateApiKeyAsync();
        Assert.Equal(1, handler.Count);
        updates.Deactivate();
        updates.Deactivate(); // Retirement is idempotent; do not dispose a semaphore with pending waiters.
        Assert.All(await Task.WhenAll(checks).WaitAsync(TimeSpan.FromSeconds(2)),
            result => AssertFailure(ModPageUpdateFailure.Unavailable, result));
        Assert.Equal(ModPageUpdateFailure.Unavailable, await validation.WaitAsync(TimeSpan.FromSeconds(2)));
        await transportCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        AssertFailure(ModPageUpdateFailure.Unavailable, await updates.CheckAsync(Page, "1.0"));
        Assert.Equal(ModPageUpdateFailure.Unavailable, await updates.ValidateApiKeyAsync());
        Assert.Equal(1, handler.Count);
        // A retired instance no longer retains the key, even if its transport is still unwinding.
        Assert.Null(typeof(ModPageUpdates).GetField("apiKey", System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance)!.GetValue(updates));
        var replacement = new ModPageUpdates(http, "changed-key");
        Assert.Equal(ModPageUpdateState.UpdateAvailable, (await replacement.CheckAsync(Page, "1.0")).State);
        Assert.Equal(ModPageUpdateFailure.None, await replacement.ValidateApiKeyAsync());
        Assert.Equal(3, handler.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deactivate_finishes_waiters_even_if_a_transport_ignores_cancellation(bool validationFirst)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Answers(async (_, _) =>
        {
            await release.Task;
            return Json("{\"user_id\":1,\"version\":\"2.0\"}");
        });
        using var http = new HttpClient(handler);
        var updates = new ModPageUpdates(http, Key);
        try
        {
            Task<ModPageUpdateFailure>? validation = validationFirst ? updates.ValidateApiKeyAsync() : null;
            var first = updates.CheckAsync(Page, "1.0");
            var queued = updates.CheckAsync(Page + "0", "1.0");
            validation ??= updates.ValidateApiKeyAsync();
            updates.Deactivate();
            AssertFailure(ModPageUpdateFailure.Unavailable, await first.WaitAsync(TimeSpan.FromSeconds(2)));
            AssertFailure(ModPageUpdateFailure.Unavailable, await queued.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(ModPageUpdateFailure.Unavailable, await validation.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(1, handler.Count);
        }
        finally { release.TrySetResult(); }
        AssertFailure(ModPageUpdateFailure.Unavailable, await updates.CheckAsync(Page, "1.0"));
        Assert.Equal(ModPageUpdateFailure.Unavailable, await updates.ValidateApiKeyAsync());
        Assert.Equal(1, handler.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Deactivate_overrides_cached_successes_and_specific_api_failures(HttpStatusCode status)
    {
        var handler = new Answers((_, _) => Task.FromResult(Json("{\"version\":\"2.0\"}", status)));
        using var http = new HttpClient(handler);
        var updates = new ModPageUpdates(http, Key);
        await updates.CheckAsync(Page, "1.0");
        updates.Deactivate();
        AssertFailure(ModPageUpdateFailure.Unavailable, await updates.CheckAsync(Page, "1.0"));
        Assert.Equal(ModPageUpdateFailure.Unavailable, await updates.ValidateApiKeyAsync());
        Assert.Equal(1, handler.Count);
    }

    private static void AssertFailure(ModPageUpdateFailure failure, ModPageUpdateResult result) =>
        Assert.Equal(new ModPageUpdateResult(ModPageUpdateState.Unknown, Failure: failure), result);

    private static HttpResponseMessage Version(string version) => Json(JsonSerializer.Serialize(new { version, available = true, status = "published" }));
    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

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
