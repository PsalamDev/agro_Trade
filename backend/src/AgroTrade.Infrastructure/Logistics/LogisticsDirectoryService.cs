using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using AgroTrade.Domain.Communication;
using AgroTrade.Domain.Enums;
using AgroTrade.Domain.Trade;
using AgroTrade.Infrastructure.Data;
using AgroTrade.Infrastructure.Trade;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Infrastructure.Logistics;

/// <summary>
/// The logistics-provider directory and the bookings that link a provider to an order (Phase 4 —
/// LSP directory + booking).
/// </summary>
/// <remarks>
/// <para>
/// Two capabilities, one service. The directory is read by anyone and written only by the provider
/// that owns the profile; a booking is raised by a party to an order and answered by the provider
/// it names. Keeping them together means the rule "you can only book a provider that is listed"
/// lives next to the code that lists providers, rather than being asserted at the boundary.
/// </para>
/// <para>
/// A booking is deliberately not a <see cref="Shipment"/>. The shipment is the operational record
/// the seller keeps once goods move; the booking is the commercial agreement that comes first, and
/// it is the one either party to the order may initiate.
/// </para>
/// </remarks>
public sealed class LogisticsDirectoryService(AgroTradeDbContext db) : ILogisticsDirectoryService
{
    // ---- Directory ----

    public async Task<PagedResult<LogisticsProviderCardDto>> SearchProvidersAsync(
        LogisticsProviderQuery query, CancellationToken ct = default)
    {
        var (page, pageSize) = PageHelper.Normalize(query.Page, query.PageSize);

        // Only active providers are listed. A provider the owner has withdrawn is invisible to the
        // directory but still readable by its owner, so the edit form can re-activate it.
        var providers = db.LogisticsProviders.AsNoTracking().Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(query.Country))
        {
            var country = query.Country.Trim().ToUpperInvariant();
            providers = providers.Where(p => p.Coverage.Any(c => c.Country == country));
        }

        if (query.Mode is { } mode)
        {
            var bit = ShipmentModeMask.Bit(mode);
            providers = providers.Where(p => (p.Modes & bit) != 0);
        }

        if (query.Service is { } service && service != LogisticsService.None)
        {
            var bits = (int)service;
            providers = providers.Where(p => (p.Services & bits) != 0);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            providers = providers.Where(p =>
                p.CompanyName.Contains(term)
                || (p.Description != null && p.Description.Contains(term)));
        }

        var total = await providers.CountAsync(ct);

