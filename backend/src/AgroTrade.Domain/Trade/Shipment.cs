using AgroTrade.Domain.Common;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Domain.Trade;

/// <summary>
/// Goods in motion against an order: the consignment itself, plus the carrier milestones that
/// say where it has got to (plan §02 — Shipments, task 2.6).
/// </summary>
/// <remarks>
/// <para>
/// <b>There is deliberately no Status column.</b> A shipment's status <i>is</i> the status of its
/// most recent tracking event. Storing it as well would create two sources of truth that can
/// disagree, and a shipment reading "In transit" with no milestone behind it is exactly the kind
/// of claim a buyer cannot check. Deriving it means every status the system reports is backed by
/// a dated, located, described event — see <see cref="LatestEvent"/>.
/// </para>
/// <para>
/// Because of that, creating a shipment writes its first tracking event. There is no state in
/// which a shipment exists and nothing has been said about it.
/// </para>
/// <para>
/// One shipment per order, enforced by a unique index. The order snapshots a single set of trade
/// terms — one incoterm, one port of loading, one port of discharge — so splitting a consignment
/// would mean allocating quantities across order items, which the model does not express. That is
/// a real trade pattern and a genuine MVP boundary rather than an oversight.
/// </para>
/// </remarks>
public sealed class Shipment : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Unique reference, e.g. "SHP-2026-000001".</summary>
    public string Reference { get; set; } = string.Empty;

    public Guid OrderId { get; set; }

    /// <summary>
    /// The carrier or freight forwarder handling the leg, as free text. There is no carrier
    /// entity in the MVP, and inventing one would mean maintaining a directory nobody asked for.
    /// </summary>
    public string CarrierName { get; set; } = string.Empty;

    /// <summary>
    /// The carrier's own identifier — container number, air waybill, bill of lading. Optional,
    /// because a booking exists before a container is assigned, but it is the field a buyer or
    /// freight agent will ask for.
    /// </summary>
    public string? TrackingReference { get; set; }

    public ShipmentMode Mode { get; set; }

    // ---- Route ----
    // Defaults to the order's snapshotted ports when the caller does not override them, so a
    // shipment cannot quietly contradict the trade terms the buyer agreed to.
    public string? OriginCountry { get; set; }
    public string? OriginPort { get; set; }
    public string? DestinationCountry { get; set; }
    public string? DestinationPort { get; set; }

    /// <summary>
    /// Snapshotted from the order when the caller does not override it. Non-nullable because the
    /// order always has one — a shipment is never booked without the incoterm in force.
    /// </summary>
    public Incoterms Incoterms { get; set; }

    /// <summary>Carrier's estimated arrival. A forecast, and never a promise.</summary>
    public DateTime? EstimatedArrivalUtc { get; set; }

    /// <summary>
    /// SHA-256 of the guest tracking token, base64url-decoded. Null means guest tracking is off.
    /// </summary>
    /// <remarks>
    /// Stored hashed, like a refresh token, because it is a bearer secret that gets pasted into
    /// emails to people outside the platform. A database dump must not yield working tracking
    /// links. The raw token is returned exactly once, when it is generated or rotated, and there
    /// is no endpoint that can recover it afterwards — so rotation is the only remedy for a leak,
    /// which is why rotation exists.
    /// </remarks>
    public string? PublicTrackingTokenHash { get; set; }

    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public byte[]? RowVersion { get; set; }

    public Order? Order { get; set; }
    public List<ShipmentTrackingEvent> Events { get; set; } = [];

    /// <summary>
    /// The milestone that defines where the shipment currently is.
    /// </summary>
    /// <remarks>
    /// Tie-broken on insertion time because two milestones reported in the same second are common
    /// — a port arrival and a customs clearance, say — and the timeline still has to be
    /// deterministic. Ordering by <c>OccurredAtUtc</c> alone would be wrong for the same reason the
    /// webhook handler normalises timestamps: a raw <see cref="DateTime"/> comparison ignores
    /// <see cref="DateTime.Kind"/>, so a milestone reported with an offset can read as later than
    /// one that happened after it. Ingestion normalises every timestamp to UTC first.
    /// </remarks>
    public ShipmentTrackingEvent? LatestEvent() =>
        LatestFirst(Events.AsQueryable()).FirstOrDefault();

    /// <summary>
    /// When the goods actually left, taken from the earliest <c>Departed</c> milestone.
    /// </summary>
    /// <remarks>
    /// Derived, not stored, for the same reason as <see cref="LatestEvent"/> — and more sharply,
    /// because this fact is unknowable at booking time. A column would have to be written later by
    /// a second code path, which is a second source of truth about something the timeline already
    /// records. Earliest rather than latest, so a re-routed second departure does not rewrite when
    /// the consignment first left.
    /// </remarks>
    public DateTime? DepartedAtUtc() => Events
        .Where(e => e.Status == ShipmentStatus.Departed)
        .Select(e => (DateTime?)e.OccurredAtUtc)
        .OrderBy(e => e)
        .FirstOrDefault();

    /// <summary>
    /// The one definition of "most recent milestone", used by the service, the projections and
    /// the status filter. Takes an <see cref="IQueryable{T}"/> so the identical expression serves
    /// both an EF query and an already-materialised list (<c>list.AsQueryable()</c>), rather than
    /// being written twice and drifting apart.
    /// </summary>
    public static IOrderedQueryable<ShipmentTrackingEvent> LatestFirst(IQueryable<ShipmentTrackingEvent> events) =>
        events.OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.CreatedAtUtc);
}