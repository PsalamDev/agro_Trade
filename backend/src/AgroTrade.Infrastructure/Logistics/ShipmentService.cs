using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using AgroTrade.Domain.Communication;
using AgroTrade.Domain.Enums;
using AgroTrade.Domain.Trade;
using AgroTrade.Infrastructure.Auth;
using AgroTrade.Infrastructure.Data;
using AgroTrade.Infrastructure.Trade;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Infrastructure.Logistics;

/// <summary>
/// Shipments against an order, and the milestones that say where the goods are (roadmap task
/// 2.6, plan §02 — Shipments).
/// </summary>
/// <remarks>
/// <para>
/// The seller records the consignment and appends milestones; the buyer reads them. Nobody edits
/// or deletes a milestone — see <see cref="ShipmentTrackingEvent"/> for why an append-only timeline
/// is the point rather than a limitation.
/// </para>
/// <para>
/// Neither the shipment's status nor its departure date is stored. Both are read off the
/// timeline, so there is no way for a row and its milestones to disagree about where the goods
/// are.
/// </para>
/// </remarks>
public sealed class ShipmentService(AgroTradeDbContext db, ITransactionalEmailService email) : IShipmentService
{
    public async Task<ShipmentDto> CreateAsync(
        Guid orderId, Guid userId, CreateShipmentRequest request, CancellationToken ct = default)
    {
        var order = await db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        // An order the caller has no part in is a 404, not a 403: its existence is itself private.
        var party = order is null ? OrderParty.Outsider : await PartyAsync(order, userId, ct);
        if (party == OrderParty.Outsider)
        {
            throw ApiException.NotFound("ORDER_NOT_FOUND", "Order not found.");
        }

        if (party != OrderParty.Seller)
        {
            throw ApiException.Forbidden(
                "NOT_SELLER", "Only the seller can record a shipment for this order.");
        }

        // Freight is booked against a live order. A draft has committed to nothing, and a
        // cancelled order has been unwound — a consignment against either would be a record of
        // something that is not happening.
        if (order!.Status is OrderStatus.Draft or OrderStatus.Cancelled)
        {
            throw ApiException.Conflict(
                "ORDER_CLOSED",
                $"A shipment cannot be recorded against a {(order.Status == OrderStatus.Draft ? "draft" : "cancelled")} order.");
        }

        if (await db.Shipments.AnyAsync(s => s.OrderId == orderId, ct))
        {
            throw ApiException.Conflict(
                "SHIPMENT_EXISTS", "This order already has a consignment recorded against it.");
        }

        var shipment = new Shipment
        {
            OrderId = order.Id,
            Mode = request.Mode,
            CarrierName = request.CarrierName.Trim(),

            // The route defaults to the terms the buyer agreed to. Overriding is legitimate —
            // goods do get re-routed — but the common case should not require typing ports that
            // are already on the order, and a shipment should not quietly contradict it.
            //
            // OriginCountry has no default to fall back on: the order carries a port of loading and
            // a destination country, but never an origin country. That asymmetry is in the order
            // model itself, and guessing at it here (from the seller's home country, say) would
            // put a plausible-looking wrong country on a public tracking page.
            OriginCountry = Blank(request.OriginCountry),
            OriginPort = Coalesce(request.OriginPort, order.PortOfLoading),
            DestinationCountry = Coalesce(request.DestinationCountry, order.DestinationCountry),
            DestinationPort = Coalesce(request.DestinationPort, order.PortOfDischarge),
            Incoterms = request.Incoterms ?? order.Incoterms,
            EstimatedArrivalUtc = ToUtc(request.EstimatedArrivalUtc),
            TrackingReference = Blank(request.TrackingReference),
        };

        // The token is optional and off is a real choice: some trades will not want a public link
        // at all. Defaulting to on, because asking a seller to opt in to sharing is how a feature
        // ends up unused, while opting out costs one checkbox.
        if (request.EnableGuestTracking)
        {
            var (_, hash) = OpaqueTokenFactory.Create();
            shipment.PublicTrackingTokenHash = hash;
        }

        // Event zero. Without it a shipment would exist with nothing said about it, and the status
        // — being the latest event — would have nothing to be.
        //
        // Its date is the origin of the whole timeline, so it is the caller's to set when they are
        // logging up a consignment that was booked earlier. Left at "now" by mistake, every real
        // milestone dated in the past sorts beneath the booking and the shipment reads as still at
        // the origin — a wrong answer produced by correct data.
        var bookedAt = ToUtc(request.InitialOccurredAtUtc) ?? DateTime.UtcNow;
        if (bookedAt > DateTime.UtcNow)
        {
            throw ApiException.BadRequest(
                "BOOKED_IN_FUTURE", "The booking date cannot be in the future.");
        }

        var origin = shipment.OriginPort;
        shipment.Events.Add(new ShipmentTrackingEvent
        {
            Shipment = shipment,
            Status = ResolveInitialStatus(request.InitialStatus),
            Location = origin,
            Description = Blank(request.InitialNote) ?? $"Consignment booked with {shipment.CarrierName}.",
            OccurredAtUtc = bookedAt,
            RecordedByUserId = userId,
        });

        db.Shipments.Add(shipment);

        try
        {
            // The reference is only knowable immediately before the insert, and the read that picks
            // it has to be locked against the insert that uses it; see SequenceAllocator. The
            // save happens inside the call, so the shipment, its first milestone and the buyer's
            // notification all commit together.
            await SequenceAllocator.AllocateAsync(
                db,
                table: "Shipments",
                column: "Reference",
                prefix: ShipmentReferencePrefix(),
                assign: number => shipment.Reference = number,
                save: async token =>
                {
                    // Only the buyer is told — the seller just did this.
                    db.Notifications.Add(new Notification
                    {
                        UserId = order.BuyerUserId,
                        Type = NotificationType.Shipment,
                        Title = $"Shipment {shipment.Reference} booked",
                        Body = $"{shipment.CarrierName} is moving this order. Track it as the milestones arrive.",
                        Link = $"/dashboard/orders/{order.Id}",
                    });

                    await db.SaveChangesAsync(token);
                },
                ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Two sellers booking the same order at once. The unique index on OrderId is the real
            // guard; the check above only narrows the common case and gives a better message.
            throw ApiException.Conflict(
                "SHIPMENT_EXISTS", "This order already has a consignment recorded against it.");
        }

        // The booking is committed by the allocator above, so the message describes a fact rather
        // than a change still in flight. Delivery is best-effort and never rolls the booking back.
        await email.OrderShippedAsync(order, shipment, ct);

        return (await ProjectAsync([shipment], userId, ct))[0];
    }

    public async Task<IReadOnlyList<ShipmentDto>> ListForOrderAsync(
        Guid orderId, Guid userId, bool isAdmin = false, CancellationToken ct = default)
    {
        // Participantship is decided against the order, not the shipment, so an order with no
        // consignment yet still 404s for a stranger rather than returning an empty list that
        // quietly confirms the order exists.
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null || (!isAdmin && await PartyAsync(order, userId, ct) == OrderParty.Outsider))
        {
            throw ApiException.NotFound("ORDER_NOT_FOUND", "Order not found.");
        }

        var shipments = await db.Shipments
            .AsNoTracking()
            .Include(s => s.Events)
            .Where(s => s.OrderId == orderId)
            .ToListAsync(ct);

        return await ProjectAsync(shipments, userId, ct);
    }

