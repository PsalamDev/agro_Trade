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
        Administrator,
    ];

    /// <summary>Roles a user may self-select at registration.</summary>
    public static readonly IReadOnlyList<string> Registrable =
    [
        Buyer,
        Farmer,
        Supplier,
        AgroProcessor,
        Exporter,
        Importer,
        Distributor,
        LogisticsProvider,
    ];

    /// <summary>Seller-side roles — own products/listings and can quote RFQs.</summary>
    public static readonly IReadOnlyList<string> SellerRoles =
    [
        Farmer,
        Supplier,
        AgroProcessor,
        Exporter,
        Distributor,
    ];

    /// <summary>Buyer-side roles — can publish RFQs and place orders.</summary>
    public static readonly IReadOnlyList<string> BuyerRoles =
    [
        Buyer,
        Importer,
        Distributor,
    ];
}