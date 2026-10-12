using AgroTrade.Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace AgroTrade.Infrastructure.Identity;

/// <summary>
/// Application user (ASP.NET Core Identity with Guid keys), extended with business
/// columns per plan §02.
/// </summary>
/// <remarks>
/// Opts into <see cref="IAuditableEntity"/> so the DbContext's audit-field stamper fills in the two
/// timestamp properties below. They were already declared here and already persisted, but nothing
/// ever wrote them: Identity does not stamp them and this type was not an <c>IAuditableEntity</c>, so
/// every user row in the database had <c>CreatedAtUtc = NULL</c>. That is not cosmetic — anything
/// ordering users by registration (the admin user queue, task 2.7) was sorting on a constant, which
/// SQL Server resolves as NULLS FIRST on a descending sort, giving an arbitrary order that looked
/// plausible in the UI. Existing rows stay null until they are next written; only new and modified
/// users acquire timestamps.
/// </remarks>
public class ApplicationUser : IdentityUser<Guid>, IAuditableEntity
{
    /// <summary>Public display name (e.g. business trading contact).</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>ISO-3166 alpha-2 home country, e.g. "NG".</summary>
    public string? HomeCountry { get; set; }

    /// <summary>ISO-4217 default currency.</summary>
    public string DefaultCurrency { get; set; } = "NGN";

    /// <summary>
    /// Human-readable buyer account number, assigned once when a buyer registers
    /// (e.g. <c>BUY-ID-AG-000001</c>).
    /// </summary>
    /// <remarks>
    /// A public identifier, deliberately not a key: it is what a buyer quotes to support and what
    /// an administrator searches on, so it is stable and never reused. Nothing joins on it —
    /// <c>Order.BuyerUserId</c> remains the identifier used for relationships. It is null for
    /// accounts that never registered as a buyer, such as the seeded administrator and the Company
    /// supplier account.
    /// </remarks>
    public string? BuyerId { get; set; }

    /// <summary>Admin suspension flag — checked on token validation and login.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}