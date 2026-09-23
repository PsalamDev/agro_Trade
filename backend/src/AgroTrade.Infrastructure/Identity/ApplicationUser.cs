using Microsoft.AspNetCore.Identity;

namespace AgroTrade.Infrastructure.Identity;

/// <summary>
/// Application user (ASP.NET Core Identity with Guid keys), extended with business
/// columns per plan §02.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>Public display name (e.g. business trading contact).</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>ISO-3166 alpha-2 home country, e.g. "NG".</summary>
    public string? HomeCountry { get; set; }

    /// <summary>ISO-4217 default currency.</summary>
    public string DefaultCurrency { get; set; } = "NGN";

    /// <summary>Admin suspension flag — checked on token validation and login.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}