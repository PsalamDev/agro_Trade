using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgroTrade.Infrastructure.BackgroundJobs;

/// <summary>
/// Base for the recurring maintenance workers. A pass runs inside a fresh DI scope, so scoped
/// collaborators — above all the <c>AgroTradeDbContext</c> — are created and disposed per pass
/// rather than living for the life of the process. A pass that throws is logged and retried on the
/// next interval; a worker that dies on its first transient database error is a worker no one
/// notices until the data has gone stale.
/// </summary>
/// <param name="scopeFactory">Creates the per-pass scope.</param>
/// <param name="options">Master switch and the per-job intervals.</param>
/// <param name="logger">Structured sink for pass outcomes and failures.</param>
public abstract class ScheduledBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<BackgroundJobsOptions> options,
    ILogger logger) : BackgroundService
{
    /// <summary>Stable name used in every log line this worker emits.</summary>
    protected abstract string JobName { get; }

    /// <summary>How long to wait between passes.</summary>
    protected abstract TimeSpan Interval { get; }

    /// <summary>The bound settings, so a subclass can derive its interval from configuration.</summary>
    protected BackgroundJobsOptions Settings => options.Value;

    /// <summary>
    /// Runs one pass against a provider scoped to that pass and returns how many rows/items it
    /// changed, for the completion log. Implementations must be idempotent: a pass may run at any
    /// time, including immediately after a previous one.
    /// </summary>
    protected abstract Task<int> ExecuteAsync(IServiceProvider services, CancellationToken ct);

    /// <summary>
    /// Runs a single pass in its own scope. The scheduled loop calls this, and it is public so an
    /// operator — or a test — can trigger a job on demand without waiting for the interval.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        return await ExecuteAsync(scope.ServiceProvider, ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Background worker {Job} is disabled.", JobName);
            return;
        }

        logger.LogInformation("Background worker {Job} started; interval {Interval}.", JobName, Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var affected = await RunOnceAsync(stoppingToken);
                logger.LogInformation("Background job {Job} completed; {Count} item(s) affected.", JobName, affected);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background job {Job} failed; it will retry in {Interval}.", JobName, Interval);
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
