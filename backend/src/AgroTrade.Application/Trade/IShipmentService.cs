using AgroTrade.Application.Common;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Application.Trade;

/// <summary>
/// Shipments and their tracking milestones, plus the anonymous guest-tracking read
/// (plan §02 — Shipments, roadmap task 2.6).
/// </summary>
public interface IShipmentService
{
    /// <summary>
    /// Records the consignment for an order and writes its first milestone.
    /// </summary>
    /// <remarks>
    /// Seller only. Ports and incoterm default to the order's snapshotted trade terms, so a
    /// shipment cannot quietly contradict what the buyer agreed to. Refused on a draft or
    /// cancelled order, and when the order already has a consignment.
    /// </remarks>
    Task<ShipmentDto> CreateAsync(
        Guid orderId, Guid userId, CreateShipmentRequest request, CancellationToken ct = default);

    /// <summary>The order's shipments. At most one today; a list so splitting need not break callers.</summary>
    Task<IReadOnlyList<ShipmentDto>> ListForOrderAsync(
        Guid orderId, Guid userId, bool isAdmin = false, CancellationToken ct = default);

    /// <summary>Every shipment the caller is a party to. Both parties may read.</summary>
    Task<IReadOnlyList<ShipmentDto>> ListAsync(
        Guid userId, bool isAdmin = false, ShipmentQuery? query = null, CancellationToken ct = default);

    /// <summary>A shipment with its full timeline. Both parties may read.</summary>
    Task<ShipmentDto> GetAsync(Guid shipmentId, Guid userId, bool isAdmin = false, CancellationToken ct = default);

    /// <summary>
    /// Appends a milestone. Seller only.
    /// </summary>
    /// <remarks>
    /// Append-only: there is no edit and no delete. The event's status becomes the shipment's
    /// status, because that is the only definition of it.
    /// </remarks>
    Task<ShipmentTrackingEventDto> AppendEventAsync(
        Guid shipmentId, Guid userId, AppendTrackingEventRequest request, CancellationToken ct = default);

    /// <summary>
    /// Issues a fresh guest tracking token, invalidating the previous link, and returns the raw
    /// value. Seller only.
    /// </summary>
    /// <remarks>
    /// The only remedy for a leaked link, and the reason it is worth having: tracking links get
    /// forwarded into inboxes and spreadsheets, and the raw token cannot be recovered from storage
    /// to be reissued. Rotation is the only way back.
    /// </remarks>
    Task<TrackingTokenDto> RotateTrackingTokenAsync(
        Guid shipmentId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Anonymous read by guest token.
    /// </summary>
    /// <remarks>
    /// Deliberately returns <see cref="ShipmentPublicTrackingDto"/>, not
    /// <see cref="ShipmentDto"/>. A shared shape would hand anyone holding a forwarded link the
    /// order number, both parties' names and the trade value — none of which a freight forwarder
    /// checking a container needs, and all of which is commercially sensitive.
    /// </remarks>
    Task<ShipmentPublicTrackingDto> TrackAsync(string token, CancellationToken ct = default);
}

/// <summary>Optional filters for <see cref="IShipmentService.ListAsync"/>.</summary>
public sealed class ShipmentQuery
{
    /// <summary>Restrict to the caller's side of the trade. Null means both.</summary>
    public ShipmentRole? Role { get; init; }

    public ShipmentStatus? Status { get; init; }
}

/// <summary>Which side of a trade the caller is on, for shipment listing.</summary>
public enum ShipmentRole
{
    Buyer,
    Seller,
}

/// <summary>The consignment to record against an order.</summary>
public sealed class CreateShipmentRequest
{
    public ShipmentMode Mode { get; init; }

    /// <summary>Carrier or freight forwarder. Required: a consignment with no carrier cannot be tracked.</summary>
    public string CarrierName { get; init; } = string.Empty;

    /// <summary>Container number, air waybill or bill of lading, if one exists yet.</summary>
    public string? TrackingReference { get; init; }

    /// <summary>
    /// Route. Each field falls back to the order's own, so the common case needs no input at all.
    /// </summary>
    public string? OriginCountry { get; init; }
    public string? OriginPort { get; init; }
    public string? DestinationCountry { get; init; }
    public string? DestinationPort { get; init; }

    /// <summary>Falls back to the order's incoterm.</summary>
    public Incoterms? Incoterms { get; init; }

    public DateTime? EstimatedArrivalUtc { get; init; }

    /// <summary>The first milestone. Omit for "Booked" at the origin.</summary>
    public string? InitialStatus { get; init; }
    public string? InitialNote { get; init; }

    /// <summary>
    /// When the consignment was booked. Defaults to now.
    /// </summary>
    /// <remarks>
    /// Exists because a shipment's status is its latest milestone, so the booking milestone's date
    /// decides what can ever be recorded after it. Without this field a seller logging up a
    /// consignment that left last week has no way to write a coherent history: the booking would be
    /// stamped today and every real milestone, correctly dated in the past, would sort beneath it.
    /// </remarks>
    public DateTime? InitialOccurredAtUtc { get; init; }

