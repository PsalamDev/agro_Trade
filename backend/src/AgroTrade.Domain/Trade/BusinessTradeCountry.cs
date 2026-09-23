using AgroTrade.Domain.Enums;

namespace AgroTrade.Domain.Trade;

/// <summary>
/// What a business trades and where — the backbone of cross-border reachability.
/// Unique per (BusinessId, Direction, Country); powers "exporters that export to NG"
/// marketplace filters (plan §02).
/// </summary>
public sealed class BusinessTradeCountry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }
    public TradeDirection Direction { get; set; }
    /// <summary>ISO-3166 alpha-2 destination/origin country code.</summary>
    public string Country { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
}