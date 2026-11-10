using System.Net;
using System.Text;
using System.Threading.Channels;
using UEManager.Core.Updates;

namespace UEManager.Core.Tests;

public class NexusApiTimeoutTests
{
    private const string SyntheticKey = "synthetic-timeout-test-key";
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(12);
    // Real time is only a deadlock guard, never the clock used by a request-budget assertion.
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Three_serialized_mods_each_receive_their_own_budget_and_cache_remote_versions()
    {
        using var test = new Fixture();
        var checks = Enumerable.Range(1, 3).Select(id => test.Updates.CheckAsync(Page(id), "1.0")).ToArray();

        AssertBudget(test, 1, TimeSpan.Zero); // Queued checks must not create timers.
        for (var i = 0; i < checks.Length; i++)
        {
            var request = await test.Handler.NextAsync();
            Assert.EndsWith($"/mods/{i + 1}.json", request.Url);
            AssertBudget(test, i + 1, TimeSpan.FromSeconds(i * 5));
            test.Clock.Advance(TimeSpan.FromSeconds(5));
            Assert.False(request.Token.IsCancellationRequested);
            request.Complete(Json(System.Text.Json.JsonSerializer.Serialize(new { version = $"{i + 2}.0" })));
            Assert.Equal(new ModPageUpdateResult(ModPageUpdateState.UpdateAvailable, $"{i + 2}.0"),
                await checks[i].WaitAsync(Guard));
        }

        Assert.Equal(TimeSpan.FromSeconds(15), test.Clock.Elapsed);
        for (var id = 1; id <= 3; id++)
        {
            Assert.Equal(new ModPageUpdateResult(ModPageUpdateState.UpToDate, $"{id + 1}.0"),
                await test.Updates.CheckAsync(Page(id), $"{id + 1}.0").WaitAsync(Guard));
            Assert.Equal(new ModPageUpdateResult(ModPageUpdateState.UpdateAvailable, $"{id + 1}.0"),
                await test.Updates.CheckAsync(Page(id), "0.1").WaitAsync(Guard));
        }
        Assert.Equal(3, test.Handler.Count);
        Assert.Equal(3, test.Clock.CreatedTimerCount);
        Assert.Equal(0, test.Clock.ActiveTimerCount);
    }

    [Fact]
    public async Task Validation_can_queue_longer_than_its_budget_and_then_use_a_full_request_budget()
    {
        using var test = new Fixture();
        var checks = Enumerable.Range(1, 3).Select(id => test.Updates.CheckAsync(Page(id), "1.0")).ToArray();
        var validation = test.Updates.ValidateApiKeyAsync();

        AssertBudget(test, 1, TimeSpan.Zero); // Queued validation must not create a timer either.
        foreach (var check in checks)
        {
            var request = await test.Handler.NextAsync();
            Assert.Contains("/mods/", request.Url);
            AssertBudget(test, test.Handler.Count, test.Clock.Elapsed);
            test.Clock.Advance(TimeSpan.FromSeconds(5));
            request.Complete(Json("""{"version":"2.0"}"""));
            Assert.Equal(ModPageUpdateState.UpdateAvailable, (await check.WaitAsync(Guard)).State);
        }

        var validateRequest = await test.Handler.NextAsync();
        Assert.Equal("https://api.nexusmods.com/v1/users/validate.json", validateRequest.Url);
        Assert.Equal(TimeSpan.FromSeconds(15), test.Clock.Elapsed);
        AssertBudget(test, 4, TimeSpan.FromSeconds(15));
        test.Clock.Advance(TimeSpan.FromSeconds(11));
        Assert.False(validateRequest.Token.IsCancellationRequested);
        Assert.False(validation.IsCompleted);
        validateRequest.Complete(Json("""{"user_id":123}"""));
        Assert.Equal(ModPageUpdateFailure.None, await validation.WaitAsync(Guard));
        Assert.Equal(4, test.Handler.Count);
    }

    [Fact]
    public async Task Cancelling_queued_validation_never_dispatches_or_cancels_current_and_next_mods()
    {
        using var test = new Fixture();
        using var caller = new CancellationTokenSource();
        var first = test.Updates.CheckAsync(Page(1), "1.0");
        var current = await test.Handler.NextAsync();
        var validation = test.Updates.ValidateApiKeyAsync(caller.Token);
        var next = test.Updates.CheckAsync(Page(2), "1.0");
        AssertBudget(test, 1, TimeSpan.Zero);

        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => validation.WaitAsync(Guard));
        Assert.Equal(1, test.Handler.Count);
        test.Clock.Advance(TimeSpan.FromSeconds(11));
        Assert.False(current.Token.IsCancellationRequested);
        current.Complete(Json("""{"version":"2.0"}"""));
        Assert.Equal(ModPageUpdateState.UpdateAvailable, (await first.WaitAsync(Guard)).State);

