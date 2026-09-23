using System.ComponentModel.DataAnnotations.Schema;
using AgroTrade.Domain.Common;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Domain.Trade;

/// <summary>
/// A trading entity (exporter, importer, processor, ...). Verification status drives
/// the marketplace Verified badge (plan §02 — Business & trust).
/// </summary>
public sealed class Business : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; set; }
    public string LegalName { get; set; } = string.Empty;
    /// <summary>Public-facing name (what appears in search and on listings).</summary>
    public string TradingName { get; set; } = string.Empty;
    public string? RegistrationNumber { get; set; }
    public BusinessType BusinessType { get; set; } = BusinessType.LimitedCompany;
    public Guid? CategoryFocusId { get; set; }
    public string? Website { get; set; }
    public string? Description { get; set; }
    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.Unverified;
    public decimal Rating { get; set; }
    public int RatingCount { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    [NotMapped]
    public bool IsVerifiedBadgeVisible => VerificationStatus == VerificationStatus.Verified;

    public List<BusinessTradeCountry> TradeCountries { get; set; } = [];
    public List<BusinessDocument> Documents { get; set; } = [];
    public List<BusinessVerification> VerificationHistory { get; set; } = [];
    public List<Product> Products { get; set; } = [];
}