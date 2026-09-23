using System.ComponentModel.DataAnnotations;

namespace AgroTrade.Application.Trade;

public sealed class CreateTradeRequestRequest
{
    [Required, MaxLength(200)]
    public string Title { get; init; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; init; }

    public Guid? CategoryId { get; init; }
    public decimal Quantity { get; init; }
    public int Unit { get; init; } = (int)AgroTrade.Domain.Enums.PriceUnit.PerTon;

    [Required, MaxLength(2)]
    public string DestinationCountry { get; init; } = string.Empty;

    [MaxLength(120)]
    public string? DestinationPortOrCity { get; init; }

    public int? PreferredIncoterm { get; init; }
    public int? TransportMode { get; init; }
    public bool InsuranceRequired { get; init; }
    public Dictionary<string, string> TargetSpecs { get; init; } = [];
    public List<string> CertificationsRequired { get; init; } = [];

    [Required, MaxLength(3)]
    public string PreferredCurrency { get; init; } = "USD";

    public int? PaymentTermsPreferred { get; init; }
    public DateTime? DeliveryDeadline { get; init; }
    public decimal? BudgetMin { get; init; }
    public decimal? BudgetMax { get; init; }
    public DateTime ExpiresAt { get; init; }
}

public class TradeRequestCardDto
{
    public Guid Id { get; init; }
    public string RfqNumber { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public Guid? CategoryId { get; init; }
    public decimal Quantity { get; init; }
    public int Unit { get; init; }
    public string DestinationCountry { get; init; } = string.Empty;
    public string? DestinationPortOrCity { get; init; }
    public int? PreferredIncoterm { get; init; }
    public int? TransportMode { get; init; }
    public string PreferredCurrency { get; init; } = "USD";
    public int Status { get; init; }
    public DateTime ExpiresAt { get; init; }
    public DateTime? CreatedAtUtc { get; init; }
    public int QuotationCount { get; init; }
    public bool IsMine { get; init; }
    public bool CanQuote { get; init; }
    public List<string> CertificationsRequired { get; init; } = [];
}

public sealed class TradeRequestDetailDto : TradeRequestCardDto
{
    public string? Description { get; init; }
    public bool InsuranceRequired { get; init; }
    public Dictionary<string, string> TargetSpecs { get; init; } = [];
    public int? PaymentTermsPreferred { get; init; }
    public DateTime? DeliveryDeadline { get; init; }
    public decimal? BudgetMin { get; init; }
    public decimal? BudgetMax { get; init; }
    public string BuyerDisplayName { get; init; } = string.Empty;
    public bool IsBuyer { get; init; }
    public List<QuotationDto> Quotations { get; init; } = [];
}

public sealed class CreateQuotationRequest
{
    [Required, MaxLength(3)]
    public string Currency { get; init; } = "USD";

    [Required]
    public int Incoterms { get; init; } = (int)AgroTrade.Domain.Enums.Incoterms.Cif;

    [MaxLength(120)]
    public string? PortOfLoading { get; init; }

    [MaxLength(120)]
    public string? PortOfDischarge { get; init; }

    public int LeadTimeDays { get; init; }
    public int PaymentTerms { get; init; } = (int)AgroTrade.Domain.Enums.PaymentTerms.TtPartialDeposit;
    public decimal Total { get; init; }

    [MaxLength(2000)]
    public string? Notes { get; init; }

    public DateTime? ValidUntil { get; init; }

    /// <summary>Optional — the quoting supplier business (must be owned + verified).</summary>
    public Guid? BusinessId { get; init; }
}

public sealed class QuotationDto
{
    public Guid Id { get; init; }
    public Guid TradeRequestId { get; init; }
    public Guid SupplierBusinessId { get; init; }
    /// <summary>Owner of the quoting business — lets the RFQ buyer open a negotiation thread.</summary>
    public Guid SupplierOwnerUserId { get; init; }
    public string SupplierTradingName { get; init; } = string.Empty;
    public bool IsSupplierVerifiedBadgeVisible { get; init; }
    public int Status { get; init; }
    public string Currency { get; init; } = "USD";
    public int Incoterms { get; init; }
    public string? PortOfLoading { get; init; }
    public string? PortOfDischarge { get; init; }
    public int LeadTimeDays { get; init; }
    public int PaymentTerms { get; init; }
    public decimal Total { get; init; }
    public string? Notes { get; init; }
    public DateTime? ValidUntil { get; init; }
    public DateTime? CreatedAtUtc { get; init; }
    public bool IsMine { get; init; }
}

public sealed class OpenRfqFilters
{
    public string? DestinationCountry { get; init; }
    public Guid? CategoryId { get; init; }
    public string? Q { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed class RfqListQuery
{
    public string? As { get; init; }
    public int? Status { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}