    public async Task<IReadOnlyList<ShipmentDto>> ListAsync(
        Guid userId, bool isAdmin = false, ShipmentQuery? query = null, CancellationToken ct = default)
    {
        var sellerBusinessIds = await SellerBusinessIdsAsync(userId, ct);

        // The timeline has to come along with the row. Status is read off the latest milestone, so a
        // shipment loaded without its events reports no status at all — and a status filter over
        // such a list silently matches nothing.
        var shipments = db.Shipments.AsNoTracking().Include(s => s.Events).AsQueryable();

        if (!isAdmin)
        {
            // Both sides of the trade may read, exactly as for documents and payments. A carrier
            // view would come with carrier accounts, which this phase does not have.
            shipments = shipments.Where(s =>
                s.Order!.BuyerUserId == userId || sellerBusinessIds.Contains(s.Order!.SellerBusinessId));
        }

        if (query?.Role == ShipmentRole.Buyer)
        {
            shipments = shipments.Where(s => s.Order!.BuyerUserId == userId);
        }
        else if (query?.Role == ShipmentRole.Seller)
        {
            shipments = shipments.Where(s => sellerBusinessIds.Contains(s.Order!.SellerBusinessId));
        }

        var materialised = await shipments.ToListAsync(ct);

        if (query?.Status is { } status)
        {
            // Status is derived, so filtering it in SQL would need a correlated subquery per row.
            // Shipments per user are few and already party-scoped, so filtering after load is both
            // simpler and fast enough — and it reuses Shipment.LatestFirst, so there is only one
            // definition of "latest milestone" in the system.
            materialised = materialised.Where(s => s.LatestEvent()?.Status == status).ToList();
        }

        // Most recently moved first: the consignment someone is waiting on is the live one.
        return await ProjectAsync(
            materialised
                .OrderByDescending(s => s.LatestEvent()?.OccurredAtUtc ?? DateTime.MinValue)
                .ThenByDescending(s => s.CreatedAtUtc ?? DateTime.MinValue)
                .ToList(),
            userId, ct);
    }

