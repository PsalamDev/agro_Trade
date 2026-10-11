using System.ComponentModel.DataAnnotations.Schema;
using AgroTrade.Domain.Common;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Domain.Trade;

/// <summary>
/// A logistics service provider listed in the marketplace directory (Phase 4 — LSP directory +
/// booking).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a dedicated entity rather than a <see cref="Business"/> with a new
/// <see cref="BusinessType"/> value. A business is a trading party — it holds listings, sits on
/// one side of an order, and moves through the verification workflow that gates selling. An LSP
/// does none of those things: it is found, it is booked against someone else's order, and it never
/// owns inventory. Folding it into <see cref="Business"/> would mean every business query having to
/// answer "but is this one actually allowed to sell?", and the single-supplier model makes that
/// question especially costly to get wrong.
/// </para>
/// <para>
/// The profile is owned by one user who holds the <see cref="RoleNames.LogisticsProvider"/> role.
/// That role is not self-registrable (see <see cref="RoleNames.Registrable"/>), so in the MVP
/// providers are onboarded by the operator — the directory exists for them to be found in, not for
/// anyone to sign up to.
/// </para>
/// </remarks>
public sealed class LogisticsProvider : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The user who maintains this profile.</summary>
    public Guid OwnerUserId { get; set; }

    public string CompanyName { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>ISO-3166 alpha-2 country the provider is based in.</summary>
    public string HomeCountry { get; set; } = string.Empty;

    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Website { get; set; }

    /// <summary>
    /// Bitmask over <see cref="ShipmentMode"/> — the transport modes the provider handles. Stored
    /// as an int because the directory filters on membership and there is no ordering to preserve.
    /// Use <see cref="ShipmentModeMask"/> to read and write it.
    /// </summary>
    public int Modes { get; set; }

    /// <summary>Bitmask over <see cref="LogisticsService"/> — the ancillary services offered.</summary>
    public int Services { get; set; }

    /// <summary>Owner-controlled visibility. An inactive provider is hidden from the directory.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Operator-set trust mark. Separate from <see cref="IsActive"/> for the same reason a listing
    /// separates the seller's intent from the platform's judgement: withdrawing a profile and
    /// withdrawing the badge are different decisions made by different people.
    /// </summary>
    public bool IsVerified { get; set; }

    public decimal Rating { get; set; }
    public int RatingCount { get; set; }

    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>Countries the provider will serve. A separate row per country so the directory can
    /// index and filter on it rather than parsing a delimited string.</summary>
    public List<LogisticsProviderCountry> Coverage { get; set; } = [];

    public List<LogisticsBooking> Bookings { get; set; } = [];

    /// <summary>Ratings left against completed bookings with this provider.</summary>
    public List<LogisticsProviderRating> Ratings { get; set; } = [];

    [NotMapped]
    public IReadOnlyList<ShipmentMode> ModeList => ShipmentModeMask.FromMask(Modes);

    [NotMapped]
    public IReadOnlyList<LogisticsService> ServiceList =>
        Enum.GetValues<LogisticsService>()
            .Where(s => s != LogisticsService.None && (Services & (int)s) != 0)
            .ToList();
}
