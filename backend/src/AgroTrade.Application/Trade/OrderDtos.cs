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

public sealed record OrderCardDto
{
    public Guid Id { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public OrderStatus Status { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public string Currency { get; init; } = string.Empty;
    public decimal Total { get; init; }
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

    public string Currency { get; init; } = string.Empty;
    public decimal Subtotal { get; init; }
    public decimal Discount { get; init; }
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

public sealed record PaymentDto
{
    public Guid Id { get; init; }
    public Guid OrderId { get; init; }
    public string Reference { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public PaymentProvider Provider { get; init; }
    public PaymentMethod Method { get; init; }
    public PaymentStatus Status { get; init; }
    public string? RemittanceReference { get; init; }
    public string? BankName { get; init; }
    public DateTime? ValueDateUtc { get; init; }
    public DateTime InitiatedAtUtc { get; init; }
    public DateTime? PaidAtUtc { get; init; }
    public string? FailureReason { get; init; }
    public string? Notes { get; init; }
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
}