using System.ComponentModel.DataAnnotations;

namespace AgroTrade.Application.Trade;

public sealed class CreateProductRequest
{
    [Required, MaxLength(200)]
    public string Name { get; init; } = string.Empty;

    [Required]
    public Guid CategoryId { get; init; }

    [MaxLength(10)]
    public string? HsCode { get; init; }

    [Required, MaxLength(2)]
    public string OriginCountry { get; init; } = string.Empty;

    [MaxLength(120)]
    public string? PortOfLoading { get; init; }

    public decimal Price { get; init; }
    public int PriceUnit { get; init; } = (int)AgroTrade.Domain.Enums.PriceUnit.PerTon;

    [Required, MaxLength(3)]
    public string Currency { get; init; } = "USD";

    public decimal AvailableQuantity { get; init; }
    public decimal MinimumOrderQuantity { get; init; }
    /// <summary>Whether the listing also sells domestically (Phase 4 — buy-now / cart).</summary>
    public bool SellsDomestically { get; init; }
    /// <summary>Flat per-line domestic delivery charge, in the listing's currency.</summary>
    public decimal DomesticDeliveryFee { get; init; }
    public int? Incoterms { get; init; }
    public int? LeadTimeDays { get; init; }
    public List<string> PaymentTermsOffered { get; init; } = [];
    public Dictionary<string, string> QualitySpecs { get; init; } = [];
    public List<string> PackagingOptions { get; init; } = [];
    public List<string> Certifications { get; init; } = [];
    public int ListingStatus { get; init; } = (int)AgroTrade.Domain.Enums.ListingStatus.Active;
}

public sealed class UpdateProductRequest
{
    [MaxLength(200)]
    public string? Name { get; init; }

    [MaxLength(2000)]
    public string? Description { get; init; }

    [MaxLength(10)]
    public string? HsCode { get; init; }

    [MaxLength(120)]
    public string? PortOfLoading { get; init; }

    public decimal? Price { get; init; }
    public int? PriceUnit { get; init; }
    [MaxLength(3)]
    public string? Currency { get; init; }
    public decimal? AvailableQuantity { get; init; }
    public decimal? MinimumOrderQuantity { get; init; }
    public bool? SellsDomestically { get; init; }
    public decimal? DomesticDeliveryFee { get; init; }
    public int? Incoterms { get; init; }
    public int? LeadTimeDays { get; init; }
    public List<string>? PaymentTermsOffered { get; init; }
    public Dictionary<string, string>? QualitySpecs { get; init; }
    public List<string>? PackagingOptions { get; init; }
    public List<string>? Certifications { get; init; }
    public int? ListingStatus { get; init; }
}

public sealed class SellerCardDto
{
    public Guid BusinessId { get; init; }
    public string TradingName { get; init; } = string.Empty;
    public int VerificationStatus { get; init; }
    public bool IsVerifiedBadgeVisible { get; init; }
    public decimal Rating { get; init; }
    public int RatingCount { get; init; }
    public string? HomeCountry { get; init; }
}

public class ProductCardDto
{
    public Guid Id { get; init; }
    public string Slug { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? HsCode { get; init; }
    public string OriginCountry { get; init; } = string.Empty;
    public string? PortOfLoading { get; init; }
    public decimal Price { get; init; }
    public int PriceUnit { get; init; }
    public string Currency { get; init; } = "USD";
    public decimal MinimumOrderQuantity { get; init; }
    /// <summary>Whether the listing is bookable by a domestic buyer (Phase 4).</summary>
    public bool SellsDomestically { get; init; }
    /// <summary>Flat per-line domestic delivery charge, in the listing's currency.</summary>
    public decimal DomesticDeliveryFee { get; init; }
    public decimal AvailableQuantity { get; init; }
    public int? Incoterms { get; init; }
    public int? LeadTimeDays { get; init; }
    public int ListingStatus { get; init; }
    public int VerificationStatus { get; init; }
    public bool IsVerified { get; init; }
    public Guid SellerBusinessId { get; init; }
    public string SellerTradingName { get; init; } = string.Empty;
    public bool IsSellerVerifiedBadgeVisible { get; init; }
    public string? PrimaryImageUrl { get; init; }
    public Guid CategoryId { get; init; }
    public DateTime? CreatedAtUtc { get; init; }
}

public sealed class ProductImageDto
{
    public Guid Id { get; init; }
    public string Url { get; init; } = string.Empty;
    public string? AltText { get; init; }
    public int SortOrder { get; init; }
    public bool IsPrimary { get; init; }
}

public sealed class ProductDetailDto : ProductCardDto
{
    public string? Description { get; init; }
    public List<string> PaymentTermsOffered { get; init; } = [];
    public Dictionary<string, string> QualitySpecs { get; init; } = [];
    public List<string> PackagingOptions { get; init; } = [];
    public List<string> Certifications { get; init; } = [];
    public List<ProductImageDto> Images { get; init; } = [];
    public SellerCardDto Seller { get; init; } = new();
}

public sealed class ProductSearchQuery
{
    public string? Q { get; init; }
    public Guid? CategoryId { get; init; }
    public string? HsCode { get; init; }
    public string? OriginCountry { get; init; }
    /// <summary>Destination reachability: seller exports to this country.</summary>
    public string? ExportsToCountry { get; init; }
    public int? Incoterms { get; init; }
    public string? Currency { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public bool VerifiedOnly { get; init; }
    /// <summary>Domestic only: restrict to listings opted into local buy-now (Phase 4).</summary>
    public bool DomesticOnly { get; init; }
    public Guid? SellerBusinessId { get; init; }
    public string? SortBy { get; init; }
    public string? SortDir { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}