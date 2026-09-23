using AgroTrade.Domain.Common;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Domain.Trade;

/// <summary>
/// An exporter's offer against an RFQ with complete trade terms (incoterm, ports,
/// lead time, payment terms, currency). One active quote per supplier per RFQ —
/// unique (TradeRequestId, SupplierBusinessId) (plan §02).
/// </summary>
public sealed class Quotation : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TradeRequestId { get; set; }
    public Guid SupplierBusinessId { get; set; }
    public QuotationStatus Status { get; set; } = QuotationStatus.Submitted;
    public string Currency { get; set; } = "USD";
    public Incoterms Incoterms { get; set; }
    public string? PortOfLoading { get; set; }
    public string? PortOfDischarge { get; set; }
    public int LeadTimeDays { get; set; }
    public PaymentTerms PaymentTerms { get; set; }
    public decimal Total { get; set; }
    public string? Notes { get; set; }
    public DateTime? ValidUntil { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public byte[]? RowVersion { get; set; }

    public TradeRequest? TradeRequest { get; set; }
    public Business? SupplierBusiness { get; set; }
}