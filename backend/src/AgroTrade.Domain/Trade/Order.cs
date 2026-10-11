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
    /// Rollup of the order's Payments.
    /// </summary>
    /// <remarks>
    /// Recomputed from the payment rows rather than tracked independently, so it cannot drift
    /// from the payments it summarises. Settling a payment re-derives it in the same unit of work.
    /// </remarks>
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.None;

    // ---- Trade terms, snapshotted from the quotation at handoff ----
    public string Currency { get; set; } = "USD";
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    /// <summary>
    /// Domestic delivery charge (Phase 4). Zero for export orders. Summed from the cart lines'
    /// listing delivery fees at checkout and included in <see cref="Total"/>; invoices and
    /// payment settlement already operate on Total, so the money flows exactly as it does for
    /// any other order.
    /// </summary>
    public decimal DeliveryFee { get; set; }
    public decimal Total { get; set; }
    public Incoterms Incoterms { get; set; }
    public string? PortOfLoading { get; set; }
    public string? PortOfDischarge { get; set; }
    public PaymentTerms PaymentTerms { get; set; }
    /// <summary>ISO-3166 alpha-2 destination country, carried from the RFQ.</summary>
    public string? DestinationCountry { get; set; }
    public ShipmentMode? TransportMode { get; set; }
    public Guid? DestinationAddressId { get; set; }

    /// <summary>
    /// Street-level delivery address snapshotted at domestic checkout (Phase 4). Held on the
    /// order rather than reached through <see cref="DestinationAddressId"/> so that the record
    /// of where goods were to be delivered cannot mutate or disappear when the buyer later
    /// edits their address book. Export orders continue to reference a saved address by FK and
    /// leave these null. <see cref="DestinationCountry"/> carries the snapshot's country.
    /// </summary>
    public string? DeliveryLine1 { get; set; }
    public string? DeliveryLine2 { get; set; }
    public string? DeliveryCity { get; set; }
    public string? DeliveryState { get; set; }
    public string? DeliveryPostalCode { get; set; }

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
    public List<Shipment> Shipments { get; set; } = [];

    public Quotation? Quotation { get; set; }
    public TradeRequest? TradeRequest { get; set; }
    public Business? SellerBusiness { get; set; }
    public Address? DestinationAddress { get; set; }
}