using AgroTrade.Domain.Common;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Domain.Trade;

/// <summary>
/// An export product listing with full trade terms (HS code, incoterms, port of
/// loading, lead time, certifications) per plan §02 — Catalog. Destination
/// reachability comes from the seller's BusinessTradeCountries.
/// </summary>
public sealed class Product : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SellerBusinessId { get; set; }
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Harmonized System customs classification, e.g. "0801" cashew.</summary>
    public string? HsCode { get; set; }
    /// <summary>ISO-3166 alpha-2 origin country (required — every listing is export-capable).</summary>
    public string OriginCountry { get; set; } = string.Empty;
    /// <summary>e.g. "Mombasa (MBA)".</summary>
    public string? PortOfLoading { get; set; }
    public decimal Price { get; set; }
    public PriceUnit PriceUnit { get; set; }
    /// <summary>ISO-4217.</summary>
    public string Currency { get; set; } = "USD";
    /// <summary>
    /// Bookable stock, denormalised from <see cref="Inventory"/> as OnHand - Reserved so the
    /// marketplace can show live availability without a join. Written only by
    /// <c>InventoryService</c>; seller edits route through it too.
    /// </summary>
    public decimal AvailableQuantity { get; set; }
    /// <summary>Minimum order quantity (bulk trade).</summary>
    public decimal MinimumOrderQuantity { get; set; }
    public Incoterms? Incoterms { get; set; }
    public int? LeadTimeDays { get; set; }
    /// <summary>JSON string array, e.g. ["T/T 50/50", "L/C at sight"].</summary>
    public string? PaymentTermsOffered { get; set; }
    /// <summary>JSON object: grade, moisture, purity, size, ...</summary>
    public string? QualitySpecs { get; set; }
    /// <summary>JSON string array, e.g. ["50 kg PP bag", "1 MT jumbo bag"].</summary>
    public string? PackagingOptions { get; set; }
    /// <summary>JSON string array, e.g. ["GlobalG.A.P.", "Phytosanitary"].</summary>
    public string? Certifications { get; set; }
    public ListingStatus ListingStatus { get; set; } = ListingStatus.Draft;

    /// <summary>
    /// Whether an administrator has acted against this listing. Separate from
    /// <see cref="ListingStatus"/> on purpose: a suspension must survive the seller editing their
    /// own listing back to <c>Active</c>, which one field could not do.
    /// </summary>
    public ModerationStatus ModerationStatus { get; set; } = ModerationStatus.Cleared;

    /// <summary>When the current suspension was applied. Cleared when <see cref="ModerationStatus"/> is.</summary>
    public DateTime? SuspendedAtUtc { get; set; }

    /// <summary>The administrator who applied the current suspension.</summary>
    public Guid? SuspendedByUserId { get; set; }

    /// <summary>
    /// Why the listing was suspended, as stated by the administrator. Required to suspend: a
    /// moderation action nobody can explain is indistinguishable from an arbitrary one.
    /// </summary>
    public string? ModerationReason { get; set; }
    /// <summary>Snapshot of the seller's verification state at publish time.</summary>
    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.Unverified;
    public bool IsDeleted { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public Business? SellerBusiness { get; set; }
    public List<ProductImage> Images { get; set; } = [];

    /// <summary>
    /// Per-listing stock. Null until seeded or backfilled — the marketplace treats a null
    /// Inventory as zero available rather than guessing.
    /// </summary>
    public Inventory? Inventory { get; set; }
}