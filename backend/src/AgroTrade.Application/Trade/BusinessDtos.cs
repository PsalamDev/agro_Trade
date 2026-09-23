using System.ComponentModel.DataAnnotations;

namespace AgroTrade.Application.Trade;

public sealed class RegisterBusinessRequest
{
    [Required, MaxLength(250)]
    public string LegalName { get; init; } = string.Empty;

    [Required, MaxLength(250)]
    public string TradingName { get; init; } = string.Empty;

    [MaxLength(100)]
    public string? RegistrationNumber { get; init; }

    public int BusinessType { get; init; } = (int)AgroTrade.Domain.Enums.BusinessType.LimitedCompany;

    public Guid? CategoryFocusId { get; init; }

    [MaxLength(300)]
    public string? Website { get; init; }

    [MaxLength(2000)]
    public string? Description { get; init; }
}

public sealed class UpdateBusinessRequest
{
    [MaxLength(250)]
    public string? TradingName { get; init; }

    [MaxLength(300)]
    public string? Website { get; init; }

    [MaxLength(2000)]
    public string? Description { get; init; }
}

/// <summary>Replaces the exports-to / imports-from country lists per direction.</summary>
public sealed class TradeCountriesRequest
{
    public List<string> ExportsTo { get; init; } = [];
    public List<string> ImportsFrom { get; init; } = [];
}

public sealed class ReviewVerificationRequest
{
    public bool Approve { get; init; }
    [MaxLength(2000)]
    public string? Notes { get; init; }
}

public sealed class BusinessSearchQuery
{
    public string? Q { get; init; }
    public string? Country { get; init; }
    public int? Type { get; init; }
    public bool VerifiedOnly { get; init; }
    public string? ExportsTo { get; init; }
    public string? ImportsFrom { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed class AdminBusinessSearchQuery
{
    public int? Status { get; init; }
    public string? Q { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed class BusinessDocumentDto
{
    public Guid Id { get; init; }
    public int DocType { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string FileUrl { get; init; } = string.Empty;
    public DateTime UploadedAtUtc { get; init; }
}

public class BusinessSummaryDto
{
    public Guid Id { get; init; }
    public string TradingName { get; init; } = string.Empty;
    public string LegalName { get; init; } = string.Empty;
    public int BusinessType { get; init; }
    public int VerificationStatus { get; init; }
    public bool IsVerifiedBadgeVisible { get; init; }
    public decimal Rating { get; init; }
    public int RatingCount { get; init; }
    public Guid? CategoryFocusId { get; init; }
    public List<string> ExportsTo { get; init; } = [];
    public List<string> ImportsFrom { get; init; } = [];
    public bool IsOwner { get; init; }
}

public sealed class VerificationHistoryDto
{
    public int Status { get; init; }
    public DateTime RequestedAtUtc { get; init; }
    public DateTime? ReviewedAtUtc { get; init; }
    public string? Notes { get; init; }
}

public sealed class VerificationStateDto
{
    public int Status { get; init; }
    public bool IsBadgeVisible { get; init; }
    public string VerifiedLevel { get; init; } = "Basic";
    public DateTime? RequestedAtUtc { get; init; }
    public DateTime? ReviewedAtUtc { get; init; }
    public string? Notes { get; init; }
    public List<VerificationHistoryDto> History { get; init; } = [];
}

public sealed class BusinessDetailDto : BusinessSummaryDto
{
    public Guid OwnerUserId { get; init; }
    public string? Website { get; init; }
    public string? Description { get; init; }
    public VerificationStateDto? Verification { get; init; }
    public List<BusinessDocumentDto> Documents { get; init; } = [];
    public List<Guid> ProductIds { get; init; } = [];
}