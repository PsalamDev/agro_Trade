namespace AgroTrade.Infrastructure.BackgroundJobs;

/// <summary>
/// Scheduling knobs for the recurring maintenance workers (plan §15 — background workers).
/// Intervals are minutes. The defaults are deliberately gentle: one node runs every worker,
/// and these are idempotent sweeps, not latency-sensitive work.
/// </summary>
public sealed class BackgroundJobsOptions
{
    public const string SectionName = "BackgroundJobs";

    /// <summary>
    /// Master switch. Integration tests turn it off — the test host starts the workers too, and a
    /// sweep firing in the middle of an assertion would make the suite non-deterministic.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often overdue RFQs are swept to <c>Expired</c>.</summary>
    public int RfqExpiryIntervalMinutes { get; set; } = 15;

    /// <summary>How often refresh tokens that can no longer be presented are deleted.</summary>
    public int RefreshTokenCleanupIntervalMinutes { get; set; } = 720;
}