    /// <summary>Issue a guest tracking link. On by default; the raw token comes back once.</summary>
    public bool EnableGuestTracking { get; init; } = true;
}

/// <summary>A milestone to append to a shipment's timeline.</summary>
public sealed class AppendTrackingEventRequest
{
    public ShipmentStatus Status { get; init; }

    public string? Location { get; init; }
    public string? Description { get; init; }

    /// <summary>
    /// When the milestone happened. Defaults to now. Send UTC; a value with no offset is read as
    /// UTC, matching gateway timestamps. Must not be in the future.
    /// </summary>
    public DateTime? OccurredAtUtc { get; init; }
}

/// <summary>A shipment, for the two parties and admins.</summary>
public sealed record ShipmentDto
{
    public Guid Id { get; init; }
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;

    /// <summary>Derived from the latest milestone — never stored.</summary>
    public ShipmentStatus Status { get; init; }

    public DateTime? StatusSinceUtc { get; init; }
    public ShipmentMode Mode { get; init; }
    public string CarrierName { get; init; } = string.Empty;
    public string? TrackingReference { get; init; }
    public string? OriginCountry { get; init; }
    public string? OriginPort { get; init; }
    public string? DestinationCountry { get; init; }
    public string? DestinationPort { get; init; }
    public Incoterms? Incoterms { get; init; }
    public DateTime? DepartedAtUtc { get; init; }
    public DateTime? EstimatedArrivalUtc { get; init; }

    /// <summary>Whether a guest link exists. The link itself is never readable after creation.</summary>
    public bool HasGuestTracking { get; init; }

    /// <summary>Set on the party reading it, so the UI can offer the right actions.</summary>
    public bool IsSeller { get; init; }
    public bool IsBuyer { get; init; }

    /// <summary>Newest milestone first. Empty only for a shipment that does not exist yet.</summary>
    public IReadOnlyList<ShipmentTrackingEventDto> Events { get; init; } = [];
}

/// <summary>One milestone on a timeline.</summary>
public sealed record ShipmentTrackingEventDto
{
    public Guid Id { get; init; }
    public Guid ShipmentId { get; init; }
    public ShipmentStatus Status { get; init; }
    public string? Location { get; init; }
    public string? Description { get; init; }
    public DateTime OccurredAtUtc { get; init; }
    public DateTime RecordedAtUtc { get; init; }
    public string? RecordedByDisplayName { get; init; }
}

/// <summary>
/// The anonymous guest view: where the goods are, and nothing else.
/// </summary>
/// <remarks>
/// The omissions are the design. No order number, no party names, no amounts, no currency, no
/// product, no documents. A freight forwarder or customs agent checking a container needs the
/// route, the carrier, the reference, the ETA and the milestone history. Every one of those is
/// here; nothing that would let the holder of a forwarded link learn who traded what, for how much.
/// </remarks>
public sealed record ShipmentPublicTrackingDto
{
    /// <summary>The shipment's own reference, which carries no order information.</summary>
    public string Reference { get; init; } = string.Empty;

    public ShipmentStatus Status { get; init; }
    public ShipmentMode Mode { get; init; }
    public string CarrierName { get; init; } = string.Empty;
    public string? TrackingReference { get; init; }
    public string? OriginCountry { get; init; }
    public string? OriginPort { get; init; }
    public string? DestinationCountry { get; init; }
    public string? DestinationPort { get; init; }
    public Incoterms? Incoterms { get; init; }
    public DateTime? DepartedAtUtc { get; init; }
    public DateTime? EstimatedArrivalUtc { get; init; }

    /// <summary>Oldest first, so the page reads as a story.</summary>
    public IReadOnlyList<ShipmentPublicTrackingEventDto> Events { get; init; } = [];
}

/// <summary>A milestone in the anonymous view. Carries the same fields as the party view.</summary>
public sealed record ShipmentPublicTrackingEventDto
{
    public ShipmentStatus Status { get; init; }
    public string? Location { get; init; }
    public string? Description { get; init; }
    public DateTime OccurredAtUtc { get; init; }
}

/// <summary>
/// A freshly issued guest tracking token.
/// </summary>
/// <remarks>
/// The raw value appears here and nowhere else, ever — not in a read endpoint, not in the order
/// detail payload, not in the database. The copy is taken at this moment or it is lost, and the
/// hash can only be rotated, never recovered. That is the intended trade: a link that cannot be
/// re-read is a link that cannot leak from a backup, an export, or a support screenshot.
/// </remarks>
public sealed record TrackingTokenDto
{
    public Guid ShipmentId { get; init; }

    /// <summary>The raw token. Path segment for <c>GET /shipments/track/{token}</c>.</summary>
    public string Token { get; init; } = string.Empty;

    /// <summary>Ready-to-share absolute-ish path the seller can copy.</summary>
    public string TrackingPath { get; init; } = string.Empty;
}