        var rows = await providers
            .Include(p => p.Coverage)
            // Verified first, then the best-rated: a buyer scanning the directory should meet the
            // providers the platform has stood behind before those it has not.
            .OrderByDescending(p => p.IsVerified)
            .ThenByDescending(p => p.Rating)
            .ThenBy(p => p.CompanyName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<LogisticsProviderCardDto>
        {
            Items = rows.Select(ToCard).ToList(),
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }

    public async Task<LogisticsProviderDetailDto> GetProviderAsync(
        Guid providerId, Guid userId, bool isAdmin = false, CancellationToken ct = default)
    {
        var provider = await db.LogisticsProviders
            .AsNoTracking()
            .Include(p => p.Coverage)
            .FirstOrDefaultAsync(p => p.Id == providerId, ct);

        // A withdrawn provider is not found by anyone but its owner and an admin — the same
        // "invisible rather than forbidden" rule the rest of the platform uses.
        if (provider is null || (!provider.IsActive && !isAdmin && provider.OwnerUserId != userId))
        {
            throw ApiException.NotFound("PROVIDER_NOT_FOUND", "Logistics provider not found.");
        }

        return ToDetail(provider, userId);
    }

    public async Task<LogisticsProviderDetailDto?> GetMyProviderAsync(
        Guid userId, CancellationToken ct = default)
    {
        var provider = await db.LogisticsProviders
            .AsNoTracking()
            .Include(p => p.Coverage)
            .FirstOrDefaultAsync(p => p.OwnerUserId == userId, ct);

        return provider is null ? null : ToDetail(provider, userId);
    }

    public async Task<LogisticsProviderDetailDto> UpsertMyProviderAsync(
        Guid userId, UpsertLogisticsProviderRequest request, CancellationToken ct = default)
    {
        var provider = await db.LogisticsProviders
            .Include(p => p.Coverage)
            .FirstOrDefaultAsync(p => p.OwnerUserId == userId, ct);

        var isNew = provider is null;
        if (provider is null)
        {
            provider = new LogisticsProvider { OwnerUserId = userId };
            db.LogisticsProviders.Add(provider);
        }

        provider.CompanyName = request.CompanyName.Trim();
        provider.Description = Blank(request.Description);
        provider.HomeCountry = request.HomeCountry.Trim().ToUpperInvariant();
        provider.ContactEmail = Blank(request.ContactEmail);
        provider.ContactPhone = Blank(request.ContactPhone);
        provider.Website = Blank(request.Website);
        provider.Modes = ShipmentModeMask.ToMask(request.Modes);
        provider.Services = ToServiceMask(request.Services);
        provider.IsActive = request.IsActive;

        // Coverage is replaced wholesale. It is a small set the client owns completely, and
        // diffing it row by row would be more code and more ways to leave a stale country behind.
        // The rows are removed and re-added explicitly rather than through the tracked collection,
        // so EF sees a plain delete-and-insert rather than a mutated navigation.
        var desired = NormalizeCountries(request.Coverage);
        var existing = await db.LogisticsProviderCountries
            .Where(c => c.LogisticsProviderId == provider.Id)
            .ToListAsync(ct);
        db.LogisticsProviderCountries.RemoveRange(existing);
        foreach (var country in desired)
        {
            db.LogisticsProviderCountries.Add(new LogisticsProviderCountry
            {
                LogisticsProviderId = provider.Id,
                Country = country,
            });
        }

        await db.SaveChangesAsync(ct);

        // The caller owns the profile, so the visibility check passes on ownership rather than on
        // an admin override — the read is the same one any owner gets.
        return await GetProviderAsync(provider.Id, userId, isAdmin: false, ct);
    }

    // ---- Bookings ----

    public async Task<LogisticsBookingDto> RequestBookingAsync(
        Guid orderId, Guid userId, CreateLogisticsBookingRequest request, CancellationToken ct = default)
    {
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);

        // An order the caller has no part in is a 404, not a 403: its existence is itself private.
        var party = order is null ? OrderParty.Outsider : await PartyAsync(order, userId, ct);
        if (party == OrderParty.Outsider)
        {
            throw ApiException.NotFound("ORDER_NOT_FOUND", "Order not found.");
        }

        // Carriage is arranged against a live order. A draft has committed to nothing, and a
        // cancelled order has been unwound.
        if (order!.Status is OrderStatus.Draft or OrderStatus.Cancelled)
        {
            throw ApiException.Conflict(
                "ORDER_CLOSED",
                $"Logistics cannot be booked against a {(order.Status == OrderStatus.Draft ? "draft" : "cancelled")} order.");
        }

        var provider = await db.LogisticsProviders
            .FirstOrDefaultAsync(p => p.Id == request.ProviderId, ct);

        // Only a listed provider can be booked. Booking a withdrawn one would put an order in the
        // hands of a provider the directory has already taken down.
        if (provider is null || !provider.IsActive)
        {
            throw ApiException.NotFound("PROVIDER_NOT_FOUND", "Logistics provider not found.");
        }

        // One open or agreed booking per provider per order. A second request to the same provider
        // is a duplicate, not a second leg.
        if (await db.LogisticsBookings.AnyAsync(b =>
                b.OrderId == orderId
                && b.LogisticsProviderId == provider.Id
                && (b.Status == LogisticsBookingStatus.Requested
                    || b.Status == LogisticsBookingStatus.Accepted), ct))
        {
            throw ApiException.Conflict(
                "BOOKING_EXISTS",
                "This provider already has an open booking for this order.");
        }

        var booking = new LogisticsBooking
        {
            OrderId = order.Id,
            LogisticsProviderId = provider.Id,
            RequestedByUserId = userId,
            Mode = request.Mode,
            Status = LogisticsBookingStatus.Requested,
            Notes = Blank(request.Notes),
        };

        db.LogisticsBookings.Add(booking);

        // The reference is only knowable immediately before the insert; the read that picks it has
        // to be locked against the insert that uses it. The provider's notification commits with
        // the booking, so the two can never disagree about whether the request was raised.
        await SequenceAllocator.AllocateAsync(
            db,
            table: "LogisticsBookings",
            column: "Reference",
            prefix: BookingReferencePrefix(),
            assign: number => booking.Reference = number,
            save: async token =>
            {
                db.Notifications.Add(new Notification
                {
                    UserId = provider.OwnerUserId,
                    Type = NotificationType.Logistics,
                    Title = $"Logistics request {booking.Reference}",
                    Body = $"{order.OrderNumber}: a {ModeLabel(request.Mode)} booking has been requested.",
                    Link = "/logistics/bookings",
                });

                await db.SaveChangesAsync(token);
            },
            ct);

        return await GetBookingAsync(booking.Id, userId, isAdmin: false, ct);
    }