    public async Task<ShipmentDto> GetAsync(
        Guid shipmentId, Guid userId, bool isAdmin = false, CancellationToken ct = default)
    {
        var shipment = await db.Shipments
            .AsNoTracking()
            .Include(s => s.Events)
            .Include(s => s.Order)
            .FirstOrDefaultAsync(s => s.Id == shipmentId, ct);

        // Same rule as everywhere else: a consignment the caller is not party to does not exist as
        // far as they are concerned.
        var order = shipment?.Order;
        var party = order is null ? OrderParty.Outsider : await PartyAsync(order, userId, ct);

        if (shipment is null || party == OrderParty.Outsider)
        {
            throw ApiException.NotFound("SHIPMENT_NOT_FOUND", "Shipment not found.");
        }

        return (await ProjectAsync([shipment], userId, ct))[0];
    }

    public async Task<ShipmentTrackingEventDto> AppendEventAsync(
        Guid shipmentId, Guid userId, AppendTrackingEventRequest request, CancellationToken ct = default)
    {
        var (shipment, order) = await RequireSellerShipmentAsync(shipmentId, userId, ct, "add tracking events to");

        if (!Enum.IsDefined(request.Status))
        {
            throw ApiException.BadRequest("INVALID_SHIPMENT_STATUS", "That shipment status is not recognised.");
        }

        var occurredAt = ToUtc(request.OccurredAtUtc) ?? DateTime.UtcNow;

        // Repeated from the validator because it is a domain rule, not a shape rule: the status is
        // read off the latest milestone, so a future-dated one becomes the shipment's current
        // position immediately — and both the buyer and the public link would then report the goods
        // as being somewhere they are not.
        if (occurredAt > DateTime.UtcNow)
        {
            throw ApiException.BadRequest(
                "MILESTONE_IN_FUTURE", "A tracking event cannot have happened in the future.");
        }

        var milestone = new ShipmentTrackingEvent
        {
            ShipmentId = shipment.Id,
            Status = request.Status,
            Location = Blank(request.Location),
            Description = Blank(request.Description),
            OccurredAtUtc = occurredAt,
            RecordedByUserId = userId,
        };

        db.ShipmentTrackingEvents.Add(milestone);

        // Appending is not editing, so this is safe with the shipment loaded for its order: nothing
        // mutates a tracked column on the shipment row itself.
        db.Notifications.Add(new Notification
        {
            UserId = order.BuyerUserId,
            Type = NotificationType.Shipment,
            Title = $"Shipment {shipment.Reference}: {Narrative(request.Status)}",
            Body = string.IsNullOrWhiteSpace(milestone.Location)
                ? $"A new tracking event was recorded for {shipment.Reference}."
                : $"{milestone.Location} — {Narrative(request.Status)}.",
            Link = $"/dashboard/orders/{shipment.OrderId}",
        });

        await db.SaveChangesAsync(ct);

        var names = await DisplayNamesAsync([userId], ct);
        return ToEventDto(milestone, names.GetValueOrDefault(userId));
    }