        var nextRequest = await test.Handler.NextAsync();
        Assert.EndsWith("/mods/2.json", nextRequest.Url);
        test.Clock.Advance(TimeSpan.FromSeconds(11));
        Assert.False(nextRequest.Token.IsCancellationRequested);
        nextRequest.Complete(Json("""{"version":"3.0"}"""));
        Assert.Equal(new ModPageUpdateResult(ModPageUpdateState.UpdateAvailable, "3.0"),
            await next.WaitAsync(Guard));
        Assert.Equal(2, test.Handler.Count);
    }

    [Fact]
    public async Task Deactivation_releases_all_queued_requests_without_any_later_http_dispatch()
    {
        using var test = new Fixture();
        var first = test.Updates.CheckAsync(Page(1), "1.0");
        var current = await test.Handler.NextAsync();
        var second = test.Updates.CheckAsync(Page(2), "1.0");
        var third = test.Updates.CheckAsync(Page(3), "1.0");
        var validation = test.Updates.ValidateApiKeyAsync();
        Assert.Equal(1, test.Handler.Count);

        test.Updates.Deactivate();
        Assert.True(current.Token.IsCancellationRequested);
        foreach (var result in await Task.WhenAll(first, second, third).WaitAsync(Guard))
            AssertUnavailable(result);
        Assert.Equal(ModPageUpdateFailure.Unavailable, await validation.WaitAsync(Guard));
        Assert.Equal(1, test.Handler.Count);

        // The non-cooperative active handler may finish late; it must not restart the retired queue.
        current.Complete(Json("""{"version":"9.0"}"""));
        test.Clock.Advance(TimeSpan.FromMinutes(20));
        AssertUnavailable(await test.Updates.CheckAsync(Page(4), "1.0").WaitAsync(Guard));
        Assert.Equal(ModPageUpdateFailure.Unavailable, await test.Updates.ValidateApiKeyAsync().WaitAsync(Guard));
        Assert.Equal(1, test.Handler.Count);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Active_headers_and_body_get_exactly_one_full_budget_after_queueing(
        bool validation, bool blockBody)
    {
        using var test = new Fixture();
        using var body = new BlockedStream();
        var first = test.Updates.CheckAsync(Page(1), "1.0");
        var firstRequest = await test.Handler.NextAsync();
        var queuedCheck = validation ? null : test.Updates.CheckAsync(Page(2), "1.0");
        var queuedValidation = validation ? test.Updates.ValidateApiKeyAsync() : null;
        AssertBudget(test, 1, TimeSpan.Zero);

        test.Clock.Advance(TimeSpan.FromSeconds(10));
        firstRequest.Complete(Json("""{"version":"2.0"}"""));
        Assert.Equal(ModPageUpdateState.UpdateAvailable, (await first.WaitAsync(Guard)).State);
        var active = await test.Handler.NextAsync();
        AssertBudget(test, 2, TimeSpan.FromSeconds(10));
        Assert.Equal(validation ? "https://api.nexusmods.com/v1/users/validate.json"
            : "https://api.nexusmods.com/v1/games/game/mods/2.json", active.Url);
        if (blockBody)
        {
            active.Complete(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) });
            await body.Started.WaitAsync(Guard);
        }

        test.Clock.Advance(TimeSpan.FromSeconds(11));
        Assert.False(blockBody ? body.ReadToken.IsCancellationRequested : active.Token.IsCancellationRequested);
        Assert.False(validation ? queuedValidation!.IsCompleted : queuedCheck!.IsCompleted);
        test.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(blockBody ? body.ReadToken.IsCancellationRequested : active.Token.IsCancellationRequested);
        Assert.Equal(TimeSpan.FromSeconds(22), test.Clock.Elapsed);
        if (validation)
            Assert.Equal(ModPageUpdateFailure.Unavailable, await queuedValidation!.WaitAsync(Guard));
        else
        {
            AssertUnavailable(await queuedCheck!.WaitAsync(Guard));
            AssertUnavailable(await test.Updates.CheckAsync(Page(2), "9.0").WaitAsync(Guard));
        }
        Assert.Equal(2, test.Handler.Count);

        // Ignored cancellation must not hold the API semaphore or turn a cached timeout into success.
        body.Finish();
        active.Complete(Json("""{"user_id":123,"version":"99.0"}"""));
        var following = test.Updates.CheckAsync(Page(3), "1.0");
        var followingRequest = await test.Handler.NextAsync();
        Assert.EndsWith("/mods/3.json", followingRequest.Url);
        followingRequest.Complete(Json("""{"version":"3.0"}"""));
        Assert.Equal(new ModPageUpdateResult(ModPageUpdateState.UpdateAvailable, "3.0"),
            await following.WaitAsync(Guard));
        if (!validation)
            AssertUnavailable(await test.Updates.CheckAsync(Page(2), "1.0").WaitAsync(Guard));
        Assert.Equal(3, test.Handler.Count);
    }

    [Fact]
    public async Task Cancelling_one_check_waiter_preserves_the_shared_request_and_cached_version()
    {
        using var test = new Fixture();
        using var caller = new CancellationTokenSource();
        var cancelledWaiter = test.Updates.CheckAsync(Page(1), "1.0", caller.Token);
        var request = await test.Handler.NextAsync();
        var sharedWaiter = test.Updates.CheckAsync(Page(1), "2.0");
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledWaiter.WaitAsync(Guard));

        test.Clock.Advance(TimeSpan.FromSeconds(11));
        Assert.False(request.Token.IsCancellationRequested);
        Assert.False(sharedWaiter.IsCompleted);
        request.Complete(Json("""{"version":"2.0"}"""));
        Assert.Equal(new ModPageUpdateResult(ModPageUpdateState.UpToDate, "2.0"),
            await sharedWaiter.WaitAsync(Guard));
        Assert.Equal(new ModPageUpdateResult(ModPageUpdateState.UpdateAvailable, "2.0"),
            await test.Updates.CheckAsync(Page(1), "1.0").WaitAsync(Guard));
        Assert.Equal(1, test.Handler.Count);
    }

    [Fact]
    public async Task Html_requests_still_start_their_budget_immediately_and_cache_timeout_as_unknown()
    {
        using var test = new Fixture(api: false);
        var check = test.Updates.CheckAsync(Page(1), "1.0");
        var request = await test.Handler.NextAsync();
        Assert.Equal(Page(1), request.Url);
        test.Clock.Advance(TimeSpan.FromSeconds(11));
        Assert.False(request.Token.IsCancellationRequested);
        test.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(request.Token.IsCancellationRequested);
        Assert.Equal(new ModPageUpdateResult(ModPageUpdateState.Unknown), await check.WaitAsync(Guard));
        Assert.Equal(new ModPageUpdateResult(ModPageUpdateState.Unknown),
            await test.Updates.CheckAsync(Page(1), "1.0").WaitAsync(Guard));
        Assert.Equal(1, test.Handler.Count);
    }

    private static void AssertBudget(Fixture test, int dispatched, TimeSpan startedAt)
    {
        Assert.Equal(dispatched, test.Clock.CreatedTimerCount);
        Assert.Equal(1, test.Clock.ActiveTimerCount);
        Assert.Equal<TimeSpan>([startedAt + Budget], test.Clock.ActiveTimerDueTimes);
    }

    private static string Page(int id) => $"https://www.nexusmods.com/game/mods/{id}";
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
    private static void AssertUnavailable(ModPageUpdateResult result) =>
        Assert.Equal(new ModPageUpdateResult(ModPageUpdateState.Unknown, Failure: ModPageUpdateFailure.Unavailable), result);

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient http;
        public ManualClock Clock { get; } = new();
        public ControlledHandler Handler { get; } = new();
        public ModPageUpdates Updates { get; }

        public Fixture(bool api = true)
        {
            http = new HttpClient(Handler) { Timeout = Timeout.InfiniteTimeSpan };
            Updates = new ModPageUpdates(http, Clock, Budget, api ? SyntheticKey : null);
        }

        public void Dispose()
        {
            Updates.Deactivate();
            http.Dispose();
        }
    }

    private sealed class PendingRequest(string url, CancellationToken token)
    {
        private readonly TaskCompletionSource<HttpResponseMessage> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Url { get; } = url;
        public CancellationToken Token { get; } = token;
        public Task<HttpResponseMessage> Response => completion.Task;
        public void Complete(HttpResponseMessage response)
        {
            if (!completion.TrySetResult(response)) response.Dispose();
        }
    }

    // Deliberately ignores cancellation: production must bound headers even for a bad transport.
    private sealed class ControlledHandler : HttpMessageHandler
    {
        private readonly Channel<PendingRequest> arrivals = Channel.CreateUnbounded<PendingRequest>();
        private readonly List<PendingRequest> pending = [];
        private int count;
        public int Count => Volatile.Read(ref count);
        public Task<PendingRequest> NextAsync() => arrivals.Reader.ReadAsync().AsTask().WaitAsync(Guard);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var item = new PendingRequest(request.RequestUri!.AbsoluteUri, cancellationToken);
            lock (pending) pending.Add(item);
            Interlocked.Increment(ref count);
            if (!arrivals.Writer.TryWrite(item)) throw new InvalidOperationException("Test channel closed");
            return item.Response;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (pending)
                    foreach (var item in pending) item.Complete(Json("""{}"""));
                arrivals.Writer.TryComplete();
            }
            base.Dispose(disposing);
        }
    }

    // Body reads also ignore cancellation; the outer budget must bound the whole response.
    private sealed class BlockedStream : Stream
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<int> finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Started => started.Task;
        public CancellationToken ReadToken { get; private set; }
        public void Finish() => finished.TrySetResult(0);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadToken = cancellationToken;
            started.TrySetResult();
            return new ValueTask<int>(finished.Task);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) Finish();
            base.Dispose(disposing);
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

    // No timer dependency or wall-clock sleeps. Advance fires due callbacks synchronously, outside
    // the clock lock; tests await HTTP/body arrival before advancing so each budget is installed.
    private sealed class ManualClock : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ManualTimer> timers = [];
        private readonly DateTimeOffset origin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private long ticks;
        private int createdTimerCount;
        public int CreatedTimerCount { get { lock (gate) return createdTimerCount; } }
        public int ActiveTimerCount { get { lock (gate) return timers.Count(timer => timer.DueTicks != long.MaxValue); } }
        public TimeSpan[] ActiveTimerDueTimes
        {
            get
            {
                lock (gate) return timers.Where(timer => timer.DueTicks != long.MaxValue)
                    .Select(timer => TimeSpan.FromTicks(timer.DueTicks)).OrderBy(due => due).ToArray();
            }
        }
        public TimeSpan Elapsed { get { lock (gate) return TimeSpan.FromTicks(ticks); } }
        public override DateTimeOffset GetUtcNow() => origin + Elapsed;
        public override long GetTimestamp() { lock (gate) return ticks; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            lock (gate)
            {
                createdTimerCount++;
                timers.Add(timer);
                timer.Change(dueTime, period);
            }
            return timer;
        }

        public void Advance(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
            long target;
            lock (gate) target = checked(ticks + elapsed.Ticks);
            while (true)
            {
                ManualTimer? due;
                lock (gate)
                {
                    due = timers.Where(timer => timer.DueTicks <= target && timer.DueTicks != long.MaxValue)
                        .OrderBy(timer => timer.DueTicks).FirstOrDefault();
                    if (due is null) { ticks = target; return; }
                    ticks = due.DueTicks;
                    due.DueTicks = due.PeriodTicks > 0 ? checked(ticks + due.PeriodTicks) : long.MaxValue;
                }
                due.Fire();
            }
        }

        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
        {
            private bool disposed;
            public long DueTicks { get; set; } = long.MaxValue;
            public long PeriodTicks { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (dueTime < TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan)
                    throw new ArgumentOutOfRangeException(nameof(dueTime));
                if (period < TimeSpan.Zero && period != Timeout.InfiniteTimeSpan)
                    throw new ArgumentOutOfRangeException(nameof(period));
                lock (clock.gate)
                {
                    if (disposed) return false;
                    DueTicks = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : checked(clock.ticks + dueTime.Ticks);
                    PeriodTicks = period > TimeSpan.Zero ? period.Ticks : 0;
                    return true;
                }
            }
            public void Fire() => callback(state);
            public void Dispose()
            {
                lock (clock.gate)
                {
                    disposed = true;
                    DueTicks = long.MaxValue;
                    clock.timers.Remove(this);
                }
            }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
