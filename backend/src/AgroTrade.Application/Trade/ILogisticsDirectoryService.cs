using AgroTrade.Application.Common;

namespace AgroTrade.Application.Trade;

/// <summary>
/// The logistics-provider directory and the bookings that link a provider to an order (Phase 4 —
/// LSP directory + booking).
/// </summary>
/// <remarks>
/// Finding a provider and booking one are the same capability seen from two ends, so they share a
/// service. The directory is read by anyone; a profile is written by the provider that owns it; a
/// booking is raised by a party to an order and answered by the provider.
/// </remarks>
public interface ILogisticsDirectoryService
{
    /// <summary>Directory search. Active providers only; anonymous and safe to cache.</summary>
    Task<PagedResult<LogisticsProviderCardDto>> SearchProvidersAsync(
        LogisticsProviderQuery query, CancellationToken ct = default);

    /// <summary>
    /// A provider's full profile. Authenticated. A deactivated provider is still readable by its
    /// owner, so the edit form can offer to re-activate it.
    /// </summary>
    Task<LogisticsProviderDetailDto> GetProviderAsync(
        Guid providerId, Guid userId, bool isAdmin = false, CancellationToken ct = default);

    /// <summary>The caller's own provider profile, or null if they have not created one yet.</summary>
    Task<LogisticsProviderDetailDto?> GetMyProviderAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Creates or replaces the caller's own profile. Provider role only.</summary>
    Task<LogisticsProviderDetailDto> UpsertMyProviderAsync(
        Guid userId, UpsertLogisticsProviderRequest request, CancellationToken ct = default);

    /// <summary>
    /// Raises a booking request against an order. Either party to the order may ask — the buyer to
    /// arrange carriage, the seller to engage a forwarder — but neither may book on behalf of an
    /// order they are not part of, and a provider cannot book itself.
    /// </summary>
    Task<LogisticsBookingDto> RequestBookingAsync(
        Guid orderId, Guid userId, CreateLogisticsBookingRequest request, CancellationToken ct = default);

    /// <summary>
    /// The bookings on an order. Visible to both order parties and to the provider the booking
    /// names; a stranger gets a 404, because the order itself is private.
    /// </summary>
    Task<IReadOnlyList<LogisticsBookingDto>> ListBookingsForOrderAsync(
        Guid orderId, Guid userId, bool isAdmin = false, CancellationToken ct = default);

    /// <summary>Every booking the caller raised or was named in, newest first.</summary>
    Task<PagedResult<LogisticsBookingDto>> ListMyBookingsAsync(
        Guid userId, LogisticsBookingQuery query, CancellationToken ct = default);

    /// <summary>A single booking, for a party to it.</summary>
    Task<LogisticsBookingDto> GetBookingAsync(
        Guid bookingId, Guid userId, bool isAdmin = false, CancellationToken ct = default);

    /// <summary>Accepts or declines an open request. The named provider only.</summary>
    Task<LogisticsBookingDto> RespondAsync(
        Guid bookingId, Guid userId, RespondToLogisticsBookingRequest request, CancellationToken ct = default);

    /// <summary>Withdraws an un-answered request. The requester only.</summary>
    Task<LogisticsBookingDto> CancelAsync(
        Guid bookingId, Guid userId, CancellationToken ct = default);

    /// <summary>Marks an accepted booking as fulfilled. The requester only.</summary>
    Task<LogisticsBookingDto> CompleteAsync(
        Guid bookingId, Guid userId, CancellationToken ct = default);
}
