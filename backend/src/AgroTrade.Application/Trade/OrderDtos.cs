using AgroTrade.Domain.Enums;

namespace AgroTrade.Application.Trade;

// Orders are created from an accepted quotation (task 2.1). DTOs use the same int-backed
// enum shape as ProductDtos so the contract stays enum-agnostic to the client.

public sealed record CreateOrderFromQuotationRequest
{
    /// <summary>Quotation the buyer is converting. Must be Accepted and not already converted.</summary>
    public Guid QuotationId { get; init; }

    /// <summary>Delivery address for this shipment. Defaults to the buyer's address of record.</summary>
    public Guid? DestinationAddressId { get; init; }

    public string? Notes { get; init; }

    /// <summary>Send the order straight to Placed rather than leaving it as a Draft.</summary>
    public bool PlaceImmediately { get; init; } = true;
}

public sealed record CreateManualOrderRequest
{
    public Guid SellerBusinessId { get; init; }
    public string Currency { get; init; } = "USD";
    public int Incoterms { get; init; }
    public int PaymentTerms { get; init; }
    public string? PortOfLoading { get; init; }
    public string? PortOfDischarge { get; init; }
    public string? DestinationCountry { get; init; }
    public int? TransportMode { get; init; }
    public Guid? DestinationAddressId { get; init; }
    public DateTime? ExpectedDeliveryUtc { get; init; }
    public decimal Discount { get; init; }
    public string? Notes { get; init; }
    public List<CreateOrderItemRequest> Items { get; init; } = [];
}

public sealed record CreateOrderItemRequest
{
    public Guid? ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? Sku { get; init; }
    public decimal Quantity { get; init; }
    public int Unit { get; init; }
    public decimal UnitPrice { get; init; }
}

public sealed record OrderListQuery
{
    public string? Status { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

/// <summary>
/// Moves an order to the next state. Who may perform which transition is enforced by the
/// service: the buyer places and confirms receipt, the seller confirms and dispatches, and
/// either party may cancel until the goods are in transit.
/// </summary>
public sealed record OrderTransitionRequest
{
    public OrderStatus To { get; init; }

    /// <summary>Reason, recorded against the order. Required when cancelling.</summary>
    public string? Reason { get; init; }
}

public sealed record OrderCardDto
{
    public Guid Id { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public OrderStatus Status { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public string Currency { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public decimal DeliveryFee { get; init; }
    public string SellerTradingName { get; init; } = string.Empty;
    public string BuyerDisplayName { get; init; } = string.Empty;
    public Incoterms Incoterms { get; init; }
    public string? PortOfLoading { get; init; }
    public string? PortOfDischarge { get; init; }
    public int ItemCount { get; init; }
    public DateTime? ExpectedDeliveryUtc { get; init; }
    public DateTime? PlacedAtUtc { get; init; }
    public DateTime? CreatedAtUtc { get; init; }
}

public sealed record OrderDetailDto
{
    public Guid Id { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public Guid? QuotationId { get; init; }
    public Guid? TradeRequestId { get; init; }
    public string? RfqNumber { get; init; }

    public OrderStatus Status { get; init; }
    public PaymentStatus PaymentStatus { get; init; }

    public Guid BuyerUserId { get; init; }
    public string BuyerDisplayName { get; init; } = string.Empty;
    public Guid SellerBusinessId { get; init; }
    public string SellerTradingName { get; init; } = string.Empty;

    // Which side the caller is on, resolved from actual business ownership rather than from the
    // Company role. Carried on the DTO so the detail page can render the right actions without
    // fetching the caller's businesses on every load.
    public bool IsBuyer { get; init; }
    public bool IsSeller { get; init; }

    public string Currency { get; init; } = string.Empty;
    public decimal Subtotal { get; init; }
    public decimal Discount { get; init; }
    /// <summary>Domestic delivery charge; zero on export orders (Phase 4).</summary>
    public decimal DeliveryFee { get; init; }
    public decimal Total { get; init; }
    public decimal AmountPaid { get; init; }
    public decimal BalanceDue { get; init; }

    public Incoterms Incoterms { get; init; }
    public string? PortOfLoading { get; init; }
    public string? PortOfDischarge { get; init; }
    public PaymentTerms PaymentTerms { get; init; }
    public string? DestinationCountry { get; init; }
    public ShipmentMode? TransportMode { get; init; }
    public Guid? DestinationAddressId { get; init; }

    public DateTime? ExpectedDeliveryUtc { get; init; }
    public DateTime? PlacedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public DateTime? CreatedAtUtc { get; init; }
    public DateTime? UpdatedAtUtc { get; init; }
    public string? Notes { get; init; }

    public List<OrderItemDto> Items { get; init; } = [];
    public List<PaymentDto> Payments { get; init; } = [];
    public List<TradeDocumentDto> Documents { get; init; } = [];
}

public sealed record OrderItemDto
{
    public Guid Id { get; init; }
    public Guid? ProductId { get; init; }
    public string ProductNameSnapshot { get; init; } = string.Empty;
    public string? ProductSkuSnapshot { get; init; }
    public decimal Quantity { get; init; }
    public PriceUnit Unit { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal LineTotal { get; init; }
    public string Currency { get; init; } = string.Empty;
}

public sealed record TradeDocumentDto
{
    public Guid Id { get; init; }
    public Guid OrderId { get; init; }
    public DocumentType DocType { get; init; }
    public string? DocumentReference { get; init; }
    public string? FileUrl { get; init; }
    public string? FileName { get; init; }
    public long? FileSizeBytes { get; init; }
    public string? ContentType { get; init; }
    public DocumentStatus Status { get; init; }
    public string? ReviewNotes { get; init; }
    public Guid? ReviewedByUserId { get; init; }
    public DateTime? ReviewedAtUtc { get; init; }
    public DateTime? IssuedOnUtc { get; init; }
    public DateTime? ExpiresOnUtc { get; init; }
    public DateTime? CreatedAtUtc { get; init; }

    // ---- Context the queue and the detail page need, added in task 2.3 ----

    /// <summary>Order number, so a document is identifiable in an admin list without a second lookup.</summary>
    public string OrderNumber { get; init; } = string.Empty;

    public string? OrderCounterparty { get; init; }
    public Guid? UploadedByUserId { get; init; }
    public string? UploadedByDisplayName { get; init; }
}

/// <summary>Admin review queue filter. Nulls mean "any".</summary>
public sealed class TradeDocumentSearchQuery
{
    public DocumentStatus? Status { get; init; }
    public DocumentType? DocType { get; init; }
    public string? Q { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record UploadTradeDocumentRequest
{
    public DocumentType DocType { get; init; }
    /// <summary>Reference printed on the paper, e.g. "INV-2026-0042".</summary>
    public string? DocumentReference { get; init; }
    public DateTime? IssuedOnUtc { get; init; }
    public DateTime? ExpiresOnUtc { get; init; }

    /// <summary>File metadata, taken from the validated upload rather than the form fields.</summary>
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = "application/octet-stream";
}

public sealed record ReviewDocumentRequest
{
    /// <summary>Verified or Rejected.</summary>
    public DocumentStatus Status { get; init; }
    /// <summary>Required when rejecting: the uploader needs to know what to fix.</summary>
    public string? Notes { get; init; }
}