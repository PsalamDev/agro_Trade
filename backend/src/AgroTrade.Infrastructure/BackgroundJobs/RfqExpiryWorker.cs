using AgroTrade.Application.Trade;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgroTrade.Infrastructure.BackgroundJobs;

/// <summary>
/// Sweeps overdue RFQs on a schedule. The RFQ service also expires them lazily whenever a list or a
/// detail is read, which means an RFQ no one looks at keeps its <c>Open</c> badge — and its buyer
/// is never told — until someone happens to browse. This worker makes the deadline real on its own,
/// and shares <see cref="IRfqService.ExpireOverdueAsync"/> so there is exactly one definition of
/// "expired".
/// </summary>
public sealed class RfqExpiryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<BackgroundJobsOptions> options,
    ILogger<RfqExpiryWorker> logger)
    : ScheduledBackgroundService(scopeFactory, options, logger)
{
    protected override string JobName => "rfq-expiry";

    protected override TimeSpan Interval =>
        TimeSpan.FromMinutes(Math.Max(1, Settings.RfqExpiryIntervalMinutes));

    protected override Task<int> ExecuteAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<IRfqService>().ExpireOverdueAsync(ct);
}
