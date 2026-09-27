namespace AgroTrade.Domain.Enums;

/// <summary>
/// Platform roles (spec §6). A user may hold multiple roles (e.g. Farmer + Exporter).
/// Values are stored as Identity role names.
/// </summary>
public static class RoleNames
{
    public const string Buyer = "Buyer";
    public const string Farmer = "Farmer";
    public const string Supplier = "Supplier";
    public const string AgroProcessor = "AgroProcessor";
    public const string Exporter = "Exporter";
    public const string Importer = "Importer";
    public const string Distributor = "Distributor";
    public const string LogisticsProvider = "LogisticsProvider";
    public const string Administrator = "Administrator";
    /// <summary>
    /// The platform's single supplier account (Achievers Global Consult). Only this
    /// role may list products and quote on RFQs (single-supplier model).
    /// </summary>
    public const string Company = "Company";

    /// <summary>All roles seeded on startup (spec §6).</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Buyer,
        Farmer,
        Supplier,
        AgroProcessor,
        Exporter,
        Importer,
        Distributor,
        LogisticsProvider,
        Company,
        Administrator,
    ];

    /// <summary>Roles a user may self-select at registration (buyers/importers only).</summary>
    public static readonly IReadOnlyList<string> Registrable =
    [
        Buyer,
        Importer,
        Distributor,
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
        Importer,
        Distributor,
    ];
}