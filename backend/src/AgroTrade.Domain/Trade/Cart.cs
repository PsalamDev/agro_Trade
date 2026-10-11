using AgroTrade.Domain.Common;

namespace AgroTrade.Domain.Trade;

/// <summary>
/// A buyer's shopping cart (Phase 4 — domestic buy-now). One row per user; lines hold a
/// product id and a quantity, never a price. Prices change and stock moves, so the cart is
/// re-validated and snapshotted onto orders at checkout rather than storing terms a buyer
/// could point at later ("it said 400 in my cart" is not a constructive dispute).
/// </summary>
public sealed class Cart : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public List<CartItem> Items { get; set; } = [];
}