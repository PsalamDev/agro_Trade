using System.ComponentModel.DataAnnotations;

namespace AgroTrade.Application.Trade;

// Domestic buy-now / cart (Phase 4). The cart holds product ids and quantities, never prices:
// every read resolves the listings' live terms, and checkout re-validates stock and domestic
// availability before snapshotting the agreed terms onto fresh orders. Nothing a shopper saw
// in a cart can misquote what an order actually charged.

public sealed record CartItemDto
{
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int PriceUnit { get; init; }
    public string Currency { get; init; } = "USD";
    public decimal Quantity { get; init; }
    /// <summary>Price × quantity for this line.</summary>
    public decimal LineTotal { get; init; }
    /// <summary>The listing's flat domestic delivery charge for this line (Phase 4).</summary>
    public decimal DeliveryFee { get; init; }
    public decimal AvailableQuantity { get; init; }
    public Guid SellerBusinessId { get; init; }
    public string SellerTradingName { get; init; } = string.Empty;
    public string? PrimaryImageUrl { get; init; }
    /// <summary>
    /// False when the listing changed after it was added — taken down, deactivated, suspended,
    /// no longer domestic, or short of stock — in which case checkout will refuse the line.
    /// The cart page uses it to flag the line and disable ordering until it is fixed.
    /// </summary>
    public bool IsAvailable { get; init; }
}

public sealed record CartDto
{
    public List<CartItemDto> Items { get; init; } = [];

    /// <summary>Sum of line quantities — the figure the header badge shows.</summary>
    public int TotalQuantity { get; init; }
}

public sealed record AddToCartRequest
{
    [Required]
    public Guid ProductId { get; init; }

    public decimal Quantity { get; init; } = 1;
}

/// <summary>Sets a line's quantity to an absolute value (never cumulative).</summary>
public sealed record SetCartItemQuantityRequest
{
    public decimal Quantity { get; init; } = 1;
}

public sealed record CheckoutAddressRequest
{
    [Required, MaxLength(200)]
    public string Line1 { get; init; } = string.Empty;

    [MaxLength(200)]
    public string? Line2 { get; init; }

    [Required, MaxLength(120)]
    public string City { get; init; } = string.Empty;

    [MaxLength(120)]
    public string? State { get; init; }

    /// <summary>ISO-3166 alpha-2. Must match the sellers' countries — "domestic" is fixed by this.</summary>
    [Required, MaxLength(2)]
    public string Country { get; init; } = string.Empty;

    [MaxLength(20)]
    public string? PostalCode { get; init; }
}

public sealed record CheckoutRequest
{
    public CheckoutAddressRequest Destination { get; init; } = new();

    [MaxLength(2000)]
    public string? Notes { get; init; }
}

public sealed record CheckoutOrderDto
{
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public Guid SellerBusinessId { get; init; }
    public string SellerTradingName { get; init; } = string.Empty;
    public string Currency { get; init; } = "USD";
    public decimal Subtotal { get; init; }
    public decimal DeliveryFee { get; init; }
    public decimal Total { get; init; }
    public int ItemCount { get; init; }
    public DateTime? ExpectedDeliveryUtc { get; init; }
}

/// <summary>One order per seller, because one order row carries one seller and one currency.</summary>
public sealed record CheckoutResultDto
{
    public List<CheckoutOrderDto> Orders { get; init; } = [];
}