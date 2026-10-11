using AgroTrade.Domain.Common;

namespace AgroTrade.Domain.Trade;

/// <summary>
/// One line in a buyer's cart: the product and how many units. One line per product — adding
/// the same listing again increments it rather than duplicating the row.
/// </summary>
public sealed class CartItem : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CartId { get; set; }
    public Guid ProductId { get; set; }

    public decimal Quantity { get; set; }

    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public Cart? Cart { get; set; }
    public Product? Product { get; set; }
}