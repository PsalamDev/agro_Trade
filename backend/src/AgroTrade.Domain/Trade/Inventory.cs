using AgroTrade.Domain.Common;

namespace AgroTrade.Domain.Trade;

/// <summary>
/// Per-listing stock. Exactly one row per product (unique ProductId). Bookable stock is
/// OnHand - Reserved: orders hold a reservation while they are being prepared, so the same
/// units cannot be sold twice (plan §02 — Inventory).
///
/// <para>
/// Product.AvailableQuantity is a denormalised copy of OnHand - Reserved, kept in step by
/// <c>InventoryService</c> in the same transaction. It exists because the marketplace
/// displays and the seller edits stock without this join; it is never the source of truth.
/// </para>
/// </summary>
public sealed class Inventory : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProductId { get; set; }

    /// <summary>Physical stock the seller holds, including units reserved against live orders.</summary>
    public decimal QuantityOnHand { get; set; }

    /// <summary>Units held against orders that are placed or confirmed but not yet shipped.</summary>
    public decimal QuantityReserved { get; set; }

    /// <summary>Optimistic concurrency token. Reservations race when two buyers order the last lot.</summary>
    public byte[]? RowVersion { get; set; }

    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public Product? Product { get; set; }

    /// <summary>Stock that can still be ordered right now. Never negative.</summary>
    public decimal Available => QuantityOnHand - QuantityReserved;
}
