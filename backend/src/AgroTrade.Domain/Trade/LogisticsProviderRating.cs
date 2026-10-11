using AgroTrade.Domain.Common;

namespace AgroTrade.Domain.Trade;

/// <summary>
/// A rating a party leaves for a logistics provider once the work is done (Phase 4 — LSP
/// directory: reputation). This is what gives <see cref="LogisticsProvider.Rating"/> and
/// <see cref="LogisticsProvider.RatingCount"/> a write path; before it they were seed-only, so the
/// directory's ranking was a fixed decoration rather than a signal.
/// </summary>
/// <remarks>
/// <para>
/// One rating per booking, written by the booking's requester after the booking is completed. The
/// booking is what earned the rating, so tying them together is what makes "you may rate this" a
/// question with a definite answer rather than a guess.
/// </para>
/// <para>
/// Deliberately not an <see cref="AgroTrade.Domain.Trust.Review"/>. A review is about a trading
/// business seen through an order, with a target business as its anchor. This is about a provider
/// seen through the leg it carried: a different anchor (a booking, not an order) and a different
/// rule for who may write it (the requester who engaged the provider, not either party).
/// </para>
/// </remarks>
public sealed class LogisticsProviderRating : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The booking being rated. Unique — one rating per booking.</summary>
    public Guid LogisticsBookingId { get; set; }

    /// <summary>
    /// The provider rated. Denormalised from the booking so a provider's ratings can be read and
    /// its aggregate recomputed without joining back through every booking.
    /// </summary>
    public Guid LogisticsProviderId { get; set; }

    /// <summary>The user who rated — the booking's requester (buyer or seller).</summary>
    public Guid RatedByUserId { get; set; }

    /// <summary>Stars, 1–5.</summary>
    public byte Rating { get; set; }

    public string? Comment { get; set; }

    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public LogisticsBooking? Booking { get; set; }
    public LogisticsProvider? Provider { get; set; }
}