    public async Task<IReadOnlyList<LogisticsBookingDto>> ListBookingsForOrderAsync(
        Guid orderId, Guid userId, bool isAdmin = false, CancellationToken ct = default)
    {
        // Participantship is decided against the order, not the booking, so an order with no
        // bookings still 404s for a stranger rather than returning an empty list that quietly
        // confirms the order exists.
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null || (!isAdmin && await PartyAsync(order, userId, ct) == OrderParty.Outsider))
        {
            throw ApiException.NotFound("ORDER_NOT_FOUND", "Order not found.");
        }

        var bookings = await db.LogisticsBookings
            .AsNoTracking()
            .Include(b => b.Order)
            .Include(b => b.Provider)
            .Where(b => b.OrderId == orderId)
            .OrderByDescending(b => b.CreatedAtUtc)
            .ToListAsync(ct);

        return await ProjectBookingsAsync(bookings, userId, ct);
    }

    public async Task<PagedResult<LogisticsBookingDto>> ListMyBookingsAsync(
        Guid userId, LogisticsBookingQuery query, CancellationToken ct = default)
    {
        var (page, pageSize) = PageHelper.Normalize(query.Page, query.PageSize);

        var providerIds = await db.LogisticsProviders
            .AsNoTracking()
            .Where(p => p.OwnerUserId == userId)
            .Select(p => p.Id)
            .ToListAsync(ct);

        var bookings = db.LogisticsBookings
            .AsNoTracking()
            .Include(b => b.Order)
            .Include(b => b.Provider)
            .Where(b => b.RequestedByUserId == userId || providerIds.Contains(b.LogisticsProviderId));

        if (query.Status is { } status)
        {
            bookings = bookings.Where(b => b.Status == status);
        }

        var total = await bookings.CountAsync(ct);

        var rows = await bookings
            .OrderByDescending(b => b.CreatedAtUtc)
            .ThenByDescending(b => b.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<LogisticsBookingDto>
        {
            Items = await ProjectBookingsAsync(rows, userId, ct),
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }

    public async Task<LogisticsBookingDto> GetBookingAsync(
        Guid bookingId, Guid userId, bool isAdmin = false, CancellationToken ct = default)
    {
        var booking = await db.LogisticsBookings
            .AsNoTracking()
            .Include(b => b.Order)
            .Include(b => b.Provider)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking is null || (!isAdmin && !await CanSeeAsync(booking, userId, ct)))
        {
            throw ApiException.NotFound("BOOKING_NOT_FOUND", "Booking not found.");
        }

        return (await ProjectBookingsAsync([booking], userId, ct))[0];
    }

    public async Task<LogisticsBookingDto> RespondAsync(
        Guid bookingId, Guid userId, RespondToLogisticsBookingRequest request, CancellationToken ct = default)
    {
        var booking = await LoadForWriteAsync(bookingId, ct);

        if (booking.Provider?.OwnerUserId != userId)
        {
            throw ApiException.Forbidden(
                "NOT_PROVIDER", "Only the booked provider can answer this request.");
        }

        if (booking.Status != LogisticsBookingStatus.Requested)
        {
            throw ApiException.Conflict(
                "BOOKING_CLOSED", $"This request has already been {StatusLabel(booking.Status)}.");
        }

        booking.Status = request.Accept
            ? LogisticsBookingStatus.Accepted
            : LogisticsBookingStatus.Declined;
        booking.ResponseNotes = Blank(request.Notes);
        booking.DecidedAtUtc = DateTime.UtcNow;

        db.Notifications.Add(new Notification
        {
            UserId = booking.RequestedByUserId,
            Type = NotificationType.Logistics,
            Title = request.Accept
                ? $"Logistics request {booking.Reference} accepted"
                : $"Logistics request {booking.Reference} declined",
            Body = request.Accept
                ? $"{booking.Provider.CompanyName} agreed to carry this order."
                : $"{booking.Provider.CompanyName} declined this request.",
            Link = $"/dashboard/orders/{booking.OrderId}",
        });

        await db.SaveChangesAsync(ct);

        return await GetBookingAsync(bookingId, userId, isAdmin: false, ct);
    }

    public async Task<LogisticsBookingDto> CancelAsync(
        Guid bookingId, Guid userId, CancellationToken ct = default)
    {
        var booking = await LoadForWriteAsync(bookingId, ct);

        if (booking.RequestedByUserId != userId)
        {
            throw ApiException.Forbidden("NOT_REQUESTER", "Only the requester can withdraw this request.");
        }

        if (booking.Status != LogisticsBookingStatus.Requested)
        {
            throw ApiException.Conflict(
                "BOOKING_CLOSED", $"This request has already been {StatusLabel(booking.Status)}.");
        }

        booking.Status = LogisticsBookingStatus.Cancelled;
        booking.DecidedAtUtc = DateTime.UtcNow;

        if (booking.Provider is not null)
        {
            db.Notifications.Add(new Notification
            {
                UserId = booking.Provider.OwnerUserId,
                Type = NotificationType.Logistics,
                Title = $"Logistics request {booking.Reference} withdrawn",
                Body = "The requester withdrew this booking request.",
                Link = "/logistics/bookings",
            });
        }

        await db.SaveChangesAsync(ct);

        return await GetBookingAsync(bookingId, userId, isAdmin: false, ct);
    }

    public async Task<LogisticsBookingDto> CompleteAsync(
        Guid bookingId, Guid userId, CancellationToken ct = default)
    {
        var booking = await LoadForWriteAsync(bookingId, ct);

        if (booking.RequestedByUserId != userId)
        {
            throw ApiException.Forbidden("NOT_REQUESTER", "Only the requester can close this booking.");
        }

        if (booking.Status != LogisticsBookingStatus.Accepted)
        {
            throw ApiException.Conflict(
                "BOOKING_NOT_ACCEPTED", "Only an accepted booking can be completed.");
        }

        booking.Status = LogisticsBookingStatus.Completed;

        await db.SaveChangesAsync(ct);

        return await GetBookingAsync(bookingId, userId, isAdmin: false, ct);
    }

    // ---- Helpers ----

    private async Task<LogisticsBooking> LoadForWriteAsync(Guid bookingId, CancellationToken ct)
    {
        var booking = await db.LogisticsBookings
            .Include(b => b.Provider)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking is null)
        {
            throw ApiException.NotFound("BOOKING_NOT_FOUND", "Booking not found.");
        }

        return booking;
    }

    /// <summary>Whether the caller may read a booking: a party to it or a party to its order.</summary>
    private async Task<bool> CanSeeAsync(LogisticsBooking booking, Guid userId, CancellationToken ct)
    {
        if (booking.RequestedByUserId == userId || booking.Provider?.OwnerUserId == userId)
        {
            return true;
        }

        return booking.Order is not null
            && await PartyAsync(booking.Order, userId, ct) != OrderParty.Outsider;
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

    /// <summary>
    /// Builds booking DTOs for a page of bookings, resolving requester names in one round trip.
    /// Every booking must arrive with <c>Order</c> and <c>Provider</c> loaded.
    /// </summary>
    private async Task<List<LogisticsBookingDto>> ProjectBookingsAsync(
        List<LogisticsBooking> bookings, Guid userId, CancellationToken ct)
    {
        if (bookings.Count == 0)
        {
            return [];
        }

        var requesterIds = bookings.Select(b => b.RequestedByUserId).Distinct().ToList();
        var names = await db.Users
            .AsNoTracking()
            .Where(u => requesterIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        return bookings.Select(b => new LogisticsBookingDto
        {
            Id = b.Id,
            Reference = b.Reference,
            OrderId = b.OrderId,
            OrderNumber = b.Order?.OrderNumber ?? string.Empty,
            OrderDestinationCountry = b.Order?.DestinationCountry,
            ProviderId = b.LogisticsProviderId,
            ProviderName = b.Provider?.CompanyName ?? string.Empty,
            ProviderHomeCountry = b.Provider?.HomeCountry ?? string.Empty,
            RequestedByUserId = b.RequestedByUserId,
            RequestedByDisplayName = names.GetValueOrDefault(b.RequestedByUserId) ?? string.Empty,
            Mode = b.Mode,
            Status = b.Status,
            Notes = b.Notes,
            ResponseNotes = b.ResponseNotes,
            DecidedAtUtc = b.DecidedAtUtc,
            IsRequester = b.RequestedByUserId == userId,
            IsProvider = b.Provider?.OwnerUserId == userId,
            CreatedAtUtc = b.CreatedAtUtc,
        }).ToList();
    }

    private static LogisticsProviderCardDto ToCard(LogisticsProvider p) => new()
    {
        Id = p.Id,
        CompanyName = p.CompanyName,
        Description = p.Description,
        HomeCountry = p.HomeCountry,
        Modes = p.ModeList,
        Services = p.ServiceList,
        Coverage = p.Coverage.Select(c => c.Country).OrderBy(c => c).ToList(),
        IsVerified = p.IsVerified,
        Rating = p.Rating,
        RatingCount = p.RatingCount,
    };

    private static LogisticsProviderDetailDto ToDetail(LogisticsProvider p, Guid userId) => new()
    {
        Id = p.Id,
        CompanyName = p.CompanyName,
        Description = p.Description,
        HomeCountry = p.HomeCountry,
        Modes = p.ModeList,
        Services = p.ServiceList,
        Coverage = p.Coverage.Select(c => c.Country).OrderBy(c => c).ToList(),
        IsVerified = p.IsVerified,
        Rating = p.Rating,
        RatingCount = p.RatingCount,
        ContactEmail = p.ContactEmail,
        ContactPhone = p.ContactPhone,
        Website = p.Website,
        IsActive = p.IsActive,
        IsMine = p.OwnerUserId == userId,
        CreatedAtUtc = p.CreatedAtUtc,
        UpdatedAtUtc = p.UpdatedAtUtc,
    };

    private static int ToServiceMask(IEnumerable<LogisticsService>? services)
    {
        var mask = 0;
        foreach (var service in services ?? [])
        {
            if (service != LogisticsService.None && Enum.IsDefined(service))
            {
                mask |= (int)service;
            }
        }

        return mask;
    }

    private static List<string> NormalizeCountries(IEnumerable<string>? countries) =>
        (countries ?? [])
            .Select(c => c?.Trim().ToUpperInvariant())
            .Where(c => !string.IsNullOrWhiteSpace(c) && c!.Length == 2)
            .Distinct(StringComparer.Ordinal)
            .Cast<string>()
            .ToList();

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Per-year prefix, matching the RFQ, order, payment and shipment number scheme.</summary>
    private static string BookingReferencePrefix() => $"LSP-{DateTime.UtcNow.Year}-";

    private static string ModeLabel(ShipmentMode mode) => mode switch
    {
        ShipmentMode.Road => "road",
        ShipmentMode.Rail => "rail",
        ShipmentMode.Sea => "sea",
        ShipmentMode.Air => "air",
        ShipmentMode.Multimodal => "multimodal",
        _ => mode.ToString(),
    };

    private static string StatusLabel(LogisticsBookingStatus status) => status switch
    {
        LogisticsBookingStatus.Requested => "requested",
        LogisticsBookingStatus.Accepted => "accepted",
        LogisticsBookingStatus.Declined => "declined",
        LogisticsBookingStatus.Cancelled => "withdrawn",
        LogisticsBookingStatus.Completed => "completed",
        _ => status.ToString(),
    };
}
