using AgroTrade.Application.Admin;
using AgroTrade.Application.Common;
using AgroTrade.Domain.Admin;
using AgroTrade.Domain.Enums;
using AgroTrade.Domain.Trade;
using Microsoft.EntityFrameworkCore;
using AgroTrade.Infrastructure.Data;

namespace AgroTrade.Infrastructure.Admin;

/// <summary>
/// Writes an audit entry into the change tracker rather than saving it itself.
/// </summary>
/// <remarks>
/// Deliberately synchronous and save-less. The entry is flushed by the same
/// <c>SaveChangesAsync</c> as the action it records, so an admin action and its audit row commit
/// or fail together. A writer that saved on its own could record an approval that then failed to
/// persist, which is the one failure mode an audit trail cannot have.
/// </remarks>
public sealed class AuditLogWriter(AgroTradeDbContext db) : IAuditLogWriter
{
    public void Record(Guid userId, string action, string entityType, Guid? entityId, string summary, string? details = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Summary = summary,
            Details = string.IsNullOrWhiteSpace(details) ? null : details.Trim(),
        });
    }
}

/// <summary>
/// Admin dashboard reads and the audit trail (task 2.7).
/// </summary>
/// <remarks>
/// Every figure here is a deliberate server-side group-by. The dashboard is the one place in the
/// system that reads across every table at once, so it is also the place where a client-side
/// <c>ToList</c>-then-tally would be most tempting and most dangerous - it would keep working
/// right up until the order table grew past what a laptop should be asked to hold in memory.
/// </remarks>
public sealed class AdminService(AgroTradeDbContext db, IAuditLogWriter audit) : IAdminService
{
    private const int CommodityRowLimit = 12;

    // ---- Dashboard ----

    public async Task<AdminOverviewDto> GetOverviewAsync(CancellationToken ct = default)
    {
        // Sequential, and deliberately so. The obvious instinct is to issue these eight aggregates
        // concurrently, but they all share one scoped DbContext and EF Core forbids starting a second
        // operation before the first has finished - the result is a runtime failure, not a faster
        // query. Each aggregate is a single small round trip against a local SQL Server, so the
        // round trips are not the cost worth optimising; correctness is.
        return new AdminOverviewDto
        {
            Counts = await CountsAsync(ct),
            BusinessVerificationFunnel = await BusinessFunnelAsync(ct),
            DocumentVerificationFunnel = await DocumentFunnelAsync(ct),
            OrderFunnel = await OrderFunnelAsync(ct),
            PaymentFunnel = await PaymentFunnelAsync(ct),
            Corridors = await CorridorsAsync(ct),
            Commodities = await CommoditiesAsync(ct),
            Demand = await DemandAsync(ct),
        };
    }

    private async Task<PlatformCountsDto> CountsAsync(CancellationToken ct) => new()
    {
        Users = await db.Users.CountAsync(ct),
        Businesses = await db.Businesses.CountAsync(ct),
        VerifiedBusinesses = await db.Businesses.CountAsync(b => b.VerificationStatus == VerificationStatus.Verified, ct),
        Products = await db.Products.CountAsync(p => !p.IsDeleted, ct),
        PublishedProducts = await db.Products.CountAsync(
            p => !p.IsDeleted && p.ListingStatus == ListingStatus.Active && p.ModerationStatus == ModerationStatus.Cleared, ct),
        SuspendedProducts = await db.Products.CountAsync(
            p => !p.IsDeleted && p.ModerationStatus == ModerationStatus.Suspended, ct),
        TradeRequests = await db.TradeRequests.CountAsync(ct),
        Quotations = await db.Quotations.CountAsync(ct),
        Orders = await db.Orders.CountAsync(ct),
        Shipments = await db.Shipments.CountAsync(ct),
        Transactions = await db.Transactions.CountAsync(ct),
        ShipmentsInException = await db.ShipmentTrackingEvents
            .Where(e => e.Status == ShipmentStatus.Exception)
            .Select(e => e.ShipmentId)
            .Distinct()
            .CountAsync(ct),
    };

    private async Task<IReadOnlyList<AdminFunnelStageDto>> BusinessFunnelAsync(CancellationToken ct) =>
        await FunnelAsync(
            db.Businesses.AsNoTracking()
                .GroupBy(b => (int)b.VerificationStatus)
                .Select(g => new { Status = g.Key, Count = g.Count() }),
            VerificationLabels,
            row => row.Status,
            row => row.Count,
            ct);

