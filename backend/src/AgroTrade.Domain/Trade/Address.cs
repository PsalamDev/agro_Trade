using AgroTrade.Domain.Common;

namespace AgroTrade.Domain.Trade;

/// <summary>
/// Polygonal owner address (user or business). Countries are ISO-3166 alpha-2.
/// </summary>
public sealed class Address : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? OwnerUserId { get; set; }
    public Guid? OwnerBusinessId { get; set; }
    public bool IsPrimary { get; set; }
    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string Country { get; set; } = string.Empty;
    public string? PostalCode { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}