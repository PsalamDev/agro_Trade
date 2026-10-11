namespace AgroTrade.Domain.Trade;

/// <summary>
/// One country a <see cref="LogisticsProvider"/> will serve (Phase 4 — LSP directory).
/// </summary>
public sealed class LogisticsProviderCountry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LogisticsProviderId { get; set; }

    /// <summary>ISO-3166 alpha-2 country code.</summary>
    public string Country { get; set; } = string.Empty;
}
