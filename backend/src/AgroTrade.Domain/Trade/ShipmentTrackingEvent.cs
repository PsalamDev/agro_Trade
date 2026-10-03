using AgroTrade.Domain.Common;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Domain.Trade;

/// <summary>
/// One carrier milestone: "vessel departed Mombasa", "held at customs". The shipment's progress
/// is the story these tell, in order (plan §02 — ShipmentTrackingEvents).
/// </summary>
/// <remarks>
/// <para>
/// <b>Append-only, by design.</b> There is no update and no delete path, in the API or the model.
/// A milestone that turns out to have been typed wrong is corrected by appending the right one
/// after it, so the record of what was believed and when keeps its integrity — which is the same
/// reason a payment ledger entry is never edited.
/// </para>
/// <para>
/// That matters commercially here: a shipment timeline is the evidence a buyer uses when goods
/// arrive late or damaged. An editable timeline is worthless as evidence, because it cannot show
/// that it was not adjusted after the fact.
/// </para>
/// <para>
/// Statuses are not required to advance. Real freight is messy — a container can clear customs
/// days after a "departed" milestone, and a delivery can be followed by a customs exception. The
/// timeline records what happened; refusing an inconvenient milestone would only push someone to
/// stop recording them.
/// </para>
/// </remarks>
public sealed class ShipmentTrackingEvent : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ShipmentId { get; set; }

    public ShipmentStatus Status { get; set; }

    /// <summary>Where it happened — port, airport, city, or "in transit".</summary>
    public string? Location { get; set; }

    /// <summary>The carrier's own wording, or the seller's note about the milestone.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// When the milestone happened, per the carrier. Normalised to UTC on the way in — see
    /// <see cref="Shipment.LatestFirst"/>, which depends on that.
    /// </summary>
    /// <remarks>
    /// Cannot be in the future. A milestone dated ahead of now would immediately become the
    /// shipment's current status, because status is the latest milestone — so the shipment would
    /// report a position the goods are not in.
    /// </remarks>
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>Who typed it in. Carriers have no accounts yet, so this is the seller.</summary>
    public Guid RecordedByUserId { get; set; }

    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public Shipment? Shipment { get; set; }
}