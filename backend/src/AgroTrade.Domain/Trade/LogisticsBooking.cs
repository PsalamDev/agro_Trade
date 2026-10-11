using AgroTrade.Domain.Common;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Domain.Trade;

/// <summary>
/// A request to a directory <see cref="LogisticsProvider"/> to move an order's goods (Phase 4 —
/// LSP booking). This is the link between an order and a provider.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="Shipment"/>, and deliberately so. A shipment is the operational
/// record the seller keeps once goods are actually moving — one per order, append-only milestones,
/// guest tracking. A booking is the commercial step before that: either party to the order may ask
/// a provider to take the leg, and the provider may accept or decline. Once accepted, the seller
/// still records the consignment the way they always have; the booking is the record of who agreed
/// to carry it and when.
/// </para>
/// <para>
/// The provider is referenced by foreign key rather than by a snapshotted name. Unlike an address
/// (which is mutable, private, and shared across the buyer's history), a provider is a directory
/// entry whose whole purpose is to be found again — so the booking should show the provider as it
/// is now, and a deactivated provider is hidden by <see cref="LogisticsProvider.IsActive"/> rather
/// than deleted.
/// </para>
/// </remarks>
public sealed class LogisticsBooking : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Unique reference, e.g. "LSP-2026-000001".</summary>
    public string Reference { get; set; } = string.Empty;

    public Guid OrderId { get; set; }

    public Guid LogisticsProviderId { get; set; }

    /// <summary>The party who raised the request — the buyer or the seller, never the provider.</summary>
    public Guid RequestedByUserId { get; set; }

    /// <summary>The leg being requested. A single mode, matching <see cref="Shipment.Mode"/> so the
    /// booking and the consignment that follows it can be lined up.</summary>
    public ShipmentMode Mode { get; set; }

    public LogisticsBookingStatus Status { get; set; } = LogisticsBookingStatus.Requested;

    /// <summary>Free text from the requester: cargo details, collection window, special handling.</summary>
    public string? Notes { get; set; }

    /// <summary>The provider's note recorded when accepting or declining.</summary>
    public string? ResponseNotes { get; set; }

    /// <summary>When the provider accepted or declined. Null while the request is open.</summary>
    public DateTime? DecidedAtUtc { get; set; }

    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public Order? Order { get; set; }
    public LogisticsProvider? Provider { get; set; }
}
