using AgroTrade.Domain.Common;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Domain.Trade;

/// <summary>
/// An order line. Product name, unit and price are snapshotted rather than referenced, so
/// an order keeps the terms that were agreed even if the listing is later renamed, repriced
/// or taken down (plan §02 — OrderItems).
/// </summary>
public sealed class OrderItem : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }

    /// <summary>Null for manual orders; nulled (not cascaded) if the product is deleted.</summary>
    public Guid? ProductId { get; set; }

    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string? ProductSkuSnapshot { get; set; }
    public decimal Quantity { get; set; }
    public PriceUnit Unit { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public string Currency { get; set; } = "USD";

    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public Order? Order { get; set; }
    public Product? Product { get; set; }
}