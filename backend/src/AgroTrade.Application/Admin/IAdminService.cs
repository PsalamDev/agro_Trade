using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;

namespace AgroTrade.Application.Admin;

/// <summary>
/// Admin dashboard and audit trail (task 2.7).
/// </summary>
public interface IAdminService
{
    /// <summary>
    /// Counts, funnel and corridor figures for the admin dashboard.
    /// </summary>
    Task<AdminOverviewDto> GetOverviewAsync(CancellationToken ct = default);

    /// <summary>The audit trail, newest first.</summary>
    Task<PagedResult<AdminAuditLogDto>> SearchAuditLogAsync(
        AdminAuditLogQuery query, CancellationToken ct = default);

    /// <summary>
    /// Writes the audit trail as CSV under the same filters as the list, and returns the number of
    /// rows written (the header is not counted).
    /// </summary>
    /// <remarks>
    /// Streamed rather than returned as a string: an audit trail only grows, and the whole point of
    /// taking it away is that it may be larger than a page. Writing to a <see cref="TextWriter"/>
    /// rather than straight to the response keeps the query testable without an HTTP server.
    /// </remarks>
    Task<int> ExportAuditLogCsvAsync(
        AdminAuditLogQuery query, TextWriter writer, CancellationToken ct = default);

    /// <summary>Platform users, for the admin list.</summary>
    Task<PagedResult<AdminUserDto>> SearchUsersAsync(
        AdminUserSearchQuery query, CancellationToken ct = default);

    /// <summary>Every consignment across every order, for the admin list.</summary>
    Task<PagedResult<AdminShipmentDto>> SearchShipmentsAsync(
        AdminShipmentSearchQuery query, CancellationToken ct = default);

    /// <summary>Every order across every party, for the admin list.</summary>
    Task<PagedResult<AdminOrderRowDto>> SearchOrdersAsync(
        AdminOrderSearchQuery query, CancellationToken ct = default);

    /// <summary>Every listing across every seller, for the moderation queue.</summary>
    Task<PagedResult<AdminProductRowDto>> SearchProductsAsync(
        AdminProductSearchQuery query, CancellationToken ct = default);

    /// <summary>
    /// Suspends or reinstates a listing, and records which administrator did it.
    /// </summary>
    /// <remarks>
    /// <paramref name="adminUserId"/> is passed in rather than resolved from a current-user
    /// abstraction, because a moderation action is attributed to a person and an unattributable
    /// one is not moderation. Modifies visibility only: it never touches inventory, orders, or
    /// quotations. Stock already reserved against a live order is the order state machine's
    /// business (task 2.2), and taking a listing down is not a reason to break a contract the
    /// platform has already accepted.
    /// </remarks>
    Task<AdminProductRowDto> ModerateProductAsync(
        Guid productId, Guid adminUserId, ModerateProductCommand command, CancellationToken ct = default);

    /// <summary>Every RFQ across every buyer, for the admin list.</summary>
    Task<PagedResult<AdminRfqRowDto>> SearchRfqsAsync(
        AdminRfqSearchQuery query, CancellationToken ct = default);

    /// <summary>
    /// The payments on one order, for the admin refund screen. Payments carry their own
    /// refunded totals, so an administrator can see what is still refundable per payment.
    /// </summary>
    Task<IReadOnlyList<PaymentDto>> ListOrderPaymentsAsync(
        Guid orderId, CancellationToken ct = default);

    /// <summary>Every movement of money, newest first, across every order.</summary>
    Task<PagedResult<AdminTransactionRowDto>> SearchTransactionsAsync(
        AdminTransactionSearchQuery query, CancellationToken ct = default);

    /// <summary>
    /// Writes the transaction ledger as CSV under the same filters as the list, and returns the
    /// number of rows written (the header is not counted).
    /// </summary>
    /// <remarks>
    /// The ledger is the record an operator reconciles against a bank statement, so a filtered
    /// screen is often not enough: they need the rows themselves, in a spreadsheet, without paging
    /// through the UI a screenful at a time. Same filters, same ordering, no page cap.
    /// </remarks>
    Task<int> ExportTransactionsCsvAsync(
        AdminTransactionSearchQuery query, TextWriter writer, CancellationToken ct = default);

