using AgroTrade.Domain.Enums;

namespace AgroTrade.Application.Intelligence;

/// <summary>
/// The documents a route is likely to require, plus a one-line summary of what drives the list.
/// Document types are returned as the same int-backed enum the rest of the trade surface uses.
/// </summary>
public sealed record DestinationRequirementsResponse
{
    public string Origin { get; init; } = string.Empty;
    public string Destination { get; init; } = string.Empty;

    /// <summary>True when origin and destination are the same country: no customs paperwork.</summary>
    public bool Domestic { get; init; }

    public IReadOnlyList<DestinationRequirementDto> Documents { get; init; } = [];

    /// <summary>A short summary of the corridor rules applied, or null when there is nothing to add.</summary>
    public string? Notes { get; init; }
}

public sealed record DestinationRequirementDto
{
    public DocumentType Document { get; init; }

    /// <summary>True when trade on this route ordinarily cannot clear without it.</summary>
    public bool Mandatory { get; init; }

    /// <summary>Why this document is on the list, in one short line.</summary>
    public string Reason { get; init; } = string.Empty;
}