    private async Task<IReadOnlyList<AdminFunnelStageDto>> DocumentFunnelAsync(CancellationToken ct) =>
        await FunnelAsync(
            db.TradeDocuments.AsNoTracking()
                .GroupBy(d => (int)d.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() }),
            DocumentLabels,
            row => row.Status,
            row => row.Count,
            ct);

    private async Task<IReadOnlyList<AdminFunnelStageDto>> OrderFunnelAsync(CancellationToken ct) =>
        await FunnelAsync(
            db.Orders.AsNoTracking()
                .GroupBy(o => (int)o.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() }),
            OrderLabels,
            row => row.Status,
            row => row.Count,
            ct);

    /// <summary>
    /// The payment funnel counts <em>orders</em> by payment status, not payments by payment status.
    /// An order is the thing that gets paid; counting payment attempts would make one order with
    /// three retries look like three separate obligations, and would hide the "unpaid" stage that
    /// is the number an admin actually acts on.
    /// </summary>
    private async Task<IReadOnlyList<AdminFunnelStageDto>> PaymentFunnelAsync(CancellationToken ct) =>
        await FunnelAsync(
            db.Orders.AsNoTracking()
                .GroupBy(o => (int)o.PaymentStatus)
                .Select(g => new { Status = g.Key, Count = g.Count() }),
            PaymentLabels,
            row => row.Status,
            row => row.Count,
            ct);

    /// <summary>
    /// Projects a status-count query into a full funnel, empty stages included.
    /// </summary>
    /// <remarks>
    /// A funnel that omits its zero stages cannot show a drop. "3 verified, 1 pending" is only
    /// legible next to "0 rejected", because the size of the gap between stages is the entire
    /// reason to draw the shape.
    /// <para>
    /// Status and count are read back through selectors rather than off a named row type, because
    /// each funnel projects its own anonymous shape and a common row type here would be four times
    /// the same two integers re-declared.
    /// </para>
    /// </remarks>
    private static async Task<IReadOnlyList<AdminFunnelStageDto>> FunnelAsync<T>(
        IQueryable<T> source,
        IReadOnlyDictionary<int, string> labels,
        Func<T, int> status,
        Func<T, int> count,
        CancellationToken ct)
    {
        var counts = new Dictionary<int, int>();
        foreach (var row in await source.ToListAsync(ct))
        {
            counts[status(row)] = count(row);
        }

        return labels.Keys
            .Select(status => new AdminFunnelStageDto
            {
                Status = status,
                Label = labels[status],
                Count = counts.GetValueOrDefault(status),
            })
            .ToList();
    }

    /// <summary>
    /// Trade value per corridor, per currency.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Gross and net are aggregated separately and merged on the full key, currency included. They
    /// answer different questions - what was ordered, against what actually arrived - and collapsing
    /// them into one number would hide precisely the unpaid orders an admin most needs to see.
    /// </para>
    /// <para>
    /// Net comes from the transaction ledger rather than from a payment rollup, because the ledger
    /// is the record that reconciles against a bank statement and because it carries refunds and
    /// fees as signed entries. The order's payment status is a cached conclusion; this is the money.
    /// </para>
    /// <para>
    /// Grouping by currency is not a nicety. There is no FX rate anywhere in this system and none
    /// is invented here, so NGN and USD figures are not comparable and must never be added.
    /// </para>
    /// <para>
    /// Deliberately uncapped, unlike the commodity panel. The row count is bounded by the number of
    /// distinct port/country/currency combinations a business actually trades, which is small, and
    /// a cap here would be actively harmful: the corridor an admin opens the page to inspect is
    /// frequently the low-volume one, and silently omitting it is how a route with three shipments a
    /// year reads as a route with none.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<AdminCorridorStatDto>> CorridorsAsync(CancellationToken ct)
    {
        var gross = await db.Orders.AsNoTracking()
            .GroupBy(o => new { o.PortOfLoading, o.DestinationCountry, o.Currency })
            .Select(g => new
            {
                g.Key.PortOfLoading,
                g.Key.DestinationCountry,
                g.Key.Currency,
                OrderCount = g.Count(),
                Gross = g.Sum(o => o.Total),
            })
            .ToListAsync(ct);

        var net = await db.Transactions.AsNoTracking()
            .Join(db.Orders.AsNoTracking(), t => t.OrderId, o => o.Id, (t, o) => new
            {
                t.Amount,
                TransactionCurrency = t.Currency,
                OrderCurrency = o.Currency,
                o.PortOfLoading,
                o.DestinationCountry,
            })
            .GroupBy(x => new { x.PortOfLoading, x.DestinationCountry, x.TransactionCurrency })
            .Select(g => new
            {
                g.Key.PortOfLoading,
                g.Key.DestinationCountry,
                Currency = g.Key.TransactionCurrency,
                Net = g.Sum(x => x.Amount),
                // Surfaced so a ledger entry denominated differently from its order shows up as a
                // discrepancy instead of being quietly folded into a corridor it does not belong to.
                Mismatched = g.Count(x => x.TransactionCurrency != x.OrderCurrency),
            })
            .ToListAsync(ct);

        var netByKey = net.ToDictionary(
            x => CorridorKey(x.PortOfLoading, x.DestinationCountry, x.Currency),
            x => x.Net);

        // A ledger entry denominated differently from its order cannot be reconciled against that
        // order's gross figure, so it is surfaced as a count on the corridor rather than being
        // quietly averaged away into a total that looks plausible and is not.
        var mismatchedByKey = net
            .Where(x => x.Mismatched > 0)
            .ToDictionary(x => CorridorKey(x.PortOfLoading, x.DestinationCountry, x.Currency), x => x.Mismatched);

        return gross
            .Select(g =>
            {
                var settled = netByKey.GetValueOrDefault(CorridorKey(g.PortOfLoading, g.DestinationCountry, g.Currency));
                return new AdminCorridorStatDto
                {
                    OriginPort = g.PortOfLoading,
                    DestinationCountry = g.DestinationCountry,
                    Currency = g.Currency,
                    OrderCount = g.OrderCount,
                    GrossOrderValue = g.Gross,
                    NetSettled = settled,
                    Outstanding = g.Gross - settled,
                    MismatchedLedgerEntries = mismatchedByKey
                        .GetValueOrDefault(CorridorKey(g.PortOfLoading, g.DestinationCountry, g.Currency)),
                };
            })
            .OrderByDescending(c => c.GrossOrderValue)
            .ThenByDescending(c => c.OrderCount)
            .ToList();
    }

    private static string CorridorKey(string? origin, string? destination, string currency) =>
        $"{origin ?? "-"}|{destination ?? "-"}|{currency}";

    /// <summary>
    /// Supply and demand per category.
    /// </summary>
    /// <remarks>
    /// Products and RFQs are counted separately and merged on the category, because "we list cocoa
    /// and nobody is asking for it" and "everyone wants cocoa and we list none of it" are the two
    /// commercially interesting facts, and a single count per category would show neither.
    /// </remarks>
    private async Task<IReadOnlyList<AdminCommodityStatDto>> CommoditiesAsync(CancellationToken ct)
    {
        var products = await db.Products.AsNoTracking()
            .Where(p => !p.IsDeleted)
            .Join(db.Categories.AsNoTracking(), p => p.CategoryId, c => c.Id,
                (p, c) => new { c.Name, p.ListingStatus, p.ModerationStatus })
            .GroupBy(x => x.Name)
            .Select(g => new
            {
                CategoryName = g.Key,
                ProductCount = g.Count(),

                // Published means buyable, so a suspended listing is excluded here exactly as it is
                // from the marketplace. Counting it as supply would tell an operator we can serve a
                // category from stock that no buyer can actually order.
                PublishedCount = g.Count(x => x.ListingStatus == ListingStatus.Active
                    && x.ModerationStatus == ModerationStatus.Cleared),
            })
            .ToListAsync(ct);

        // Two RFQ counts per category, because "is anyone asking for this?" and "has anyone ever
        // wanted this?" are different questions. A category whose only RFQ closed a year ago is
        // history, not a live gap, and flagging it as one would put a false signal on the exact
        // panel whose whole purpose is to be worth reading.
        var rfqs = await db.TradeRequests.AsNoTracking()
            .Where(r => r.CategoryId != null)
            .Join(db.Categories.AsNoTracking(), r => r.CategoryId!.Value, c => c.Id, (r, c) =>
                new { c.Name, r.Status })
            .GroupBy(x => x.Name)
            .Select(g => new
            {
                CategoryName = g.Key,
                RfqCount = g.Count(),
                OpenRfqCount = g.Count(x => x.Status == RfqStatus.Open || x.Status == RfqStatus.Quoting),
            })
            .ToListAsync(ct);

        var rfqByCategory = rfqs.ToDictionary(x => x.CategoryName);
        var productByCategory = products.ToDictionary(x => x.CategoryName);

        // Driven by the union of both sides, not by the product list alone. A category buyers are
        // asking for that we list nothing in is precisely the catalogue gap this panel exists to
        // surface — iterating the products only would omit it, and the omission would be invisible,
        // which is the worst way for a missing row to show up.
        var names = productByCategory.Keys
            .Concat(rfqByCategory.Keys)
            .Distinct();

        return names
            .Select(name => new AdminCommodityStatDto
            {
                CategoryName = name,
                ProductCount = productByCategory.GetValueOrDefault(name)?.ProductCount ?? 0,
                PublishedCount = productByCategory.GetValueOrDefault(name)?.PublishedCount ?? 0,
                RfqCount = rfqByCategory.GetValueOrDefault(name)?.RfqCount ?? 0,
                OpenRfqCount = rfqByCategory.GetValueOrDefault(name)?.OpenRfqCount ?? 0,
            })
            // Ordered by live demand first, so a category buyers are asking for right now is never
            // pushed below one with a longer history and no current interest.
            .OrderByDescending(c => c.OpenRfqCount)
            .ThenByDescending(c => c.PublishedCount)
            .ThenByDescending(c => c.RfqCount)
            .ThenBy(c => c.CategoryName, StringComparer.Ordinal)
            .Take(CommodityRowLimit)
            .ToList();
    }

    private async Task<AdminDemandStatDto> DemandAsync(CancellationToken ct)
    {
        var open = await db.TradeRequests.AsNoTracking()
            .Where(r => r.Status == RfqStatus.Open || r.Status == RfqStatus.Quoting)
            .Select(r => new { r.Id, r.BudgetMax, r.PreferredCurrency })
            .ToListAsync(ct);

        var total = await db.TradeRequests.CountAsync(ct);

        // "Unanswered" is the actionable figure: an open RFQ with no quotation at all is demand the
        // seller is not meeting. Counted as RFQs, not quotations, so one eager supplier quoting
        // everything cannot read as full coverage.
        var answered = (await db.Quotations.AsNoTracking()
                .Select(q => q.TradeRequestId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        var unanswered = open.Count(r => !answered.Contains(r.Id));

        // Only meaningful while every open RFQ agrees on a currency. Mixed currencies would make
        // the average a sum of incomparable numbers, so it is withheld rather than approximated.
        //
        // Three distinct answers, not two. With nothing open there is no currency to report and no
        // disagreement to report either — calling that "Mixed" would imply open RFQs in several
        // currencies when there are none at all, which is a claim about demand the data does not
        // support. So: empty when nothing is open, "Mixed" only when open RFQs genuinely disagree.
        var currencies = open.Select(r => r.PreferredCurrency).Distinct().ToList();
        var singleCurrency = currencies.Count == 1 ? currencies[0] : null;
        var withBudget = singleCurrency is null
            ? []
            : open.Where(r => r.BudgetMax.HasValue).Select(r => r.BudgetMax!.Value).ToList();

        return new AdminDemandStatDto
        {
            OpenRfqs = open.Count,
            TotalRfqs = total,
            UnansweredOpenRfqs = unanswered,
            AverageBudgetCeiling = withBudget.Count > 0 ? withBudget.Average() : null,
            PreferredCurrency = open.Count == 0 ? string.Empty : singleCurrency ?? "Mixed",
        };
    }

    // ---- Audit log ----

    public async Task<PagedResult<AdminAuditLogDto>> SearchAuditLogAsync(
        AdminAuditLogQuery query, CancellationToken ct = default)
    {
        var (page, pageSize) = PageHelper.Normalize(query.Page, query.PageSize);
        var search = query.Q?.Trim();

        var filtered = db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            filtered = filtered.Where(a => a.Action == query.Action);
        }

        if (query.UserId.HasValue)
        {
            filtered = filtered.Where(a => a.UserId == query.UserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            Guid? searchId = null;
            if (Guid.TryParse(search, out var pid))
            {
                searchId = pid;
            }

            filtered = filtered.Where(a =>
                EF.Functions.Like(a.Summary, $"%{search}%") ||
                EF.Functions.Like(a.EntityType, $"%{search}%") ||
                EF.Functions.Like(a.Action, $"%{search}%") ||
                (a.Details != null && EF.Functions.Like(a.Details, $"%{search}%")) ||
                (searchId.HasValue && a.EntityId == searchId.Value));
        }

        var total = await filtered.CountAsync(ct);
        var rows = await filtered
            .OrderByDescending(a => a.CreatedAtUtc)
            .ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // An actor whose account has since been deleted must not blank the row out. "Who did this"
        // is the entire point of the log, so the id stands in and the name reads as unknown rather
        // than as absent - a blank would be indistinguishable from "nobody did this".
        var actorNames = rows.Count == 0
            ? []
            : await db.Users.AsNoTracking()
                .Where(u => rows.Select(r => r.UserId).Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        return new PagedResult<AdminAuditLogDto>
        {
            Items = rows.Select(a => new AdminAuditLogDto
            {
                Id = a.Id,
                UserId = a.UserId,
                ActorDisplayName = actorNames.GetValueOrDefault(a.UserId),
                Action = a.Action,
                EntityType = a.EntityType,
                EntityId = a.EntityId,
                Summary = a.Summary,
                Details = a.Details,
                OccurredAtUtc = a.CreatedAtUtc ?? DateTime.UnixEpoch,
            }).ToList(),
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = TotalPages(total, pageSize),
        };
    }

    // ---- Lists ----

    public async Task<PagedResult<AdminUserDto>> SearchUsersAsync(
        AdminUserSearchQuery query, CancellationToken ct = default)
    {
        var (page, pageSize) = PageHelper.Normalize(query.Page, query.PageSize);
        var search = query.Q?.Trim();

        var filtered = db.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(u =>
                EF.Functions.Like(u.Email, $"%{search}%") ||
                EF.Functions.Like(u.DisplayName, $"%{search}%") ||
                db.Businesses.Any(b => b.OwnerUserId == u.Id && EF.Functions.Like(b.TradingName, $"%{search}%")));
        }

        if (query.EmailConfirmed.HasValue)
        {
            filtered = filtered.Where(u => u.EmailConfirmed == query.EmailConfirmed.Value);
        }

        if (query.LockedOut.HasValue)
        {
            filtered = query.LockedOut.Value
                ? filtered.Where(u => u.LockoutEnd != null && u.LockoutEnd > DateTime.UtcNow)
                : filtered.Where(u => u.LockoutEnd == null || u.LockoutEnd <= DateTime.UtcNow);
        }

        var role = query.Role?.Trim();
        if (!string.IsNullOrWhiteSpace(role))
        {
            filtered = filtered.Where(u => db.UserRoles
                .Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == role)));
        }

        var total = await filtered.CountAsync(ct);
        var rows = await filtered
            // Newest registration first, with rows of *unknown* age explicitly last.
            //
            // A plain OrderByDescending would put NULLs first on SQL Server, so every user whose
            // CreatedAtUtc was never stamped would sit at the top of a queue that claims to be sorted
            // by recency — the admin screen would be showing the least-known accounts as the newest.
            // Sorting the flag first costs one comparison and makes "newest" mean newest.
            .OrderByDescending(u => u.CreatedAtUtc != null)
            .ThenByDescending(u => u.CreatedAtUtc)
            .ThenByDescending(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new
            {
                u.Id,
                Email = u.Email ?? string.Empty,
                u.DisplayName,
                u.EmailConfirmed,
                u.LockoutEnd,
                u.CreatedAtUtc,
                BusinessName = db.Businesses
                    .Where(b => b.OwnerUserId == u.Id)
                    .Select(b => b.TradingName)
                    .FirstOrDefault(),
                Roles = db.UserRoles
                    .Where(ur => ur.UserId == u.Id)
                    .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                    // Role names are non-null in Identity's model; EF's projection loses that and
                    // widens to string?, which then fails to assign to the DTO's string[].
                    .Select(name => name!)
                    .ToArray(),
            })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;

        return new PagedResult<AdminUserDto>
        {
            Items = rows.Select(u => new AdminUserDto
            {
                Id = u.Id,
                Email = u.Email,
                DisplayName = u.DisplayName,
                EmailConfirmed = u.EmailConfirmed,
                LockedOut = u.LockoutEnd != null && u.LockoutEnd > now,
                Roles = u.Roles,
                BusinessName = u.BusinessName,
                CreatedAtUtc = u.CreatedAtUtc,
            }).ToList(),
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = TotalPages(total, pageSize),
        };
    }

    /// <summary>
    /// Every consignment across every order.
    /// </summary>
    /// <remarks>
    /// The status filter is applied in memory, and that is not an oversight. Status is derived from
    /// the latest milestone (task 2.6), so it is a method call on the loaded entity rather than a
    /// column - there is nothing in SQL to filter on. The alternative, denormalising a status
    /// column purely so a list could filter on it, would reintroduce exactly the drift 2.6 removed.
    /// The mitigation is that the timeline is loaded with the row and the page is bounded, so the
    /// working set is the shipment count, not the table count.
    /// </remarks>
    public async Task<PagedResult<AdminShipmentDto>> SearchShipmentsAsync(
        AdminShipmentSearchQuery query, CancellationToken ct = default)
    {
        var (page, pageSize) = PageHelper.Normalize(query.Page, query.PageSize);
        var search = query.Q?.Trim();

        // The timeline travels with the row. Without it LatestEvent() returns null, every shipment
        // reports the same default status, and the status filter matches nothing.
        var filtered = db.Shipments.AsNoTracking()
            .Include(s => s.Events)
            .Include(s => s.Order)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(s =>
                EF.Functions.Like(s.Reference, $"%{search}%") ||
                EF.Functions.Like(s.CarrierName, $"%{search}%") ||
                (s.TrackingReference != null && EF.Functions.Like(s.TrackingReference, $"%{search}%")) ||
                EF.Functions.Like(s.Order!.OrderNumber, $"%{search}%"));
        }

        var candidates = await filtered
            .OrderByDescending(s => s.CreatedAtUtc)
            .ThenByDescending(s => s.Id)
            .ToListAsync(ct);

        var matched = query.Status.HasValue
            ? candidates.Where(s => s.LatestEvent()?.Status == (ShipmentStatus)query.Status.Value).ToList()
            : candidates;

        if (query.HasGuestTracking.HasValue)
        {
            matched = query.HasGuestTracking.Value
                ? matched.Where(s => s.PublicTrackingTokenHash is not null).ToList()
                : matched.Where(s => s.PublicTrackingTokenHash is null).ToList();
        }

        var total = matched.Count;

        return new PagedResult<AdminShipmentDto>
        {
            Items = matched
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s =>
                {
                    var latest = s.LatestEvent();
                    return new AdminShipmentDto
                    {
                        Id = s.Id,
                        Reference = s.Reference,
                        OrderId = s.OrderId,
                        OrderNumber = s.Order?.OrderNumber ?? string.Empty,
                        Status = latest is null ? (int)ShipmentStatus.Pending : (int)latest.Status,
                        StatusSinceUtc = latest?.OccurredAtUtc,
                        Mode = (int)s.Mode,
                        CarrierName = s.CarrierName,
                        DestinationCountry = s.DestinationCountry ?? s.Order?.DestinationCountry,
                        HasGuestTracking = s.PublicTrackingTokenHash != null,
                        MilestoneCount = s.Events.Count,
                        EstimatedArrivalUtc = s.EstimatedArrivalUtc,
                        CreatedAtUtc = s.CreatedAtUtc,
                    };
                })
                .ToList(),
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = TotalPages(total, pageSize),
        };
    }

    public async Task<PagedResult<AdminOrderRowDto>> SearchOrdersAsync(
        AdminOrderSearchQuery query, CancellationToken ct = default)
    {
        var (page, pageSize) = PageHelper.Normalize(query.Page, query.PageSize);
        var search = query.Q?.Trim();

        var filtered = db.Orders.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(o =>
                EF.Functions.Like(o.OrderNumber, $"%{search}%") ||
                (o.DestinationCountry != null && EF.Functions.Like(o.DestinationCountry, $"%{search}%")));
        }

        if (query.Status.HasValue)
        {
            filtered = filtered.Where(o => o.Status == (OrderStatus)query.Status.Value);
        }

        if (query.PaymentStatus.HasValue)
        {
            filtered = filtered.Where(o => o.PaymentStatus == (PaymentStatus)query.PaymentStatus.Value);
        }

        var total = await filtered.CountAsync(ct);

        // Paid is summed from the payment rows rather than read off the order, for the same reason
        // the order detail page does it that way: there is no column to drift.
        var rows = await filtered
            .OrderByDescending(o => o.CreatedAtUtc)
            .ThenByDescending(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                Status = (int)o.Status,
                PaymentStatus = (int)o.PaymentStatus,
                o.Currency,
                o.Total,
                o.DestinationCountry,
                o.PlacedAtUtc,
                o.CreatedAtUtc,
                BuyerDisplayName = db.Users
                    .Where(u => u.Id == o.BuyerUserId)
                    .Select(u => u.DisplayName)
                    .FirstOrDefault(),
                SellerTradingName = o.SellerBusiness != null ? o.SellerBusiness.TradingName : null,
                ShipmentCount = db.Shipments.Count(s => s.OrderId == o.Id),
                AmountPaid = o.Payments
                    .Where(p => p.Status == PaymentStatus.Paid)
                    .Sum(p => p.Amount),
            })
            .ToListAsync(ct);

        return new PagedResult<AdminOrderRowDto>
        {
            Items = rows.Select(o => new AdminOrderRowDto
            {
                Id = o.Id,
                OrderNumber = o.OrderNumber,
                Status = o.Status,
                PaymentStatus = o.PaymentStatus,
                Currency = o.Currency,
                Total = o.Total,
                AmountPaid = o.AmountPaid,
                BuyerDisplayName = o.BuyerDisplayName,
                SellerTradingName = o.SellerTradingName,
                DestinationCountry = o.DestinationCountry,
                PlacedAtUtc = o.PlacedAtUtc,
                CreatedAtUtc = o.CreatedAtUtc,
                ShipmentCount = o.ShipmentCount,
            }).ToList(),
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = TotalPages(total, pageSize),
        };
    }

    private static int TotalPages(int total, int pageSize) =>
        (int)Math.Ceiling(total / (double)pageSize);

    public async Task<PagedResult<AdminProductRowDto>> SearchProductsAsync(
        AdminProductSearchQuery query, CancellationToken ct = default)
    {
        var (page, pageSize) = PageHelper.Normalize(query.Page, query.PageSize);
        var search = query.Q?.Trim();

        // Soft-deleted listings are excluded everywhere in this system, so they are excluded here
        // too. A queue that surfaces a row the marketplace will never show is a queue with ghosts in it.
        var filtered = db.Products.AsNoTracking().Where(p => !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(p =>
                EF.Functions.Like(p.Name, $"%{search}%") ||
                EF.Functions.Like(p.Slug, $"%{search}%") ||
                (p.HsCode != null && EF.Functions.Like(p.HsCode, $"%{search}%")));
            if (Guid.TryParse(search, out var pid))
            {
                filtered = filtered.Union(db.Products.AsNoTracking().Where(p => p.Id == pid && !p.IsDeleted));
            }
        }

        if (query.ListingStatus.HasValue)
        {
            filtered = filtered.Where(p => p.ListingStatus == (ListingStatus)query.ListingStatus.Value);
        }

        if (query.ModerationStatus.HasValue)
        {
            filtered = filtered.Where(p => p.ModerationStatus == (ModerationStatus)query.ModerationStatus.Value);
        }

        if (query.CategoryId.HasValue)
        {
            filtered = filtered.Where(p => p.CategoryId == query.CategoryId.Value);
        }

        var total = await filtered.CountAsync(ct);

        var rows = await filtered
            // Newest first, with nulls last: SQL Server sorts NULLs first on a descending sort, so
            // without this the queue leads with the listings whose age nobody knows.
            .OrderByDescending(p => p.CreatedAtUtc != null)
            .ThenByDescending(p => p.CreatedAtUtc)
            .ThenByDescending(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new AdminProductRowDto
            {
                Id = p.Id,
                Name = p.Name,
                Slug = p.Slug,
                SellerBusinessId = p.SellerBusinessId,
                OriginCountry = p.OriginCountry,
                Currency = p.Currency,
                Price = p.Price,
                PriceUnit = (int)p.PriceUnit,
                AvailableQuantity = p.AvailableQuantity,
                ListingStatus = (int)p.ListingStatus,
                ModerationStatus = (int)p.ModerationStatus,
                ModerationReason = p.ModerationReason,
                SuspendedAtUtc = p.SuspendedAtUtc,
                SuspendedByUserId = p.SuspendedByUserId,
                // Both conditions, computed once here so no client has to re-derive them and
                // disagree with the marketplace about what is buyable.
                IsVisibleInMarketplace =
                    p.ListingStatus == ListingStatus.Active && p.ModerationStatus == ModerationStatus.Cleared,
                CreatedAtUtc = p.CreatedAtUtc,
                CategoryName = db.Categories
                    .Where(c => c.Id == p.CategoryId)
                    .Select(c => c.Name)
                    .FirstOrDefault(),
                SellerTradingName = db.Businesses
                    .Where(b => b.Id == p.SellerBusinessId)
                    .Select(b => b.TradingName)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        return new PagedResult<AdminProductRowDto>
        {
            Items = rows,
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = TotalPages(total, pageSize),
        };
    }

    public async Task<AdminProductRowDto> ModerateProductAsync(
        Guid productId, Guid adminUserId, ModerateProductCommand command, CancellationToken ct = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId && !p.IsDeleted, ct)
            ?? throw ApiException.NotFound("PRODUCT_NOT_FOUND", "Listing not found.");

        var summary = string.Empty;

        switch (command.Action)
        {
            case ProductModerationAction.Suspend:
                if (product.ModerationStatus == ModerationStatus.Suspended)
                {
                    // Re-suspending an already-suspended listing would overwrite the original
                    // reason and the original administrator, which is how a moderation trail stops
                    // being a record of anything.
                    throw ApiException.Conflict(
                        "LISTING_ALREADY_SUSPENDED",
                        "This listing is already suspended. Reinstate it before suspending it again.");
                }

                if (string.IsNullOrWhiteSpace(command.Reason))
                {
                    // Re-checked here even though the controller validates it first. A service that
                    // trusts its caller to have checked is only safe while there is exactly one
                    // caller, and the alternative to this line is a suspension with no stated reason
                    // in the audit log - the one record nobody could later act on.
                    throw ApiException.BadRequest(
                        "REASON_REQUIRED", "A suspension must state a reason.");
                }

                product.ModerationStatus = ModerationStatus.Suspended;
                product.ModerationReason = command.Reason.Trim();
                product.SuspendedAtUtc = DateTime.UtcNow;
                product.SuspendedByUserId = adminUserId;
                summary = $"Suspended listing \"{product.Name}\"";
                audit.Record(
                    adminUserId,
                    "product.moderation.suspended",
                    "Product",
                    product.Id,
                    summary,
                    command.Reason.Trim());
                break;

            case ProductModerationAction.Reinstate:
                if (product.ModerationStatus != ModerationStatus.Suspended)
                {
                    throw ApiException.Conflict(
                        "LISTING_NOT_SUSPENDED",
                        "This listing is not suspended, so there is nothing to reinstate.");
                }

                var previousReason = product.ModerationReason;
                product.ModerationStatus = ModerationStatus.Cleared;
                product.ModerationReason = null;
                product.SuspendedAtUtc = null;
                product.SuspendedByUserId = null;
                summary = $"Reinstated listing \"{product.Name}\"";

                // Deliberately does not set ListingStatus = Active. Reinstating ends the
                // suspension; whether the seller has the listing live is the seller's own decision,
                // and an admin quietly publishing a draft would be writing the seller's content
                // decision for them. A listing that was Active before the suspension returns on its own.
                audit.Record(
                    adminUserId,
                    "product.moderation.reinstated",
                    "Product",
                    product.Id,
                    summary,
                    command.Reason?.Trim() ?? $"Cleared suspension: {previousReason}");
                break;

            default:
                throw ApiException.BadRequest(
                    "UNKNOWN_MODERATION_ACTION", "Unknown moderation action. Expected Suspend or Reinstate.");
        }

        await db.SaveChangesAsync(ct);

        return new AdminProductRowDto
        {
            Id = product.Id,
            Name = product.Name,
            Slug = product.Slug,
            SellerBusinessId = product.SellerBusinessId,
            OriginCountry = product.OriginCountry,
            Currency = product.Currency,
            Price = product.Price,
            PriceUnit = (int)product.PriceUnit,
            AvailableQuantity = product.AvailableQuantity,
            ListingStatus = (int)product.ListingStatus,
            ModerationStatus = (int)product.ModerationStatus,
            ModerationReason = product.ModerationReason,
            SuspendedAtUtc = product.SuspendedAtUtc,
            SuspendedByUserId = product.SuspendedByUserId,
            IsVisibleInMarketplace =
                product.ListingStatus == ListingStatus.Active && product.ModerationStatus == ModerationStatus.Cleared,
            CreatedAtUtc = product.CreatedAtUtc,
            CategoryName = await db.Categories.Where(c => c.Id == product.CategoryId).Select(c => c.Name).FirstOrDefaultAsync(ct),
            SellerTradingName = await db.Businesses.Where(b => b.Id == product.SellerBusinessId).Select(b => b.TradingName).FirstOrDefaultAsync(ct),
        };
    }

    public async Task<PagedResult<AdminRfqRowDto>> SearchRfqsAsync(
        AdminRfqSearchQuery query, CancellationToken ct = default)
    {
        var (page, pageSize) = PageHelper.Normalize(query.Page, query.PageSize);
        var search = query.Q?.Trim();

        var filtered = db.TradeRequests.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(r =>
                EF.Functions.Like(r.Title, $"%{search}%") ||
                EF.Functions.Like(r.RfqNumber, $"%{search}%") ||
                (r.DestinationPortOrCity != null && EF.Functions.Like(r.DestinationPortOrCity, $"%{search}%")));
        }

        if (query.Status.HasValue)
        {
            filtered = filtered.Where(r => r.Status == (RfqStatus)query.Status.Value);
        }

        // "Unanswered" is about the RFQ, not the number of offers. Expressed as an EXISTS-free
        // count comparison so it can be answered by SQL and paged, rather than by loading every
        // quotation and counting in memory.
        if (query.Unanswered == true)
        {
            filtered = filtered.Where(r => !db.Quotations.Any(q => q.TradeRequestId == r.Id));
        }
        else if (query.Unanswered == false)
        {
            filtered = filtered.Where(r => db.Quotations.Any(q => q.TradeRequestId == r.Id));
        }

        var total = await filtered.CountAsync(ct);

        var rows = await filtered
            .OrderByDescending(r => r.CreatedAtUtc != null)
            .ThenByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new AdminRfqRowDto
            {
                Id = r.Id,
                RfqNumber = r.RfqNumber,
                Title = r.Title,
                Status = (int)r.Status,
                PreferredCurrency = r.PreferredCurrency,
                BudgetMax = r.BudgetMax,
                Quantity = r.Quantity,
                Unit = (int)r.Unit,
                DestinationCountry = r.DestinationCountry,
                DestinationPortOrCity = r.DestinationPortOrCity,
                BuyerUserId = r.BuyerUserId,
                ExpiresAtUtc = r.ExpiresAt,
                CreatedAtUtc = r.CreatedAtUtc,
                QuotationCount = db.Quotations.Count(q => q.TradeRequestId == r.Id),
                CategoryName = r.CategoryId == null
                    ? null
                    : db.Categories.Where(c => c.Id == r.CategoryId.Value).Select(c => c.Name).FirstOrDefault(),
                BuyerDisplayName = db.Users
                    .Where(u => u.Id == r.BuyerUserId)
                    .Select(u => u.DisplayName)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        return new PagedResult<AdminRfqRowDto>
        {
            Items = rows,
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = TotalPages(total, pageSize),
        };
    }

    // ---- Labels ----

    // Held beside the queries rather than in the DTOs: a stage's display name is a presentation
    // concern, and baking it into the payload would make the frontend's own labels a second,
    // silently divergent copy of the same vocabulary. The funnels are the one place a human reads
    // a stage name without also having the enum's status value to hand.

    private static readonly IReadOnlyDictionary<int, string> VerificationLabels = new Dictionary<int, string>
    {
        [(int)VerificationStatus.Unverified] = "Unverified",
        [(int)VerificationStatus.Pending] = "Pending review",
        [(int)VerificationStatus.Verified] = "Verified",
        [(int)VerificationStatus.Rejected] = "Rejected",
        [(int)VerificationStatus.Suspended] = "Suspended",
    };

    private static readonly IReadOnlyDictionary<int, string> DocumentLabels = new Dictionary<int, string>
    {
        [(int)DocumentStatus.Draft] = "Draft",
        [(int)DocumentStatus.Uploaded] = "Uploaded",
        [(int)DocumentStatus.UnderReview] = "Under review",
        [(int)DocumentStatus.Verified] = "Verified",
        [(int)DocumentStatus.Rejected] = "Rejected",
        [(int)DocumentStatus.Expired] = "Expired",
    };

    private static readonly IReadOnlyDictionary<int, string> OrderLabels = new Dictionary<int, string>
    {
        [(int)OrderStatus.Draft] = "Draft",
        [(int)OrderStatus.Placed] = "Placed",
        [(int)OrderStatus.Confirmed] = "Confirmed",
        [(int)OrderStatus.InTransit] = "In transit",
        [(int)OrderStatus.Delivered] = "Delivered",
        [(int)OrderStatus.Completed] = "Completed",
        [(int)OrderStatus.Cancelled] = "Cancelled",
    };

    private static readonly IReadOnlyDictionary<int, string> PaymentLabels = new Dictionary<int, string>
    {
        [(int)PaymentStatus.None] = "Unpaid",
        [(int)PaymentStatus.Pending] = "Pending",
        [(int)PaymentStatus.PartiallyPaid] = "Partially paid",
        [(int)PaymentStatus.Paid] = "Paid",
        [(int)PaymentStatus.Refunded] = "Refunded",
        [(int)PaymentStatus.Failed] = "Failed",
    };
}