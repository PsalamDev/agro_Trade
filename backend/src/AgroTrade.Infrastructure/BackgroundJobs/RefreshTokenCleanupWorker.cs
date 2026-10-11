using AgroTrade.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgroTrade.Infrastructure.BackgroundJobs;

/// <summary>
/// Deletes refresh tokens that can no longer be presented. Only <i>expired</i> rows are removed: a
/// revoked-but-still-live row is kept so that replaying a rotated token is still recognised and its
/// session family is still revoked (see <c>AuthService.RefreshAsync</c>). Deleting those early
/// would turn a detectable replay into a plain "invalid token".
/// </summary>
public sealed class RefreshTokenCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<BackgroundJobsOptions> options,
    ILogger<RefreshTokenCleanupWorker> logger)
    : ScheduledBackgroundService(scopeFactory, options, logger)
{
    protected override string JobName => "refresh-token-cleanup";

    protected override TimeSpan Interval =>
        TimeSpan.FromMinutes(Math.Max(1, Settings.RefreshTokenCleanupIntervalMinutes));

    protected override async Task<int> ExecuteAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AgroTradeDbContext>();
        var now = DateTime.UtcNow;
        return await db.RefreshTokens.Where(t => t.ExpiresAtUtc < now).ExecuteDeleteAsync(ct);
    }
}
