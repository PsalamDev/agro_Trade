using AgroTrade.Domain.Enums;

namespace AgroTrade.Application.Trade;

// Logistics-provider directory and booking (Phase 4 — LSP directory + booking). DTO enum fields
// follow the same int-backed shape as the rest of the trade surface, so the contract stays
// enum-agnostic to the client.

/// <summary>Filters for the provider directory. Nulls mean "any".</summary>
public sealed class LogisticsProviderQuery
{
    /// <summary>Restrict to providers whose coverage includes this ISO-3166 alpha-2 country.</summary>
    public string? Country { get; init; }

    /// <summary>Restrict to providers that handle this transport mode.</summary>
    public ShipmentMode? Mode { get; init; }

    /// <summary>Restrict to providers that offer this ancillary service.</summary>
    public LogisticsService? Service { get; init; }

    /// <summary>Free-text search over company name and description.</summary>
    public string? Q { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

/// <summary>
/// A provider as it appears in the public directory. Deliberately carries no contact details: the
/// directory is anonymous, and direct contact is a benefit of having an account.
/// </summary>
public sealed record LogisticsProviderCardDto
{
    public Guid Id { get; init; }
    public string CompanyName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string HomeCountry { get; init; } = string.Empty;
    public IReadOnlyList<ShipmentMode> Modes { get; init; } = [];
    public IReadOnlyList<LogisticsService> Services { get; init; } = [];
    public IReadOnlyList<string> Coverage { get; init; } = [];
    public bool IsVerified { get; init; }
    public decimal Rating { get; init; }
    public int RatingCount { get; init; }
}

/// <summary>A provider's full profile, including contact details. Authenticated callers only.</summary>
public sealed record LogisticsProviderDetailDto
{
    public Guid Id { get; init; }
    public string CompanyName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string HomeCountry { get; init; } = string.Empty;
    public IReadOnlyList<ShipmentMode> Modes { get; init; } = [];
    public IReadOnlyList<LogisticsService> Services { get; init; } = [];
    public IReadOnlyList<string> Coverage { get; init; } = [];
    public bool IsVerified { get; init; }
    public decimal Rating { get; init; }
    public int RatingCount { get; init; }

    public string? ContactEmail { get; init; }
    public string? ContactPhone { get; init; }
    public string? Website { get; init; }

    public bool IsActive { get; init; }

    /// <summary>True when the caller owns this profile, so the UI can offer the edit form.</summary>
    public bool IsMine { get; init; }

    public DateTime? CreatedAtUtc { get; init; }
    public DateTime? UpdatedAtUtc { get; init; }
}

/// <summary>
/// Creates or replaces the caller's own provider profile. Whole-object rather than a patch: a
/// directory entry is small, and a replace is far easier to reason about than merging a set of
/// mode and service bitmasks field by field.
/// </summary>
public sealed record UpsertLogisticsProviderRequest
{
    public string CompanyName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string HomeCountry { get; init; } = string.Empty;
    public string? ContactEmail { get; init; }
    public string? ContactPhone { get; init; }
    public string? Website { get; init; }

    /// <summary>Transport modes handled. At least one is required, or the provider cannot be found.</summary>
    public List<ShipmentMode> Modes { get; init; } = [];

    public List<LogisticsService> Services { get; init; } = [];

    /// <summary>ISO-3166 alpha-2 countries served. At least one is required.</summary>
    public List<string> Coverage { get; init; } = [];

    public bool IsActive { get; init; } = true;
}

/// <summary>Raises a booking request against an order, naming the provider and the leg.</summary>
public sealed record CreateLogisticsBookingRequest
{
    public Guid ProviderId { get; init; }
    public ShipmentMode Mode { get; init; }
    public string? Notes { get; init; }
}

/// <summary>The provider's answer to a booking request.</summary>
public sealed record RespondToLogisticsBookingRequest
{
    /// <summary>True to accept, false to decline.</summary>
    public bool Accept { get; init; }

    public string? Notes { get; init; }
}

public sealed class LogisticsBookingQuery
{
    public LogisticsBookingStatus? Status { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

/// <summary>A booking request, for both sides of it and for the order it names.</summary>
public sealed record LogisticsBookingDto
{
    public Guid Id { get; init; }
    public string Reference { get; init; } = string.Empty;

    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public string? OrderDestinationCountry { get; init; }

    public Guid ProviderId { get; init; }
    public string ProviderName { get; init; } = string.Empty;
    public string ProviderHomeCountry { get; init; } = string.Empty;

    public Guid RequestedByUserId { get; init; }
    public string RequestedByDisplayName { get; init; } = string.Empty;

    public ShipmentMode Mode { get; init; }
    public LogisticsBookingStatus Status { get; init; }
    public string? Notes { get; init; }
    public string? ResponseNotes { get; init; }
    public DateTime? DecidedAtUtc { get; init; }

    /// <summary>Set on the caller, so the UI can offer the right actions without a second lookup.</summary>
    public bool IsRequester { get; init; }
    public bool IsProvider { get; init; }

    public DateTime? CreatedAtUtc { get; init; }
}
