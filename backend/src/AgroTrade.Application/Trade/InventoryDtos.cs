namespace AgroTrade.Application.Trade;

public sealed record SetStockRequest
{
    /// <summary>
    /// Stock the seller physically holds, including anything already reserved against live
    /// orders. Cannot be negative.
    /// </summary>
    public decimal QuantityOnHand { get; init; }
}

public sealed record InventoryDto
{
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal QuantityOnHand { get; init; }

    /// <summary>Held against placed or confirmed orders that have not shipped.</summary>
    public decimal QuantityReserved { get; init; }

    /// <summary>OnHand minus Reserved: what a buyer can still order right now.</summary>
    public decimal Available { get; init; }
}