    public async Task<TrackingTokenDto> RotateTrackingTokenAsync(
        Guid shipmentId, Guid userId, CancellationToken ct = default)
    {
        var (shipment, _) = await RequireSellerShipmentAsync(shipmentId, userId, ct, "rotate the guest tracking link for");

        // The previous link stops working the moment this saves. That is the point: a tracking URL
        // forwarded into an inbox cannot be recalled, so rotation has to be a real kill switch
        // rather than a way to get a second copy of the same secret.
        var (raw, hash) = OpaqueTokenFactory.Create();
        shipment.PublicTrackingTokenHash = hash;

        await db.SaveChangesAsync(ct);

        return new TrackingTokenDto
        {
            ShipmentId = shipment.Id,
            Token = raw,
            TrackingPath = $"/track/{raw}",
        };
    }

    public async Task<ShipmentPublicTrackingDto> TrackAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw ApiException.NotFound("TRACKING_NOT_FOUND", "That tracking link is not valid.");
        }

        // Looked up by hash, never by the raw value: the raw token is not in the database, so a
        // dump cannot be walked to enumerate live tracking links.
        var hash = OpaqueTokenFactory.Hash(token.Trim());

        var shipment = await db.Shipments
            .AsNoTracking()
            .Include(s => s.Events)
            .FirstOrDefaultAsync(s => s.PublicTrackingTokenHash == hash, ct);

        if (shipment is null)
        {
            // 404 for anything invalid, with nothing to distinguish the cases: every token is
            // either a live link or random noise, so there is no "exists but not yours" to leak.
            throw ApiException.NotFound("TRACKING_NOT_FOUND", "That tracking link is not valid.");
        }

        var latest = shipment.LatestEvent();

        return new ShipmentPublicTrackingDto
        {
            Reference = shipment.Reference,
            Status = latest?.Status ?? ShipmentStatus.Pending,
            Mode = shipment.Mode,
            CarrierName = shipment.CarrierName,
            TrackingReference = shipment.TrackingReference,
            OriginCountry = shipment.OriginCountry,
            OriginPort = shipment.OriginPort,
            DestinationCountry = shipment.DestinationCountry,
            DestinationPort = shipment.DestinationPort,
            Incoterms = shipment.Incoterms,
            DepartedAtUtc = shipment.DepartedAtUtc(),
            EstimatedArrivalUtc = shipment.EstimatedArrivalUtc,

            // Oldest first, so the page reads forwards as a journey rather than backwards. It is
            // the reverse of the party view, which leads with the current position.
            Events = Shipment.LatestFirst(shipment.Events.AsQueryable())
                .Select(e => new ShipmentPublicTrackingEventDto
                {
                    Status = e.Status,
                    Location = e.Location,
                    Description = e.Description,
                    OccurredAtUtc = e.OccurredAtUtc,
                })
                .OrderBy(e => e.OccurredAtUtc)
                .ToList(),
        };
    }

    // ---- Helpers ----

    /// <summary>
    /// Loads a shipment and its order for writing, refusing anyone who is not the seller.
    /// </summary>
    /// <remarks>
    /// The two refusals are deliberately different. A stranger gets 404, because the order behind
    /// the shipment is itself private and confirming it exists is already a leak. A participant
    /// who is simply on the wrong side gets 403, because they know the order is theirs and telling
    /// them "this is the seller's job" is more useful than pretending otherwise.
    /// </remarks>
    /// <returns>The shipment and its order, which the caller always needs and which cannot be null.</returns>
    private async Task<(Shipment Shipment, Order Order)> RequireSellerShipmentAsync(
        Guid shipmentId, Guid userId, CancellationToken ct, string action)
    {
        var shipment = await db.Shipments
            .Include(s => s.Order)
            .FirstOrDefaultAsync(s => s.Id == shipmentId, ct);

        var order = shipment?.Order;
        var party = order is null ? OrderParty.Outsider : await PartyAsync(order, userId, ct);

        if (shipment is null || order is null || party == OrderParty.Outsider)
        {
            throw ApiException.NotFound("SHIPMENT_NOT_FOUND", "Shipment not found.");
        }

        if (party != OrderParty.Seller)
        {
            throw ApiException.Forbidden("NOT_SELLER", $"Only the seller can {action} this shipment.");
        }

        return (shipment, order);
    }

    /// <summary>Which side of the trade the caller is on. A stranger is an Outsider, not a 403.</summary>
    private async Task<OrderParty> PartyAsync(Order order, Guid userId, CancellationToken ct)
    {
        if (order.BuyerUserId == userId)
        {
            return OrderParty.Buyer;
        }

        return await db.Businesses
            .AsNoTracking()
            .AnyAsync(b => b.Id == order.SellerBusinessId && b.OwnerUserId == userId, ct)
            ? OrderParty.Seller
            : OrderParty.Outsider;
    }

    private async Task<List<Guid>> SellerBusinessIdsAsync(Guid userId, CancellationToken ct) =>
        await db.Businesses
            .AsNoTracking()
            .Where(b => b.OwnerUserId == userId)
            .Select(b => b.Id)
            .ToListAsync(ct);

    /// <summary>
    /// Resolves the first milestone's status. Free text because the caller may send a name or a
    /// number; anything unrecognised is refused with the list of what is allowed, rather than
    /// silently becoming "Pending" and quietly misstating where the goods are.
    /// </summary>
    private static ShipmentStatus ResolveInitialStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ShipmentStatus.Booked;
        }

        if (Enum.TryParse<ShipmentStatus>(raw.Trim(), ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw ApiException.BadRequest(
            "INVALID_SHIPMENT_STATUS",
            $"'{raw}' is not a shipment status. Use one of: {string.Join(", ", Enum.GetNames<ShipmentStatus>())}.");
    }

    /// <summary>Plain-language milestone, for a notification title the buyer reads at a glance.</summary>
    private static string Narrative(ShipmentStatus status) => status switch
    {
        ShipmentStatus.Pending => "Booking pending",
        ShipmentStatus.Booked => "Booked with the carrier",
        ShipmentStatus.Departed => "Departed",
        ShipmentStatus.InTransit => "In transit",
        ShipmentStatus.Arrived => "Arrived at destination",
        ShipmentStatus.Delivered => "Delivered",
        ShipmentStatus.Exception => "Exception reported",
        _ => status.ToString(),
    };

    /// <summary>
    /// Builds DTOs for a page of shipments, gathering order numbers, party and milestone recorders
    /// in a fixed number of round trips rather than per shipment.
    /// </summary>
    /// <remarks>
    /// Every caller must arrive with <c>shipment.Events</c> loaded, or with it populated in memory.
    /// The derived reads — <c>LatestEvent</c> and <c>DepartedAtUtc</c> — read that collection, so a
    /// shipment without its timeline reports no status. Keeping the entity's own collection as the
    /// single timeline is what stops that happening by accident.
    /// </remarks>
    private async Task<List<ShipmentDto>> ProjectAsync(List<Shipment> shipments, Guid userId, CancellationToken ct)
    {
        if (shipments.Count == 0)
        {
            return [];
        }

        var orders = await db.Orders
            .AsNoTracking()
            .Where(o => shipments.Select(s => s.OrderId).Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, ct);

        var recorderIds = shipments
            .SelectMany(s => s.Events)
            .Select(e => e.RecordedByUserId)
            .Distinct()
            .ToList();
        var recorderNames = await DisplayNamesAsync(recorderIds, ct);

        var sellerBusinessIds = await SellerBusinessIdsAsync(userId, ct);
        var myBusinesses = sellerBusinessIds.ToHashSet();

        var result = new List<ShipmentDto>(shipments.Count);

        foreach (var shipment in shipments)
        {
            var order = orders.GetValueOrDefault(shipment.OrderId);
            var latest = shipment.LatestEvent();

            result.Add(new ShipmentDto
            {
                Id = shipment.Id,
                OrderId = shipment.OrderId,
                OrderNumber = order?.OrderNumber ?? string.Empty,
                Reference = shipment.Reference,
                Status = latest?.Status ?? ShipmentStatus.Pending,
                StatusSinceUtc = latest?.OccurredAtUtc,
                Mode = shipment.Mode,
                CarrierName = shipment.CarrierName,
                TrackingReference = shipment.TrackingReference,
                OriginCountry = shipment.OriginCountry,
                OriginPort = shipment.OriginPort,
                DestinationCountry = shipment.DestinationCountry,
                DestinationPort = shipment.DestinationPort,
                Incoterms = shipment.Incoterms,
                DepartedAtUtc = shipment.DepartedAtUtc(),
                EstimatedArrivalUtc = shipment.EstimatedArrivalUtc,

                // Whether a link exists — never the link. The raw token was shown once at creation
                // and cannot be recovered, so a seller who loses it rotates.
                HasGuestTracking = shipment.PublicTrackingTokenHash is not null,

                // Resolved server-side from real ownership, as on order detail: the client is not
                // trusted to decide which side of the trade it is showing actions for.
                IsSeller = order is not null && myBusinesses.Contains(order.SellerBusinessId),
                IsBuyer = order?.BuyerUserId == userId,

                // Newest first: a timeline is read from the present backwards.
                Events = Shipment.LatestFirst(shipment.Events.AsQueryable())
                    .Select(e => ToEventDto(e, recorderNames.GetValueOrDefault(e.RecordedByUserId)))
                    .ToList(),
            });
        }

        return result;
    }

    private static ShipmentTrackingEventDto ToEventDto(ShipmentTrackingEvent e, string? recorder) => new()
    {
        Id = e.Id,
        ShipmentId = e.ShipmentId,
        Status = e.Status,
        Location = e.Location,
        Description = e.Description,
        OccurredAtUtc = e.OccurredAtUtc,
        RecordedAtUtc = e.CreatedAtUtc ?? e.OccurredAtUtc,
        RecordedByDisplayName = recorder,
    };

    /// <summary>Resolves display names for a set of user ids in one round trip.</summary>
    private async Task<Dictionary<Guid, string>> DisplayNamesAsync(List<Guid> ids, CancellationToken ct) =>
        ids.Count == 0
            ? []
            : await db.Users
                .AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

    /// <summary>Per-year prefix, matching the RFQ, order and payment number scheme.</summary>
    private static string ShipmentReferencePrefix() => $"SHP-{DateTime.UtcNow.Year}-";

    private static string? Coalesce(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Normalises a client-supplied timestamp to UTC.
    /// </summary>
    /// <remarks>
    /// A raw <see cref="DateTime"/> comparison ignores <see cref="DateTime.Kind"/>, so an
    /// <c>OccurredAtUtc</c> of "14:00:00+01:00" reads as an hour later than a genuine 13:00Z —
    /// late enough for a stale milestone to become the shipment's current status, which is the one
    /// thing the latest-milestone rule must never do. Converting first makes the ordering mean what
    /// it says. Unspecified is taken as UTC, the convention gateways and carriers send.
    /// </remarks>
    private static DateTime? ToUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } => value,
        { Kind: DateTimeKind.Local } => value.Value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
    };

    /// <summary>SQL Server unique-index violation, which the one-shipment-per-order guard relies on.</summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 };
}