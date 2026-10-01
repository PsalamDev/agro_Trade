using AgroTrade.Application.Common;

namespace AgroTrade.Application.Trade;

/// <summary>
/// Stock reservation for orders (plan §02 — Inventory). Bookable stock is OnHand minus
/// Reserved: placing an order holds its units, confirming consumes them, cancelling
/// releases them.
///
/// <para>
/// The core operations take quantities keyed by product rather than an order id, so a caller
/// can reserve stock in the same change set that creates the order. Every stock write in the
/// system goes through here, which is what keeps <c>Product.AvailableQuantity</c> — a
/// denormalised mirror kept for the marketplace — from drifting.
/// </para>
/// </summary>
public interface IInventoryService
{
    /// <summary>
    /// Holds stock for each product listed. Throws <c>INSUFFICIENT_STOCK</c> if any listing
    /// cannot cover its quantity, and <c>STOCK_NOT_SET</c> if a listing has no stock level.
    /// Does not save — the caller commits it alongside whatever else it is writing.
    /// </summary>
    Task ReserveAsync(IReadOnlyDictionary<Guid, decimal> wantedByProduct, CancellationToken ct = default);

    /// <summary>
    /// Consumes reserved stock: the goods are committed to ship, so they leave both OnHand
    /// and Reserved. Called when the seller confirms an order.
    /// </summary>
    Task CommitAsync(IReadOnlyDictionary<Guid, decimal> wantedByProduct, CancellationToken ct = default);

    /// <summary>Releases a reservation without shipping. Called when an order is cancelled.</summary>
    Task ReleaseAsync(IReadOnlyDictionary<Guid, decimal> wantedByProduct, CancellationToken ct = default);

    /// <summary>
    /// Per-product quantities an order touches, excluding lines with no product. An order
    /// raised from a quotation describes a consignment rather than a catalog listing, so it
    /// holds nothing; two lines for the same product are summed.
    /// </summary>
    IReadOnlyDictionary<Guid, decimal> QuantitiesFor(IEnumerable<(Guid? ProductId, decimal Quantity)> lines);

    /// <summary>
    /// Seller-declared stock level for a listing. Sets OnHand and clamps Reserved down if the
    /// new level is below what live orders already hold.
    /// </summary>
    Task<InventoryDto> SetStockAsync(Guid productId, Guid ownerUserId, SetStockRequest request, CancellationToken ct = default);

    /// <summary>Current stock for a listing.</summary>
    Task<InventoryDto> GetAsync(Guid productId, CancellationToken ct = default);
}