    /// <summary>
    /// One analytics report over an optional date window: trade by corridor, RFQ volume, the
    /// verification funnel, or supply against demand by commodity.
    /// </summary>
    Task<AdminReportDto> GetReportAsync(
        AdminReportKind kind, DateTime? from, DateTime? to, CancellationToken ct = default);

    /// <summary>
    /// The operator-editable settings, every catalogue key included — stored values where they
    /// exist and the documented default where they do not.
    /// </summary>
    Task<IReadOnlyList<AdminSettingDto>> GetSettingsAsync(CancellationToken ct = default);

    /// <summary>
    /// Writes the supplied settings, leaving every other key untouched, and records who changed them.
    /// </summary>
    Task<IReadOnlyList<AdminSettingDto>> UpdateSettingsAsync(
        Guid adminUserId, AdminSettingsUpdateRequest request, CancellationToken ct = default);

    /// <summary>
    /// Suspends or reactivates an account. Suspension blocks sign-in and invalidates existing
    /// sessions; the account and its history are kept.
    /// </summary>
    /// <remarks>
    /// <paramref name="adminUserId"/> is the acting administrator, passed in and checked by the
    /// service rather than trusted from the request: an administrator must not be able to suspend
    /// their own account and lock the platform out of its own administration.
    /// </remarks>
    Task<AdminUserDto> SetUserStatusAsync(
        Guid userId, Guid adminUserId, SetUserStatusCommand command, CancellationToken ct = default);
}

/// <summary>Records an administrative action. Called explicitly by the action, never automatically.</summary>
public interface IAuditLogWriter
{
    void Record(Guid userId, string action, string entityType, Guid? entityId, string summary, string? details = null);
}

// ---- Queries ----

