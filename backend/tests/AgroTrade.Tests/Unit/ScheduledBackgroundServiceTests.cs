using AgroTrade.Infrastructure.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgroTrade.Tests.Unit;

/// <summary>
/// The shell every maintenance worker inherits: the disable switch, the per-pass scope, and the
/// promise that one failing pass does not silently end the worker.
/// </summary>
public sealed class ScheduledBackgroundServiceTests
{
    [Fact]
    public async Task Disabled_Worker_DoesNotRunTheJob()
    {
        var runs = 0;
        var worker = new TestWorker(
            Config(enabled: false),
            TimeSpan.FromMilliseconds(10),
            () => { Interlocked.Increment(ref runs); return Task.FromResult(1); });

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, runs);
    }

    [Fact]
    public async Task FailingPass_IsRetried_AndTheWorkerKeepsRunning()
    {
        var attempts = 0;
        var worker = new TestWorker(
            Config(enabled: true),
            TimeSpan.FromMilliseconds(5),
            () =>
            {
                var attempt = Interlocked.Increment(ref attempts);
                return attempt < 3
                    ? Task.FromException<int>(new InvalidOperationException("transient"))
                    : Task.FromResult(7);
            });

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => Volatile.Read(ref attempts) >= 3, TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.True(attempts >= 3, $"expected the worker to retry past its failures, saw {attempts} attempt(s).");
    }

    [Fact]
    public async Task RunOnce_ExecutesInAFreshScope_EvenWhenScheduledRunsAreDisabled()
    {
        var scopes = 0;
        var worker = new TestWorker(
            Config(enabled: false),
            TimeSpan.FromSeconds(30),
            () => Task.FromResult(0),
            onScope: () => Interlocked.Increment(ref scopes));

        // The master switch governs the timer loop, not an explicit run: an operator asking for a
        // pass should get one whether or not the schedule is on.
        Assert.Equal(0, await worker.RunOnceAsync(CancellationToken.None));
        Assert.Equal(1, scopes);
    }

    private static IOptions<BackgroundJobsOptions> Config(bool enabled) =>
        Options.Create(new BackgroundJobsOptions { Enabled = enabled });

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "condition was not met before the timeout.");
            await Task.Delay(20);
        }
    }

    private sealed class TestWorker(
        IOptions<BackgroundJobsOptions> options,
        TimeSpan interval,
        Func<Task<int>> work,
        Action? onScope = null)
        : ScheduledBackgroundService(new StubScopeFactory(onScope), options, NullLogger.Instance)
    {
        protected override string JobName => "test";

        protected override TimeSpan Interval => interval;

        protected override Task<int> ExecuteAsync(IServiceProvider services, CancellationToken ct) => work();
    }

    private sealed class StubScopeFactory(Action? onScope) : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            onScope?.Invoke();
            return new StubScope();
        }

        private sealed class StubScope : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; } = new ServiceCollection().BuildServiceProvider();

            public void Dispose()
            {
            }
        }
    }
}
