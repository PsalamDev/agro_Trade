using AgroTrade.Domain.Common;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Domain.Trade;

/// <summary>
/// A negotiated trade becomes an order. Created losslessly from an accepted quotation
/// (which carries the full trade terms) or by manual entry. Money, incoterms, ports and
/// payment terms are snapshotted here so later edits to the source RFQ or quotation can
/// never rewrite history (plan §02 — Orders).
/// </summary>
public sealed class Order : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Unique reference, e.g. "AG-2026-000001".</summary>
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>Set when the order was created from an accepted quotation.</summary>
    public Guid? QuotationId { get; set; }
    /// <summary>Set when the order descends from an RFQ, for traceability.</summary>
    public Guid? TradeRequestId { get; set; }

    public Guid BuyerUserId { get; set; }
    public Guid SellerBusinessId { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Draft;

    /// <summary>
    /// Rollup of the order's Payments. Stays <see cref="PaymentStatus.None"/> until task
    /// 2.5 gives payments an endpoint; the rollup rule is written alongside that endpoint
    /// so it lands with tests rather than as untested guesswork.
    /// </summary>
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.None;

    // ---- Trade terms, snapshotted from the quotation at handoff ----
    public string Currency { get; set; } = "USD";
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
    public Incoterms Incoterms { get; set; }
    public string? PortOfLoading { get; set; }
    public string? PortOfDischarge { get; set; }
    public PaymentTerms PaymentTerms { get; set; }
    /// <summary>ISO-3166 alpha-2 destination country, carried from the RFQ.</summary>
    public string? DestinationCountry { get; set; }
    public ShipmentMode? TransportMode { get; set; }
    public Guid? DestinationAddressId { get; set; }

    /// <summary>Agreed delivery date, from the RFQ deadline or the seller's lead time.</summary>
    public DateTime? ExpectedDeliveryUtc { get; set; }
    public DateTime? PlacedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public string? Notes { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public byte[]? RowVersion { get; set; }

    public List<OrderItem> Items { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
    public List<TradeDocument> Documents { get; set; } = [];

    public Quotation? Quotation { get; set; }
    public TradeRequest? TradeRequest { get; set; }
    public Business? SellerBusiness { get; set; }
    public Address? DestinationAddress { get; set; }
}