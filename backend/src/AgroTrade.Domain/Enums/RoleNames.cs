namespace AgroTrade.Domain.Enums;

/// <summary>
/// Platform roles. The platform keeps to three: a buyer, the supplier/operator
/// (Company) and an administrator. A user may hold more than one.
/// </summary>
public static class RoleNames
{
    /// <summary>Buyer-side role — can publish RFQs and place orders.</summary>
    public const string Buyer = "Buyer";

    /// <summary>
    /// The supplier/operator role. It holds the single supplier business that lists
    /// products and quotes on RFQs, and also operates the logistics directory
    /// (the Phase 4 LSP portal). Held only by the platform's operator accounts.
    /// </summary>
    public const string Company = "Company";

    /// <summary>Platform administrator.</summary>
    public const string Administrator = "Administrator";

    /// <summary>All roles seeded on startup.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Buyer,
        Company,
        Administrator,
    ];

    /// <summary>Roles a user may self-select at registration (buyers only).</summary>
    public static readonly IReadOnlyList<string> Registrable =
    [
        Buyer,
    ];

    /// <summary>
    /// Roles that may act as a seller. Single-supplier model: only the Company
    /// account holds this role — no self-registered role can sell.
    /// </summary>
    public static readonly IReadOnlyList<string> SellerRoles =
    [
        Company,
    ];

    /// <summary>Buyer-side roles — can publish RFQs and place orders.</summary>
    public static readonly IReadOnlyList<string> BuyerRoles =
    [
        Buyer,
    ];
}
