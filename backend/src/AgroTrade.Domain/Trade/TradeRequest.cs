using AgroTrade.Domain.Common;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Domain.Trade;

/// <summary>
/// Importer sourcing request (RFQ): destination country + port, preferred incoterm,
/// required certifications, currency, deadline. Quotable by verified suppliers
/// (plan §02 — Trade requests).
/// </summary>
public sealed class TradeRequest : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Unique reference, e.g. "RFQ-2026-000001".</summary>
    public string RfqNumber { get; set; } = string.Empty;
    public Guid BuyerUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? CategoryId { get; set; }
    public decimal Quantity { get; set; }
    public PriceUnit Unit { get; set; }
    /// <summary>ISO-3166 alpha-2 delivery country — required.</summary>
    public string DestinationCountry { get; set; } = string.Empty;
    public string? DestinationPortOrCity { get; set; }
    public Incoterms? PreferredIncoterm { get; set; }
    public ShipmentMode? TransportMode { get; set; }
    public bool InsuranceRequired { get; set; }
    /// <summary>JSON object: grade, moisture, purity, ...</summary>
    public string? TargetSpecs { get; set; }
    /// <summary>JSON string array, e.g. ["Phytosanitary", "Certificate of Origin"].</summary>
    public string? CertificationsRequired { get; set; }
    public string PreferredCurrency { get; set; } = "USD";
    public PaymentTerms? PaymentTermsPreferred { get; set; }
    public DateTime? DeliveryDeadline { get; set; }
    public decimal? BudgetMin { get; set; }
    public decimal? BudgetMax { get; set; }
    public RfqStatus Status { get; set; } = RfqStatus.Open;
    public DateTime ExpiresAt { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public List<Quotation> Quotations { get; set; } = [];
}