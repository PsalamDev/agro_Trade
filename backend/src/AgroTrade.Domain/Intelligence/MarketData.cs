using AgroTrade.Domain.Common;

namespace AgroTrade.Domain.Intelligence;

/// <summary>
/// Commodity price series entry (append-only). Powers market intelligence charts in Phase 3.
/// </summary>
public sealed class MarketData : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Commodity name, e.g. "Cashew", "Cocoa", "Maize".</summary>
    public string Commodity { get; set; } = string.Empty;

    /// <summary>ISO-3166 alpha-2 country where price is reported.</summary>
    public string Country { get; set; } = string.Empty;

    /// <summary>Optional region/state/province, e.g. "Lagos", "Mombasa".</summary>
    public string? Region { get; set; }

    /// <summary>Price unit, e.g. "Ton", "Kg", "Bag".</summary>
    public string Unit { get; set; } = string.Empty;

    public decimal Price { get; set; }

    /// <summary>ISO-4217 currency.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Data source, e.g. "FAO", "ICCO", "Local Market", "Admin".</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Date/time the price was reported for.</summary>
    public DateTime ReportedAtUtc { get; set; }

    public string? Notes { get; set; }

    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}