public sealed class AdminAuditLogQuery
{
    public string? Action { get; init; }
    public Guid? UserId { get; init; }
    public string? Q { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed class AdminUserSearchQuery
{
    public string? Q { get; init; }

    /// <summary>
    /// Role name to filter by, e.g. "Company". A name rather than a role id: role ids are
    /// database-generated, and an admin screen has no way to learn them, so an id filter would be a
    /// parameter no client could ever fill in.
    /// </summary>
    public string? Role { get; init; }

    public bool? EmailConfirmed { get; init; }
    public bool? LockedOut { get; init; }

    /// <summary>
    /// Restrict to accounts that have (true) or lack (false) a buyer account number.
    /// </summary>
    /// <remarks>
    /// The dashboard's buyer panel needs an exact count of buyer accounts, and a role filter cannot
    /// give it: the buyer account number is the one thing every buyer-side account carries, whatever
    /// role it holds.
    /// </remarks>
    public bool? HasBuyerId { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed class AdminShipmentSearchQuery
{
    public int? Status { get; init; }
    public string? Q { get; init; }

    /// <summary>
    /// Whether the consignment currently has a live guest-tracking link.
    /// </summary>
    /// <remarks>
    /// Filterable because a guest token is a bearer secret being handed to people with no account
    /// here: it cannot be listed, but an admin does need to be able to answer "which consignments
    /// have a link in circulation?" without reading every row.
    /// </remarks>
    public bool? HasGuestTracking { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed class AdminOrderSearchQuery
{
    public int? Status { get; init; }
    public int? PaymentStatus { get; init; }
    public string? Q { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed class AdminProductSearchQuery
{
    /// <summary>Seller's own visibility state.</summary>
    public int? ListingStatus { get; init; }

    /// <summary>Platform moderation state - the queue's reason for existing.</summary>
    public int? ModerationStatus { get; init; }

    public string? Q { get; init; }

    public Guid? CategoryId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed class AdminRfqSearchQuery
{
    public int? Status { get; init; }
    public string? Q { get; init; }

    /// <summary>
    /// Whether the RFQ has no quotation at all yet - demand nobody has answered.
    /// </summary>
    /// <remarks>
    /// Counted as RFQs rather than quotations, so one eager supplier quoting everything cannot
    /// make the whole board read as covered.
    /// </remarks>
    public bool? Unanswered { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed class AdminTransactionSearchQuery
{
    /// <summary>Free text: ledger reference, external reference, narrative, or order number.</summary>
    public string? Q { get; init; }

    /// <summary>Ledger entry type. Unset means every type.</summary>
    public int? Type { get; init; }

    /// <summary>ISO-4217. The ledger is per-currency and never converted, so this is a real filter.</summary>
    public string? Currency { get; init; }

    public Guid? OrderId { get; init; }

    /// <summary>Inclusive lower bound on <c>OccurredAtUtc</c>.</summary>
    public DateTime? From { get; init; }

    /// <summary>Inclusive upper bound on <c>OccurredAtUtc</c>.</summary>
    public DateTime? To { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

/// <summary>The analytics reports an administrator can pull, each over an optional date window.</summary>
public enum AdminReportKind
{
    /// <summary>Trade value per corridor per currency.</summary>
    Corridors = 0,

    /// <summary>RFQ volume: what buyers asked for, by status.</summary>
    Rfq = 1,

    /// <summary>Where businesses sit in verification.</summary>
    VerificationFunnel = 2,

    /// <summary>Listed supply against buyer demand, by commodity category.</summary>
    Commodities = 3,
}

/// <summary>The two things an administrator may do to a listing.</summary>
public enum ProductModerationAction
{
    Suspend = 0,
    Reinstate = 1,
}

public sealed record ModerateProductCommand
{
    public ProductModerationAction Action { get; init; }

    /// <summary>
    /// Required to suspend, optional to reinstate.
    /// </summary>
    /// <remarks>
    /// Suspension is the action that needs a reason. Reinstating is the resolution, and recording
    /// why it was resolved is useful but not the point of the call.
    /// </remarks>
    public string? Reason { get; init; }
}

/// <summary>The two things an administrator may do to an account.</summary>
public enum UserStatusAction
{
    Suspend = 0,
    Activate = 1,
}

public sealed record SetUserStatusCommand
{
    public UserStatusAction Action { get; init; }

    /// <summary>Required to suspend, optional to activate. See the validator.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// A partial update to the platform settings: only the keys supplied are written.
/// </summary>
/// <remarks>
/// Partial, not a whole-object replace, because the settings screen may save one field and because
/// a full replace would silently reset any key the caller did not send back — a read-modify-write
/// that a client performing it from a stale page would get wrong. An omitted key keeps its value,
/// or its default when it never had one.
/// </remarks>
public sealed record AdminSettingsUpdateRequest
{
    public IReadOnlyList<AdminSettingChange> Settings { get; init; } = [];
}

public sealed record AdminSettingChange
{
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

// ---- Audit log ----

public sealed record AdminAuditLogDto
{
    public Guid Id { get; init; }

    /// <summary>The administrator. Null if the account has since been deleted.</summary>
    public Guid UserId { get; init; }
    public string? ActorDisplayName { get; init; }

    public string Action { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public Guid? EntityId { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string? Details { get; init; }
    public DateTime OccurredAtUtc { get; init; }
}

// ---- Dashboard ----

/// <summary>
/// One stage of a count-by-status breakdown, e.g. the verification funnel.
/// </summary>
public sealed record AdminFunnelStageDto
{
    public int Status { get; init; }
    public string Label { get; init; } = string.Empty;
    public int Count { get; init; }
}

/// <summary>
/// Trade value for one corridor in one currency.
/// </summary>
/// <remarks>
/// Currency is part of the identity of this row, not a decoration. The system has no FX rate
/// anywhere and never converts, so USD and NGN figures are not comparable and must never be added.
/// Grouping without the currency would produce a single confident-looking number that is arithmetically
/// meaningless.
/// </remarks>
public sealed record AdminCorridorStatDto
{
    /// <summary>Port of loading, e.g. "Lagos". Null when the order recorded no port.</summary>
    public string? OriginPort { get; init; }

    /// <summary>Destination country as recorded on the order.</summary>
    public string? DestinationCountry { get; init; }

    public string Currency { get; init; } = string.Empty;

    public int OrderCount { get; init; }

    /// <summary>Total value of orders placed on this corridor, paid or not.</summary>
    public decimal GrossOrderValue { get; init; }

    /// <summary>
    /// Money that actually arrived, summed from the transaction ledger rather than from order
    /// rollups, because the ledger is the record that reconciles. Refunds and fees are signed
    /// against their type and therefore reduce this figure.
    /// </summary>
    public decimal NetSettled { get; init; }

    /// <summary>Gross value still outstanding. Negative where the corridor has been over-refunded.</summary>
    public decimal Outstanding { get; init; }

    /// <summary>
    /// Ledger entries on this corridor recorded in a currency other than their order's.
    /// </summary>
    /// <remarks>
    /// Expected to always be zero. It exists so that a currency mismatch surfaces as a flagged row
    /// rather than as a total that looks arithmetically fine and reconciles against nothing.
    /// </remarks>
    public int MismatchedLedgerEntries { get; init; }
}

/// <summary>A commodity line, by the category buyers most often ask for.</summary>
public sealed record AdminCommodityStatDto
{
    public string CategoryName { get; init; } = string.Empty;
    public int ProductCount { get; init; }

    /// <summary>Published listings only, so a draft does not read as available supply.</summary>
    public int PublishedCount { get; init; }

    /// <summary>
    /// Every RFQ ever filed against this category — a measure of lasting interest.
    /// </summary>
    /// <remarks>
    /// Reported alongside <see cref="OpenRfqCount"/> rather than instead of it. Lifetime interest
    /// and current demand are different questions, and collapsing them either hides a category
    /// that is reliably wanted or overstates one whose only RFQ closed years ago.
    /// </remarks>
    public int RfqCount { get; init; }

    /// <summary>
    /// RFQs still open or being quoted. This is the figure that says whether anyone is asking right
    /// now, and the one the dashboard flags a catalogue gap against.
    /// </summary>
    public int OpenRfqCount { get; init; }
}

/// <summary>RFQ demand: what buyers are asking for, and how much of it is unanswered.</summary>
public sealed record AdminDemandStatDto
{
    public int OpenRfqs { get; init; }
    public int TotalRfqs { get; init; }
    public int UnansweredOpenRfqs { get; init; }

    /// <summary>
    /// Average budget ceiling across open RFQs that stated one.
    /// </summary>
    /// <remarks>
    /// Null when no open RFQ states a budget, or when open RFQs disagree on currency — in that case
    /// an average would be a sum of incomparable numbers wearing a decimal point.
    /// </remarks>
    public decimal? AverageBudgetCeiling { get; init; }

    /// <summary>
    /// The single currency all open RFQs agree on, <c>"Mixed"</c> when they disagree, or empty when
    /// no RFQ is open at all.
    /// </summary>
    /// <remarks>
    /// Three states rather than two, because "nothing is open" is not a form of disagreement.
    /// Reporting <c>"Mixed"</c> for an empty set would assert something about demand the data does
    /// not show.
    /// </remarks>
    public string PreferredCurrency { get; init; } = string.Empty;
}

public sealed record AdminOverviewDto
{
    public PlatformCountsDto Counts { get; init; } = new();
    public IReadOnlyList<AdminFunnelStageDto> BusinessVerificationFunnel { get; init; } = [];
    public IReadOnlyList<AdminFunnelStageDto> DocumentVerificationFunnel { get; init; } = [];
    public IReadOnlyList<AdminFunnelStageDto> OrderFunnel { get; init; } = [];
    public IReadOnlyList<AdminFunnelStageDto> PaymentFunnel { get; init; } = [];
    public IReadOnlyList<AdminCorridorStatDto> Corridors { get; init; } = [];
    public IReadOnlyList<AdminCommodityStatDto> Commodities { get; init; } = [];
    public AdminDemandStatDto Demand { get; init; } = new();
}

/// <summary>Row counts for the dashboard's top strip. Deliberately raw - no derived health scores.</summary>
public sealed record PlatformCountsDto
{
    public int Users { get; init; }
    public int Businesses { get; init; }
    public int VerifiedBusinesses { get; init; }
    public int Products { get; init; }

    /// <summary>Live and buyable: <c>Active</c> and not suspended. Excludes soft-deleted rows.</summary>
    public int PublishedProducts { get; init; }

    /// <summary>
    /// Listings an administrator has suspended.
    /// </summary>
    /// <remarks>
    /// Its own figure rather than <c>Products - PublishedProducts</c> on purpose: that subtraction
    /// would fold drafts and seller-withdrawn listings in with moderated ones, and "suspended" is
    /// the one number on this strip that means somebody needs to make a decision.
    /// </remarks>
    public int SuspendedProducts { get; init; }
    public int TradeRequests { get; init; }
    public int Quotations { get; init; }
    public int Orders { get; init; }
    public int Shipments { get; init; }
    public int Transactions { get; init; }

    /// <summary>
    /// Consignments that have recorded an exception milestone at some point.
    /// </summary>
    /// <remarks>
    /// "Has hit an exception", not "is currently in one". Deciding which shipments are *currently*
    /// exceptional means reading the latest milestone of every consignment, which is exactly the
    /// per-row derivation task 2.6 exists to avoid repeating in a summary. A recovered consignment
    /// still belongs in this count, because a corridor that keeps throwing exceptions is the finding.
    /// </remarks>
    public int ShipmentsInException { get; init; }
}

// ---- Admin lists ----

public sealed record AdminUserDto
{
    public Guid Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string? DisplayName { get; init; }

    /// <summary>
    /// Buyer account number, e.g. <c>BUY-ID-AG-000001</c>; null for accounts that never registered
    /// as a buyer. Searchable — an administrator can look an account up by this or by name.
    /// </summary>
    public string? BuyerId { get; init; }

    public bool EmailConfirmed { get; init; }
    public bool LockedOut { get; init; }

    /// <summary>
    /// The platform's own suspension flag, distinct from Identity's lockout.
    /// </summary>
    /// <remarks>
    /// Two independent states, and the screen shows both because they are set by different things.
    /// <c>LockedOut</c> is Identity's failed-sign-in throttle and clears itself; this is an
    /// administrator's decision and only an administrator clears it.
    /// </remarks>
    public bool IsActive { get; init; }

    public string[] Roles { get; init; } = [];
    public string? BusinessName { get; init; }
    public DateTime? CreatedAtUtc { get; init; }
}

public sealed record AdminOrderRowDto
{
    public Guid Id { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public int Status { get; init; }
    public int PaymentStatus { get; init; }
    public string Currency { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public decimal AmountPaid { get; init; }
    public string? BuyerDisplayName { get; init; }
    public string? SellerTradingName { get; init; }
    public string? DestinationCountry { get; init; }
    public DateTime? PlacedAtUtc { get; init; }
    public DateTime? CreatedAtUtc { get; init; }

    /// <summary>Consignments on this order. Zero for most, which is why it is a count not a list.</summary>
    public int ShipmentCount { get; init; }
}

public sealed record AdminShipmentDto
{
    public Guid Id { get; init; }
    public string Reference { get; init; } = string.Empty;
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;

    /// <summary>Derived from the latest milestone, exactly as the parties see it.</summary>
    public int Status { get; init; }
    public DateTime? StatusSinceUtc { get; init; }

    public int Mode { get; init; }
    public string CarrierName { get; init; } = string.Empty;
    public string? DestinationCountry { get; init; }
    public bool HasGuestTracking { get; init; }
    public int MilestoneCount { get; init; }
    public DateTime? EstimatedArrivalUtc { get; init; }
    public DateTime? CreatedAtUtc { get; init; }
}

public sealed record AdminProductRowDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string? CategoryName { get; init; }
    public Guid SellerBusinessId { get; init; }
    public string? SellerTradingName { get; init; }
    public string OriginCountry { get; init; } = string.Empty;

    /// <summary>ISO-4217. Carried because price is meaningless without it, and never converted.</summary>
    public string Currency { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int PriceUnit { get; init; }

    public decimal AvailableQuantity { get; init; }

    /// <summary>The seller's own visibility choice.</summary>
    public int ListingStatus { get; init; }

    /// <summary>Whether an administrator has suspended it.</summary>
    public int ModerationStatus { get; init; }

    public string? ModerationReason { get; init; }
    public DateTime? SuspendedAtUtc { get; init; }
    public Guid? SuspendedByUserId { get; init; }

    /// <summary>
    /// Whether the listing is visible in the marketplace right now.
    /// </summary>
    /// <remarks>
    /// Derived server-side from both states, so every surface agrees on what a buyer can actually
    /// see. Clients should render this rather than re-deriving it from the two fields.
    /// </remarks>
    public bool IsVisibleInMarketplace { get; init; }

    public DateTime? CreatedAtUtc { get; init; }
}

public sealed record AdminRfqRowDto
{
    public Guid Id { get; init; }
    public string RfqNumber { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public int Status { get; init; }

    /// <summary>ISO-4217, as stated by the buyer. Never converted.</summary>
    public string PreferredCurrency { get; init; } = string.Empty;
    public decimal? BudgetMax { get; init; }

    public decimal Quantity { get; init; }
    public int Unit { get; init; }
    public string? CategoryName { get; init; }
    public string? DestinationCountry { get; init; }
    public string? DestinationPortOrCity { get; init; }
    public Guid BuyerUserId { get; init; }
    public string? BuyerDisplayName { get; init; }

    /// <summary>Offers received. Zero means nobody has answered this RFQ.</summary>
    public int QuotationCount { get; init; }

    public DateTime ExpiresAtUtc { get; init; }
    public DateTime? CreatedAtUtc { get; init; }
}

// ---- Ledger ----

/// <summary>One movement of money, as the operator's ledger shows it.</summary>
public sealed record AdminTransactionRowDto
{
    public Guid Id { get; init; }

    /// <summary>The ledger's own reference, e.g. "TXN-2026-000001".</summary>
    public string Reference { get; init; } = string.Empty;

    public int Type { get; init; }

    /// <summary>Signed: positive for money in, negative for refunds out.</summary>
    public decimal Amount { get; init; }

    public string Currency { get; init; } = string.Empty;

    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;

    /// <summary>Correlation only; null for off-platform entries with no originating payment row.</summary>
    public Guid? PaymentId { get; init; }

    public DateTime OccurredAtUtc { get; init; }
    public string? Narrative { get; init; }

    /// <summary>The counterparty's own identity for the money: a gateway charge id, a bank trace id.</summary>
    public string? ExternalReference { get; init; }

    public DateTime? CreatedAtUtc { get; init; }
}

// ---- Reports ----

/// <summary>
/// One report, with exactly one payload populated according to <see cref="Kind"/>.
/// </summary>
/// <remarks>
/// An envelope rather than four endpoints or a weakly-typed sheet of rows. Four endpoints would
/// repeat the same window parsing and authorisation four times; a generic row shape would throw away
/// the per-report types the frontend already knows. The payload field for the requested kind is set;
/// the rest stay empty, so a client switches on <see cref="Kind"/> and reads the one it asked for.
/// </remarks>
public sealed record AdminReportDto
{
    public string Kind { get; init; } = string.Empty;

    /// <summary>The window the report was run over, echoed back. Null means "all time".</summary>
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }

    /// <summary>Populated for <c>corridors</c>.</summary>
    public IReadOnlyList<AdminCorridorStatDto> Corridors { get; init; } = [];

    /// <summary>Populated for <c>commodities</c>.</summary>
    public IReadOnlyList<AdminCommodityStatDto> Commodities { get; init; } = [];

    /// <summary>Populated for <c>verification-funnel</c>.</summary>
    public IReadOnlyList<AdminFunnelStageDto> Stages { get; init; } = [];

    /// <summary>Populated for <c>rfq</c>.</summary>
    public AdminRfqReportDto? Rfq { get; init; }
}

/// <summary>RFQ volume: the demand summary beside a count of RFQs by status.</summary>
public sealed record AdminRfqReportDto
{
    public AdminDemandStatDto Demand { get; init; } = new();

    /// <summary>
    /// RFQs by status, empty statuses included, over the same window as the summary.
    /// </summary>
    public IReadOnlyList<AdminFunnelStageDto> ByStatus { get; init; } = [];
}

// ---- Settings ----

/// <summary>One setting, its current value, and enough context for an operator to change it safely.</summary>
public sealed record AdminSettingDto
{
    public string Key { get; init; } = string.Empty;

    /// <summary>The stored value, or the catalogue default when no row exists.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>What the value would be if the row were removed. Shown so a change can be undone.</summary>
    public string DefaultValue { get; init; } = string.Empty;

    /// <summary>The value's type, for the client to pick an editor: "boolean" or "role".</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Whether a row exists at all, as opposed to the value being the default.</summary>
    public bool IsOverridden { get; init; }

    public string Label { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public Guid? UpdatedByUserId { get; init; }
    public DateTime? UpdatedAtUtc { get; init; }
}