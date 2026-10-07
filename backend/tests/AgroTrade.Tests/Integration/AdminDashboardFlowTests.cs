using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AgroTrade.Application.Admin;
using AgroTrade.Domain.Enums;
using AgroTrade.Infrastructure.Auth;
using AgroTrade.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgroTrade.Tests.Integration;

/// <summary>
/// Admin dashboard, queues and audit trail, task 2.7 (plan §02 — Admin).
///
/// <para>
/// Four claims carry the weight here. The audit trail is <i>attributed and complete</i>: every
/// administrative decision leaves a row naming the administrator who made it, and that row commits
/// in the same unit of work as the decision itself. The corridor figures keep <i>currencies apart</i>,
/// because there is no FX rate anywhere in this system and a total that added NGN to USD would be a
/// confident-looking number meaning nothing. The funnels show their <i>empty</i> stages, because a
/// funnel that omits its zeros cannot show a drop. And the admin shipment queue filters on the
/// <i>derived</i> status — the same derivation 2.6 introduced — rather than on a stored column.
/// </para>
/// </summary>
public sealed class AdminDashboardFlowTests : IClassFixture<TestingWebAppFactory>
{
    private const string Password = "StrongPass123";

    private readonly TestingWebAppFactory _factory;
    private readonly HttpClient _client;

    public AdminDashboardFlowTests(TestingWebAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureSchema();
        _client = factory.CreateClient();
    }

    // ---- Access ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheAdminSurfaceIsClosedToEveryoneButAnAdministrator(bool asCompanyUser)
    {
        // The audit trail in particular is not participants' business: it is a record of who
        // reviews what, and a company must not be able to read the queue judging its competitors.
        var url = "/api/v1/admin/audit-log";

        var anonymous = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        if (!asCompanyUser)
        {
            return;
        }

        var companyToken = await CreateCompanyUserAsync($"adm_closed_{Guid.NewGuid():N}");
        var response = await GetAuthenticatedAsync(url, companyToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnImporterCannotReachTheDashboard()
    {
        var buyerToken = await RegisterAndLoginAsync($"adm_importer_{Guid.NewGuid():N}");
        var response = await GetAuthenticatedAsync("/api/v1/admin/overview", buyerToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- Audit trail ----

    [Fact]
    public async Task ApprovingABusinessWritesAnAuditEntryAttributedToTheReviewer()
    {
        var adminToken = await AdminTokenAsync();
        var adminUserId = await AdminUserIdAsync();
        var sellerToken = await CreateCompanyUserAsync($"adm_appr_{Guid.NewGuid():N}");
        var businessId = await SubmitBusinessForVerificationAsync(sellerToken);

        (await PostAuthenticatedAsync(
            $"/api/v1/admin/businesses/{businessId}/verification/review", adminToken,
            new { approve = true })).EnsureSuccessStatusCode();

        var entries = await AuditLogAsync(adminToken, "business.verification.approved");

        // One row, naming the reviewer, the business acted on, and what was decided. This is the
        // question an audit exists to answer, and it is not answerable from BusinessVerification
        // rows alone without querying each table separately.
        var mine = Assert.Single(entries.Items, i => i.EntityId == businessId);
        Assert.Equal("Business", mine.EntityType);

        // Attribution is the load-bearing part. An audit row that cannot say who acted is not
        // evidence, so the reviewer is resolved to a real account rather than left as a free field.
        Assert.Equal(adminUserId, mine.UserId);
        Assert.False(string.IsNullOrWhiteSpace(mine.ActorDisplayName));
        Assert.Contains("Approved", mine.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectingABusinessKeepsTheReasonTheReviewerGave()
    {
        var adminToken = await AdminTokenAsync();
        var sellerToken = await CreateCompanyUserAsync($"adm_rej_{Guid.NewGuid():N}");
        var businessId = await SubmitBusinessForVerificationAsync(sellerToken);

        const string reason = "Registration certificate expired in March.";
        (await PostAuthenticatedAsync(
            $"/api/v1/admin/businesses/{businessId}/verification/review", adminToken,
            new { approve = false, notes = reason })).EnsureSuccessStatusCode();

        var entry = Assert.Single(
            (await AuditLogAsync(adminToken, "business.verification.rejected")).Items,
            i => i.EntityId == businessId);

        // "Rejected" on its own is the entry an auditor would find least useful and the applicant
        // most unable to act on.
        Assert.Equal(reason, entry.Details);
        Assert.Contains("Rejected", entry.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ADocumentRejectionIsAuditedWithTheReviewersNoteIntact()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("docaudit");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken, port: UniquePort("DOCAUDIT"));
        var adminToken = await AdminTokenAsync();

        var uploaded = await UploadDocumentAsync(orderId, sellerToken, DocumentType.BillOfLading, "BL-AUDIT");
        var document = await uploaded.Content.ReadFromJsonAsync<DocumentMirror>();

        const string reason = "Consignee signature is missing on page 3.";
        (await PostAuthenticatedAsync(
            $"/api/v1/admin/documents/{document!.Id}/review", adminToken,
            new { status = (int)DocumentStatus.Rejected, notes = reason })).EnsureSuccessStatusCode();

        var entry = Assert.Single(
            (await AuditLogAsync(adminToken, "document.review.rejected")).Items,
            i => i.EntityId == document.Id);

        Assert.Equal("TradeDocument", entry.EntityType);
        Assert.Equal(reason, entry.Details);
        Assert.False(string.IsNullOrWhiteSpace(entry.Summary));
    }

    [Fact]
    public async Task TheAuditTrailIsNewestFirstAndPaginates()
    {
        var adminToken = await AdminTokenAsync();

        // The log is global and other suites review businesses concurrently, so paging it blind would assert
        // on their timing. The marker narrows the set to this run's two rows — and it is unique per
        // run because the test database is not reset between runs, so a fixed token would still match
        // every row a previous run left behind.
        var marker = Cap($"ORDERPROBE{Guid.NewGuid():N}", 24);
        var firstBusiness = await ReviewOneBusinessAsync(adminToken, marker, "1");
        var secondBusiness = await ReviewOneBusinessAsync(adminToken, marker, "2");

        var firstPage = await AuditLogAsync(adminToken, q: marker, page: 1, pageSize: 1);
        var secondPage = await AuditLogAsync(adminToken, q: marker, page: 2, pageSize: 1);

        // The most recent action leads, so paging back walks backwards through history rather than
        // reading it forwards with the newest entries stranded off the end.
        //
        // Matched on EntityId, not on the entry's own id: an audit row gets a fresh Guid of its own,
        // and comparing that against the business it acted on would be comparing two unrelated
        // identifiers and failing for reasons that have nothing to do with ordering.
        var newest = Assert.Single(firstPage.Items);
        var previous = Assert.Single(secondPage.Items);
        Assert.Equal(2, firstPage.Total);
        Assert.Equal(secondBusiness, newest.EntityId);
        Assert.Equal(firstBusiness, previous.EntityId);
        Assert.True(previous.OccurredAtUtc <= newest.OccurredAtUtc);
    }

    [Fact]
    public async Task TheAuditTrailCanBeSearchedByFreeText()
    {
        var adminToken = await AdminTokenAsync();
        var businessId = await SubmitBusinessForVerificationAsync(
            await CreateCompanyUserAsync($"adm_search_{Guid.NewGuid():N}"));
        (await PostAuthenticatedAsync(
            $"/api/v1/admin/businesses/{businessId}/verification/review", adminToken,
            new { approve = true })).EnsureSuccessStatusCode();

        // The reason given is searchable, because an admin auditing a complaint wants to find the
        // decision by what was said in it, not only by which record it landed on.
        var found = await AuditLogAsync(adminToken, q: "Approved verification for");
        Assert.Contains(found.Items, i => i.EntityId == businessId);
    }

    // ---- Corridor figures ----

    [Fact]
    public async Task CorridorShowsOrderedValueAndMoneyActuallyReceivedSideBySide()
    {
        var port = UniquePort("CORRIDOR");
        var (buyerToken, sellerToken) = await SeedTradeAsync("corr");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken, port: port, total: 12000m);

        // Part of the order is paid. The gap between the two figures is the whole reason they are
        // reported together rather than collapsed into one "sales" number.
        (await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 5000m, remittanceReference = "UTR-CORR-1" })).EnsureSuccessStatusCode();

        var corridor = await CorridorAsync(await AdminTokenAsync(), port, "USD");

        Assert.Equal(1, corridor.OrderCount);
        Assert.Equal(12000m, corridor.GrossOrderValue);
        Assert.Equal(5000m, corridor.NetSettled);
        Assert.Equal(7000m, corridor.Outstanding);
        Assert.Equal(0, corridor.MismatchedLedgerEntries);
    }

    [Fact]
    public async Task CorridorTotalsKeepCurrenciesApart()
    {
        var port = UniquePort("FX");

        // Same corridor, same ports, same country — the only difference is the currency. If these
        // were added the total would be a single confident-looking number that reconciles against
        // no bank statement, because no FX rate exists anywhere in this system to convert with.
        var (usdBuyer, usdSeller) = await SeedTradeAsync("fx_usd");
        var usdOrder = await SeedOrderAsync(usdBuyer, usdSeller, port: port, total: 100m, currency: "USD");

        var (ngnBuyer, ngnSeller) = await SeedTradeAsync("fx_ngn");
        var ngnOrder = await SeedOrderAsync(ngnBuyer, ngnSeller, port: port, total: 100m, currency: "NGN");

        (await PostAuthenticatedAsync(
            $"/api/v1/orders/{usdOrder}/payments/manual", usdSeller,
            new { amount = 100m, remittanceReference = "UTR-FX-USD" })).EnsureSuccessStatusCode();

        var adminToken = await AdminTokenAsync();
        var usd = await CorridorAsync(adminToken, port, "USD");
        var ngn = await CorridorAsync(adminToken, port, "NGN");

        // Two rows, not one. Neither row absorbed the other's money.
        Assert.Equal(100m, usd.GrossOrderValue);
        Assert.Equal(100m, usd.NetSettled);
        Assert.Equal(0m, usd.Outstanding);

        Assert.Equal(100m, ngn.GrossOrderValue);
        Assert.Equal(0m, ngn.NetSettled);
        Assert.Equal(100m, ngn.Outstanding);
    }

    // ---- Overview ----

    [Fact]
    public async Task FunnelsShowTheirEmptyStagesSoADropIsVisible()
    {
        var overview = await ReadOverviewAsync(await AdminTokenAsync());

        // "3 verified, 1 pending" is only legible next to "0 rejected": the size of the gap between
        // stages is the entire reason to draw the shape, so a stage with no rows must still appear.
        foreach (var stage in overview.OrderFunnel)
        {
            Assert.False(string.IsNullOrWhiteSpace(stage.Label));
            Assert.True(stage.Count >= 0);
        }

        Assert.Equal(
            Enum.GetValues<OrderStatus>().Length,
            overview.OrderFunnel.Count);
        Assert.Equal(
            Enum.GetValues<VerificationStatus>().Length,
            overview.BusinessVerificationFunnel.Count);
        Assert.Equal(
            Enum.GetValues<PaymentStatus>().Length,
            overview.PaymentFunnel.Count);

        // Every enum member is present, so no status can quietly vanish from the dashboard when it
        // is introduced and has no rows yet.
        var orderStatuses = overview.OrderFunnel.Select(f => f.Status).ToHashSet();
        Assert.All(Enum.GetValues<OrderStatus>(), s => Assert.Contains((int)s, orderStatuses));
    }

    [Fact]
    public async Task OverviewCountsThePlatformAndItsConsignments()
    {
        var adminToken = await AdminTokenAsync();
        var overview = await ReadOverviewAsync(adminToken);

        // Sanity floors rather than exact counts: other suites share this database and may be
        // writing concurrently, so asserting a total would be asserting on their timing too.
        Assert.True(overview.Counts.Users >= 1);
        Assert.True(overview.Counts.Businesses >= overview.Counts.VerifiedBusinesses);
        Assert.True(overview.Counts.Products >= overview.Counts.PublishedProducts);
        Assert.True(overview.Counts.Orders >= 1);
        Assert.True(overview.Counts.Transactions >= 0);
        Assert.True(overview.Counts.ShipmentsInException >= 0);
        Assert.True(overview.Counts.Shipments >= overview.Counts.ShipmentsInException);
    }

    [Fact]
    public async Task TheDashboardContrastsListedSupplyAgainstBuyerDemand()
    {
        var (sellerToken, buyerToken) = await SeedTradeForCatalogAsync("commod");
        var categories = await CategoriesAsync();
        var listed = categories[0];
        var draftOnly = categories[1];
        var wantedOnly = categories[2];

        // The database is shared and is not reset between runs, so this suite's own earlier
        // listings are still here. Every figure is therefore asserted as a change against a baseline
        // taken here, which is also the stronger claim: these listings, not somebody else's, are what
        // moved the number.
        var adminToken = await AdminTokenAsync();
        var before = await CommodityRowsAsync(adminToken, listed.Name, draftOnly.Name, wantedOnly.Name);

        // Two published listings in one category, one draft in another, and an RFQ for a category
        // nothing is listed in.
        await CreateProductAsync(sellerToken, listed, "Cocoa Beans", ListingStatus.Active);
        await CreateProductAsync(sellerToken, listed, "Cocoa Butter", ListingStatus.Active);
        await CreateProductAsync(sellerToken, draftOnly, "Rubber Sheets", ListingStatus.Draft);
        await CreateRfqAsync(buyerToken, wantedOnly);

        var after = await CommodityRowsAsync(adminToken, listed.Name, draftOnly.Name, wantedOnly.Name);

        Assert.Equal(before.Listed.ProductCount + 2, after.Listed.ProductCount);
        Assert.Equal(before.Listed.PublishedCount + 2, after.Listed.PublishedCount);

        // A draft does not read as available supply: "3 listed" when one of them cannot be bought is
        // the kind of number that makes a seller believe they have stock they cannot sell.
        Assert.Equal(before.DraftOnly.ProductCount + 1, after.DraftOnly.ProductCount);
        Assert.Equal(before.DraftOnly.PublishedCount, after.DraftOnly.PublishedCount);

        // The gap row exists at all only because the merge is driven by both sides. A category with
        // demand and no listings is the finding; had the panel iterated the product list instead,
        // this row would simply not exist and the gap would read as an absence of interest.
        Assert.Equal(before.WantedOnly.ProductCount, after.WantedOnly.ProductCount);
        Assert.Equal(before.WantedOnly.PublishedCount, after.WantedOnly.PublishedCount);
        Assert.Equal(before.WantedOnly.RfqCount + 1, after.WantedOnly.RfqCount);
        Assert.Equal(before.WantedOnly.OpenRfqCount + 1, after.WantedOnly.OpenRfqCount);

        var overview = await ReadOverviewAsync(adminToken);

        // An RFQ nobody has quoted is demand the seller is not meeting, counted as RFQs so one
        // eager supplier quoting everything cannot read as full coverage.
        Assert.True(overview.Demand.UnansweredOpenRfqs >= 1);
        Assert.True(overview.Demand.OpenRfqs >= 1);

        // An open RFQ exists, so a currency must be reported — and it is either the one agreed on or
        // the explicit "Mixed" marker. Never blank: blank is reserved for "nothing is open", and
        // using it for a populated set would claim there is no demand when there plainly is.
        Assert.NotEqual(string.Empty, overview.Demand.PreferredCurrency);

        // Withheld rather than approximated when open RFQs disagree on currency: an average across
        // mixed currencies is a sum of incomparable numbers wearing a decimal point.
        if (overview.Demand.PreferredCurrency == "Mixed")
        {
            Assert.Null(overview.Demand.AverageBudgetCeiling);
        }
        else
        {
            Assert.True(overview.Demand.AverageBudgetCeiling is null or >= 0m);
        }
    }

    [Fact]
    public async Task NoOpenDemandReportsNoCurrencyRatherThanClaimingItIsMixed()
    {
        // "No open RFQs" is a global state, so reaching it means touching RFQs this test does not
        // own. The statuses are captured and restored in the finally below: the shared test
        // database is never reset between runs, so leaving every open RFQ closed would quietly
        // drain demand out of the fixtures other classes rely on, a little further on every run.
        var original = new Dictionary<Guid, RfqStatus>();

        try
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Data.AgroTradeDbContext>();
                var open = await db.TradeRequests
                    .Where(r => r.Status == RfqStatus.Open || r.Status == RfqStatus.Quoting)
                    .ToListAsync();

                foreach (var rfq in open)
                {
                    original[rfq.Id] = rfq.Status;
                    rfq.Status = RfqStatus.Closed;
                }

                await db.SaveChangesAsync();
            }

            var demand = (await ReadOverviewAsync(await AdminTokenAsync())).Demand;

            Assert.Equal(0, demand.OpenRfqs);
            Assert.Equal(0, demand.UnansweredOpenRfqs);

            // Empty, not "Mixed". Nothing is open, so there is no currency and no disagreement
            // either. "Mixed" here would assert that buyers want several currencies at once, when
            // there are no buyers at all.
            Assert.Equal(string.Empty, demand.PreferredCurrency);
            Assert.Null(demand.AverageBudgetCeiling);
        }
        finally
        {
            if (original.Count > 0)
            {
                using var scope = _factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Data.AgroTradeDbContext>();
                foreach (var rfq in await db.TradeRequests
                             .Where(r => original.Keys.Contains(r.Id))
                             .ToListAsync())
                {
                    rfq.Status = original[rfq.Id];
                }

                await db.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task AnAbandonedRfqIsHistoryRatherThanLiveDemand()
    {
        var buyerToken = await RegisterAndLoginAsync($"adm_closedb_{Guid.NewGuid():N}");
        var categories = await CategoriesAsync();
        var categoryName = categories[0].Name;

        // Relative, not absolute. The shared test database is never reset and test classes run in
        // parallel against it, so "this category has exactly one open RFQ" is not a fact anybody
        // can rely on — another class may add or close one mid-assert. What is reliable is how
        // this RFQ's own lifecycle moves the two counters, which is the contrast being tested.
        var before = await CommodityRowAsync(categoryName);

        var (rfqId, _) = await CreateRfqAsync(buyerToken, categories[0]);

        var whileOpen = await CommodityRowAsync(categoryName);
        Assert.True(
            whileOpen.OpenRfqCount > before.OpenRfqCount,
            $"An open RFQ must raise live demand: {before.OpenRfqCount} -> {whileOpen.OpenRfqCount}.");

        // Close it. The RFQ is now history, and the panel must stop calling it current demand —
        // otherwise a category nobody has asked about since would be flagged as a live gap.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Data.AgroTradeDbContext>();
            var rfq = await db.TradeRequests.FirstAsync(r => r.Id == rfqId);
            rfq.Status = RfqStatus.Closed;
            await db.SaveChangesAsync();
        }

        var afterClose = await CommodityRowAsync(categoryName);

        // The split the whole panel rests on, stated as a contrast between the two counters:
        // closing an RFQ must pull it out of demand while leaving it counted as interest. A closed
        // RFQ that still read as live demand would make the gap flag fire on categories buyers
        // walked away from years ago.
        Assert.True(
            afterClose.OpenRfqCount < whileOpen.OpenRfqCount,
            $"A closed RFQ must leave live demand: {whileOpen.OpenRfqCount} -> {afterClose.OpenRfqCount}.");

        Assert.True(
            afterClose.RfqCount >= whileOpen.RfqCount,
            "Lifetime interest cannot shrink when an RFQ is closed.");
    }

    // ---- Queues ----

    [Fact]
    public async Task TheShipmentQueueSeesEveryPartysConsignment()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("cross");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken, port: UniquePort("CROSS"));
        var shipment = await BookAsync(orderId, sellerToken, new { mode = (int)ShipmentMode.Sea, carrierName = "Maersk" });

        var adminToken = await AdminTokenAsync();
        var found = await GetAuthenticatedAsync(
            $"/api/v1/admin/shipments?q={Uri.EscapeDataString(shipment.Reference)}", adminToken);
        found.EnsureSuccessStatusCode();

        var page = await found.Content.ReadFromJsonAsync<PagedMirror<ShipmentRowMirror>>();
        var row = Assert.Single(page!.Items);
        Assert.Equal(shipment.Id, row.Id);
        Assert.Equal(orderId, row.OrderId);
        Assert.Equal((int)ShipmentStatus.Booked, row.Status);
        Assert.Equal(1, row.MilestoneCount);
        Assert.Equal(shipment.Reference, row.Reference);
    }

    [Fact]
    public async Task TheShipmentQueueFiltersOnTheStatusDerivedFromTheLatestMilestone()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("derived");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken, port: UniquePort("DERIVED"));
        var shipment = await BookAsync(orderId, sellerToken, new { mode = (int)ShipmentMode.Sea, carrierName = "Maersk" });

        await AppendEventAsync(shipment.Id, sellerToken, ShipmentStatus.Departed);

        var adminToken = await AdminTokenAsync();

        var departed = await GetAuthenticatedAsync(
            $"/api/v1/admin/shipments?status={(int)ShipmentStatus.Departed}&q={Uri.EscapeDataString(shipment.Reference)}",
            adminToken);
        departed.EnsureSuccessStatusCode();
        var matched = await departed.Content.ReadFromJsonAsync<PagedMirror<ShipmentRowMirror>>();
        Assert.Single(matched!.Items);

        // Status is a method call on the loaded timeline, not a column, so filtering it in memory is
        // the only option. The failure this guards against is subtle: a queue that filtered on the
        // *stored* status of a shipment loaded without its events would match nothing at all and
        // look like an empty database rather than a broken filter.
        var booked = await GetAuthenticatedAsync(
            $"/api/v1/admin/shipments?status={(int)ShipmentStatus.Booked}&q={Uri.EscapeDataString(shipment.Reference)}",
            adminToken);
        booked.EnsureSuccessStatusCode();
        var stale = await booked.Content.ReadFromJsonAsync<PagedMirror<ShipmentRowMirror>>();
        Assert.Empty(stale!.Items);
    }

    [Fact]
    public async Task TheShipmentQueueReportsWhetherGuestTrackingIsOn()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("guest");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken, port: UniquePort("GUEST"));

        // Opted out at booking, so the queue has to reflect a real absence rather than the
        // default-on state every other consignment in the suite carries.
        var optedOut = await BookAsync(orderId, sellerToken, new
        {
            mode = (int)ShipmentMode.Sea,
            carrierName = "Maersk",
            enableGuestTracking = false,
        });

        var adminToken = await AdminTokenAsync();
        Assert.False((await ShipmentRowAsync(adminToken, optedOut.Reference)).HasGuestTracking);

        (await PostAuthenticatedAsync(
            $"/api/v1/shipments/{optedOut.Id}/tracking-token", sellerToken, new { })).EnsureSuccessStatusCode();

        // Worth surfacing: a live guest token is a bearer secret being handed to people with no
        // account, and an admin asking "who is this link shared with" needs to know it exists.
        Assert.True((await ShipmentRowAsync(adminToken, optedOut.Reference)).HasGuestTracking);

        // And filterable by it. A token cannot be listed, but "which consignments have a link in
        // circulation" is a question an admin asks, and reading every row to answer it is not a
        // method — while a silent guest link is exactly the thing worth noticing in the first place.
        var withLink = await GetAuthenticatedAsync(
            $"/api/v1/admin/shipments?hasGuestTracking=true&q={Uri.EscapeDataString(optedOut.Reference)}",
            adminToken);
        withLink.EnsureSuccessStatusCode();
        Assert.Single((await withLink.Content.ReadFromJsonAsync<PagedMirror<ShipmentRowMirror>>())!.Items);

        var withoutLink = await GetAuthenticatedAsync(
            $"/api/v1/admin/shipments?hasGuestTracking=false&q={Uri.EscapeDataString(optedOut.Reference)}",
            adminToken);
        withoutLink.EnsureSuccessStatusCode();
        Assert.Empty((await withoutLink.Content.ReadFromJsonAsync<PagedMirror<ShipmentRowMirror>>())!.Items);
    }

    [Fact]
    public async Task TheOrderQueueShowsWhatWasPaidNotJustWhatWasOrdered()
    {
        var port = UniquePort("ORDQ");
        var (buyerToken, sellerToken) = await SeedTradeAsync("ordq");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken, port: port, total: 12000m);

        (await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 7000m, remittanceReference = "UTR-ORDQ-1" })).EnsureSuccessStatusCode();

        var adminToken = await AdminTokenAsync();
        var orderNumber = await GetOrderNumberAsync(buyerToken, orderId);
        var listed = await GetAuthenticatedAsync(
            $"/api/v1/admin/orders?q={Uri.EscapeDataString(orderNumber)}", adminToken);
        listed.EnsureSuccessStatusCode();
        var order = await listed.Content.ReadFromJsonAsync<PagedMirror<OrderRowMirror>>();

        var row = Assert.Single(order!.Items);
        Assert.Equal(12000m, row.Total);
        Assert.Equal(7000m, row.AmountPaid);
        Assert.Equal("USD", row.Currency);
        Assert.Equal((int)PaymentStatus.PartiallyPaid, row.PaymentStatus);
        Assert.False(string.IsNullOrWhiteSpace(row.BuyerDisplayName));
        Assert.False(string.IsNullOrWhiteSpace(row.SellerTradingName));
    }

    [Fact]
    public async Task TheUserQueueResolvesRolesAndTheCompanyEachUserOwns()
    {
        var email = $"adm_users_{Guid.NewGuid():N}@admin.tests.agrotrade.local";
        var token = await CreateCompanyUserAsync(email.Split('@')[0]);
        var businessId = await SubmitBusinessForVerificationAsync(token);

        var adminToken = await AdminTokenAsync();
        var page = await GetAuthenticatedAsync(
            $"/api/v1/admin/users?q={Uri.EscapeDataString(email)}", adminToken);
        page.EnsureSuccessStatusCode();

        var found = await page.Content.ReadFromJsonAsync<PagedMirror<UserRowMirror>>();
        var row = Assert.Single(found!.Items);

        // Roles and trading name are resolved server-side: the admin screen has no way to join
        // Identity's role table itself, and an unresolved role list would just be blank.
        Assert.Contains(RoleNames.Company, row.Roles);
        Assert.True(row.EmailConfirmed);
        Assert.False(row.LockedOut);
        Assert.NotNull(row.BusinessName);

        // A registration timestamp, not a null. The queue orders by it, and the entity once left it
        // unstamped for every user in the database — so a null here means "newest first" is silently
        // an arbitrary order wearing a plausible label.
        Assert.NotNull(row.CreatedAtUtc);

        // Filterable by role name, which is the only form a client can supply — role ids are
        // database-generated and undiscoverable from the API. Narrowed by the same search term so the
        // result is one row rather than whichever hundred of thousands of company accounts happen
        // to sort first.
        var byRole = await GetAuthenticatedAsync(
            $"/api/v1/admin/users?role={Uri.EscapeDataString(RoleNames.Company)}&q={Uri.EscapeDataString(email)}",
            adminToken);
        byRole.EnsureSuccessStatusCode();
        var companies = await byRole.Content.ReadFromJsonAsync<PagedMirror<UserRowMirror>>();
        var narrowed = Assert.Single(companies!.Items);
        Assert.Equal(row.Id, narrowed.Id);
        Assert.Contains(RoleNames.Company, narrowed.Roles);

        Assert.NotEqual(Guid.Empty, businessId);
    }

    [Fact]
    public async Task TheModerationQueueSeesEveryListingAndNamesItsSeller()
    {
        var tag = $"modq_{Guid.NewGuid():N}";
        var sellerToken = await VerifyBusinessAsync(await CreateCompanyUserAsync(tag));
        var category = await PickCategoryAsync();
        var (productId, _) = await CreateProductAsync(sellerToken, category, "moderation-queue", ListingStatus.Active);

        var page = await GetAuthenticatedAsync(
            $"/api/v1/admin/products?q={Uri.EscapeDataString(productId.ToString())}", await AdminTokenAsync());
        page.EnsureSuccessStatusCode();

        var rows = (await page.Content.ReadFromJsonAsync<PagedMirror<AdminProductRowMirror>>())!.Items;
        var row = Assert.Single(rows, r => r.Id == productId);

        // Resolved server-side. The queue is read by an administrator who has no way to join the
        // business table, and an unresolved seller is a row nobody can act on.
        Assert.False(string.IsNullOrWhiteSpace(row.SellerTradingName));
        Assert.Equal(category.Name, row.CategoryName);
        Assert.Equal("USD", row.Currency);
        Assert.True(row.IsVisibleInMarketplace);
    }

    [Fact]
    public async Task ASuspensionHidesTheListingFromTheMarketplaceAndIsAttributed()
    {
        var tag = $"mods_{Guid.NewGuid():N}";
        var sellerToken = await VerifyBusinessAsync(await CreateCompanyUserAsync(tag));
        var category = await PickCategoryAsync();
        var (productId, _) = await CreateProductAsync(sellerToken, category, "suspend-me", ListingStatus.Active);

        var adminToken = await AdminTokenAsync();
        var adminId = await AdminUserIdAsync();

        var before = await MarketplaceContainsAsync(productId, anonymous: true);
        Assert.True(before, "Precondition: a published listing is visible in the marketplace.");

        var response = await PatchAuthenticatedAsync(
            $"/api/v1/admin/products/{productId}/moderation", adminToken,
            new { action = (int)ProductModerationAction.Suspend, reason = "Phytosanitary certificate does not match the lot." });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var moderated = (await response.Content.ReadFromJsonAsync<AdminProductRowMirror>())!;
        Assert.Equal((int)ModerationStatus.Suspended, moderated.ModerationStatus);
        Assert.Equal("Phytosanitary certificate does not match the lot.", moderated.ModerationReason);
        Assert.Equal(adminId, moderated.SuspendedByUserId);
        Assert.NotNull(moderated.SuspendedAtUtc);

        // Visibility is not merely flagged - the listing is actually gone from every buyer-facing
        // read. A moderation control that only sets a field the marketplace ignores is a comment.
        Assert.False(moderated.IsVisibleInMarketplace);
        Assert.False(await MarketplaceContainsAsync(productId, anonymous: true));

        // And it is audited with the reason intact, so the decision outlives the row's UI state.
        var log = await AuditLogAsync(adminToken, q: productId.ToString());
        Assert.NotEmpty(log.Items);
        Assert.Contains(log.Items, a =>
            a.Action == "product.moderation.suspended"
            && a.Details == "Phytosanitary certificate does not match the lot.");
    }

    [Fact]
    public async Task ASellerCannotUndoASuspensionByRepublishingTheirOwnListing()
    {
        // The load-bearing claim of moderation being a separate field rather than a ListingStatus
        // value. Without this check the seller undoes an administrator's decision with one PATCH,
        // and the control protects nothing.
        var tag = $"modr_{Guid.NewGuid():N}";
        var sellerToken = await VerifyBusinessAsync(await CreateCompanyUserAsync(tag));
        var category = await PickCategoryAsync();
        var (productId, _) = await CreateProductAsync(sellerToken, category, "undodgeable", ListingStatus.Active);

        var adminToken = await AdminTokenAsync();
        var suspend = await PatchAuthenticatedAsync(
            $"/api/v1/admin/products/{productId}/moderation", adminToken,
            new { action = (int)ProductModerationAction.Suspend, reason = "Under review by the platform." });
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);

        var retry = await PatchAuthenticatedAsync(
            $"/api/v1/products/{productId}", sellerToken, new { listingStatus = (int)ListingStatus.Active });

        Assert.Equal(HttpStatusCode.Forbidden, retry.StatusCode);
        Assert.Contains("LISTING_SUSPENDED", await retry.Content.ReadAsStringAsync());

        // Still hidden afterwards. A refused request that nonetheless republished would leave the
        // API honest and the marketplace wrong.
        Assert.False(await MarketplaceContainsAsync(productId, anonymous: true));
    }

    [Fact]
    public async Task ASellerCanStillSeeAndEditASuspendedListingTheyOwn()
    {
        // The other half of the previous test: moderation hides a listing from buyers, it does not
        // lock the seller out of their own business. A seller left guessing why their listing
        // vanished has no way to appeal.
        var tag = $"mode_{Guid.NewGuid():N}";
        var sellerToken = await VerifyBusinessAsync(await CreateCompanyUserAsync(tag));
        var category = await PickCategoryAsync();
        var (productId, _) = await CreateProductAsync(sellerToken, category, "still-mine", ListingStatus.Active);

        var suspend = await PatchAuthenticatedAsync(
            $"/api/v1/admin/products/{productId}/moderation", await AdminTokenAsync(),
            new { action = (int)ProductModerationAction.Suspend, reason = "Awaiting phytosanitary certificate." });
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);

        var detail = await GetAuthenticatedAsync($"/api/v1/products/{productId}", sellerToken);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        // Editing ordinary fields still works. Only republishing is refused.
        var edit = await PatchAuthenticatedAsync(
            $"/api/v1/products/{productId}", sellerToken, new { price = 2750m });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
    }

    [Fact]
    public async Task SuspendingRequiresAReasonAndReinstatementDoesNotRepublish()
    {
        var tag = $"modv_{Guid.NewGuid():N}";
        var sellerToken = await VerifyBusinessAsync(await CreateCompanyUserAsync(tag));
        var category = await PickCategoryAsync();
        var (productId, _) = await CreateProductAsync(sellerToken, category, "needs-reason", ListingStatus.Active);
        var adminToken = await AdminTokenAsync();

        // No reason: refused, and the listing is untouched.
        var noReason = await PatchAuthenticatedAsync(
            $"/api/v1/admin/products/{productId}/moderation", adminToken, new { action = (int)ProductModerationAction.Suspend });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal((int)ModerationStatus.Cleared, (await ProductRowAsync(adminToken, productId)).ModerationStatus);

        var suspend = await PatchAuthenticatedAsync(
            $"/api/v1/admin/products/{productId}/moderation", adminToken,
            new { action = (int)ProductModerationAction.Suspend, reason = "Certificate expired." });
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);

        var reinstated = await PatchAuthenticatedAsync(
            $"/api/v1/admin/products/{productId}/moderation", adminToken,
            new { action = (int)ProductModerationAction.Reinstate, reason = "Certificate renewed." });
        Assert.Equal(HttpStatusCode.OK, reinstated.StatusCode);

        var row = (await reinstated.Content.ReadFromJsonAsync<AdminProductRowMirror>())!;
        Assert.Equal((int)ModerationStatus.Cleared, row.ModerationStatus);
        Assert.Null(row.ModerationReason);
        Assert.Null(row.SuspendedByUserId);

        // The seller's own visibility state is untouched by moderation in either direction. An admin
        // quietly publishing a seller's draft would be making the seller's content decision for them.
        Assert.Equal((int)ListingStatus.Active, row.ListingStatus);
        Assert.True(row.IsVisibleInMarketplace);
    }

    [Fact]
    public async Task ReinstatingSomethingThatWasNeverSuspendedIsAConflict()
    {
        // Both no-op transitions are refused. Silently succeeding would leave an administrator
        // believing they had lifted a restriction that was never there.
        var tag = $"modn_{Guid.NewGuid():N}";
        var sellerToken = await VerifyBusinessAsync(await CreateCompanyUserAsync(tag));
        var category = await PickCategoryAsync();
        var (productId, _) = await CreateProductAsync(sellerToken, category, "never-suspended", ListingStatus.Active);
        var adminToken = await AdminTokenAsync();

        var redundant = await PatchAuthenticatedAsync(
            $"/api/v1/admin/products/{productId}/moderation", adminToken,
            new { action = (int)ProductModerationAction.Reinstate });
        Assert.Equal(HttpStatusCode.Conflict, redundant.StatusCode);

        await PatchAuthenticatedAsync(
            $"/api/v1/admin/products/{productId}/moderation", adminToken,
            new { action = (int)ProductModerationAction.Suspend, reason = "First suspension." });

        var again = await PatchAuthenticatedAsync(
            $"/api/v1/admin/products/{productId}/moderation", adminToken,
            new { action = (int)ProductModerationAction.Suspend, reason = "Second suspension." });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        // The original reason survives the refused second attempt. Overwriting it is how a
        // moderation trail stops being a record of anything.
        Assert.Equal("First suspension.", (await ProductRowAsync(adminToken, productId)).ModerationReason);
    }

    [Fact]
    public async Task ASuspensionRemovesTheListingFromCountedSupplyButNotFromTheCatalogue()
    {
        // The dashboard's supply figure has to mean buyable. A suspended listing counted as supply
        // tells an operator we can serve a category from stock no buyer can order, which is the
        // false signal the commodity panel exists to avoid.
        var tag = $"modd_{Guid.NewGuid():N}";
        var sellerToken = await VerifyBusinessAsync(await CreateCompanyUserAsync(tag));
        var category = await PickCategoryAsync();
        var (mine, mineName) = await CreateProductAsync(sellerToken, category, "supply-probe", ListingStatus.Active);

        var adminToken = await AdminTokenAsync();
        var publishedBefore = await CommodityRowAsync(adminToken, category.Name);

        var mineLookup = await SingleCategoryProductAsync(adminToken, category.Name, mineName);
        var suspend = await PatchAuthenticatedAsync(
            $"/api/v1/admin/products/{mineLookup}/moderation", adminToken,
            new { action = (int)ProductModerationAction.Suspend, reason = "Withdrawn pending checks." });
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);

        var publishedAfter = await CommodityRowAsync(adminToken, category.Name);

        Assert.Equal(publishedBefore.PublishedCount - 1, publishedAfter.PublishedCount);

        // Still counted as a listing. It exists, it belongs to someone, and hiding it from supply
        // must not erase it from the catalogue.
        Assert.Equal(publishedBefore.ProductCount, publishedAfter.ProductCount);

        // And the dashboard strip reports it separately, so "suspended" never gets folded into
        // "not published" where an operator would read it as a seller who withdrew their listing.
        var overview = await ReadOverviewAsync(adminToken);
        Assert.True(overview.Counts.SuspendedProducts >= 1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ModerationIsClosedToNonAdministrators(bool asCompanyUser)
    {
        var tag = $"modz_{Guid.NewGuid():N}";
        var sellerToken = await VerifyBusinessAsync(await CreateCompanyUserAsync(tag));
        var category = await PickCategoryAsync();
        var (productId, _) = await CreateProductAsync(sellerToken, category, "closed-off", ListingStatus.Active);

        var strangerToken = asCompanyUser
            ? await CreateCompanyUserAsync($"modz_other_{Guid.NewGuid():N}")
            : await RegisterAndLoginAsync($"modz_pub_{Guid.NewGuid():N}");

        var queue = await GetAuthenticatedAsync("/api/v1/admin/products", strangerToken);
        Assert.Equal(HttpStatusCode.Forbidden, queue.StatusCode);

        // The write is the one that matters. A read-only queue leaking listing data is a much
        // smaller problem than any account being able to suspend a competitor's stock.
        var write = await PatchAuthenticatedAsync(
            $"/api/v1/admin/products/{productId}/moderation", strangerToken,
            new { action = (int)ProductModerationAction.Suspend, reason = "Not mine to decide." });
        Assert.True(
            write.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"Expected 401/403 for a non-administrator, got {(int)write.StatusCode}.");

        Assert.Equal((int)ModerationStatus.Cleared, (await ProductRowAsync(await AdminTokenAsync(), productId)).ModerationStatus);
    }

    [Fact]
    public async Task TheRfqQueueSeesEveryBuyersRequestIncludingItsOwn()
    {
        var tag = $"rfqq_{Guid.NewGuid():N}";
        var buyerToken = await RegisterAndLoginAsync($"rfqb_{Guid.NewGuid():N}");
        var category = await PickCategoryAsync();
        var (rfqId, token) = await CreateRfqAsync(buyerToken, category);

        var page = await GetAuthenticatedAsync(
            $"/api/v1/admin/rfqs?q={Uri.EscapeDataString(token)}", await AdminTokenAsync());
        page.EnsureSuccessStatusCode();

        var rows = (await page.Content.ReadFromJsonAsync<PagedMirror<AdminRfqRowMirror>>())!.Items;
        var row = Assert.Single(rows, r => r.Id == rfqId);

        Assert.False(string.IsNullOrWhiteSpace(row.RfqNumber));
        Assert.Equal("USD", row.PreferredCurrency);
        Assert.Equal(category.Name, row.CategoryName);
        Assert.NotNull(row.BuyerDisplayName);

        // Brand new and nobody has quoted it: the exact row the "unanswered" filter exists to find.
        Assert.Equal(0, row.QuotationCount);
    }

    [Fact]
    public async Task TheRfqQueueSeparatesDemandNobodyHasAnsweredFromDemandThatWas()
    {
        var buyerToken = await RegisterAndLoginAsync($"rfqu_b_{Guid.NewGuid():N}");
        var sellerToken = await VerifyBusinessAsync(await CreateCompanyUserAsync($"rfqu_s_{Guid.NewGuid():N}"));
        var category = await PickCategoryAsync();
        var adminToken = await AdminTokenAsync();

        var (answeredId, answeredToken) = await CreateRfqAsync(buyerToken, category);

        var quoted = await PostAuthenticatedAsync($"/api/v1/rfqs/{answeredId}/quotations", sellerToken, new
        {
            currency = "USD",
            incoterms = (int)Incoterms.Cif,
            portOfLoading = "Mombasa (MBA)",
            leadTimeDays = 21,
            paymentTerms = (int)PaymentTerms.LetterOfCredit,
            total = 12500m,
        });
        Assert.Equal(HttpStatusCode.Created, quoted.StatusCode);

        // A second RFQ the seller never sees, so the two are distinguishable by answer state alone.
        var (unansweredId, unansweredToken) = await CreateRfqAsync(
            await RegisterAndLoginAsync($"rfqu_b2_{Guid.NewGuid():N}"), category);

        var answeredRow = await SingleRfqRowAsync(adminToken, answeredToken);
        var unansweredRow = await SingleRfqRowAsync(adminToken, unansweredToken);

        Assert.Equal(1, answeredRow.QuotationCount);
        Assert.Equal(0, unansweredRow.QuotationCount);

        // The filter is about the RFQ, not the count of offers: one eager supplier quoting everything
        // must not be able to make the whole board read as covered.
        var openOnly = await GetAuthenticatedAsync("/api/v1/admin/rfqs?unanswered=true&pageSize=100", adminToken);
        openOnly.EnsureSuccessStatusCode();
        var unanswered = (await openOnly.Content.ReadFromJsonAsync<PagedMirror<AdminRfqRowMirror>>())!.Items;

        Assert.Contains(unanswered, r => r.Id == unansweredId);
        Assert.DoesNotContain(unanswered, r => r.Id == answeredId);
        Assert.All(unanswered, r => Assert.Equal(0, r.QuotationCount));

        // The negative filter is the same claim stated from the other side: an RFQ with offers on it
        // is demand that has been met, and asking for only those must never return one without.
        var covered = await GetAuthenticatedAsync("/api/v1/admin/rfqs?unanswered=false&pageSize=100", adminToken);
        covered.EnsureSuccessStatusCode();
        var answered = (await covered.Content.ReadFromJsonAsync<PagedMirror<AdminRfqRowMirror>>())!.Items;

        Assert.Contains(answered, r => r.Id == answeredId);
        Assert.DoesNotContain(answered, r => r.Id == unansweredId);
    }

    [Fact]
    public async Task TheRfqQueueIsClosedToNonAdministrators()
    {
        var token = await CreateCompanyUserAsync($"rfqz_{Guid.NewGuid():N}");
        var response = await GetAuthenticatedAsync("/api/v1/admin/rfqs", token);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- Moderation helpers ----

    /// <summary>
    /// The one RFQ matching a per-run-unique token, asserting exactly one matched.
    /// </summary>
    /// <remarks>
    /// The admin queue has no id filter, and the shared test database holds every RFQ ever created
    /// by every run of every test. Paging until a known id appears would tie this assertion to how
    /// much data happened to accumulate; searching for something unique to this row is stable.
    /// </remarks>
    private async Task<AdminRfqRowMirror> SingleRfqRowAsync(string adminToken, string token)
    {
        var page = await GetAuthenticatedAsync(
            $"/api/v1/admin/rfqs?q={Uri.EscapeDataString(token)}", adminToken);
        page.EnsureSuccessStatusCode();
        var rows = (await page.Content.ReadFromJsonAsync<PagedMirror<AdminRfqRowMirror>>())!.Items;
        return Assert.Single(rows);
    }

    /// <summary>
    /// Whether a listing is actually reachable through the public marketplace read.
    /// </summary>
    /// <remarks>
    /// Goes through the buyer-facing endpoint rather than asserting on a DTO flag, because the
    /// claim being tested is that the listing is hidden - not that some field says it is.
    /// </remarks>
    private async Task<bool> MarketplaceContainsAsync(Guid productId, bool anonymous)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/products/{productId}");
        if (!anonymous)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await RegisterAndLoginAsync($"mp_{Guid.NewGuid():N}"));
        }

        var response = await _client.SendAsync(request);

        // A stranger gets 404 rather than 403 for anything not published, so "not found" and
        // "forbidden" both mean "not visible", and both must stop the marketplace finding it.
        return response.StatusCode == HttpStatusCode.OK;
    }

    private async Task<AdminProductRowMirror> ProductRowAsync(string adminToken, Guid productId)
    {
        var page = await GetAuthenticatedAsync($"/api/v1/admin/products?pageSize=100", adminToken);
        page.EnsureSuccessStatusCode();
        var rows = (await page.Content.ReadFromJsonAsync<PagedMirror<AdminProductRowMirror>>())!.Items;
        return Assert.Single(rows, r => r.Id == productId);
    }

    /// <summary>
    /// The one listing in a category whose name contains the marker, asserted to be exactly one.
    /// </summary>
    /// <remarks>
    /// The shared database already holds listings in every seeded category, so a category row on
    /// its own cannot identify which product a test just created. The marker makes the lookup
    /// unambiguous without depending on any category being empty.
    /// </remarks>
    private async Task<Guid> SingleCategoryProductAsync(string adminToken, string categoryName, string marker)
    {
        var page = await GetAuthenticatedAsync(
            $"/api/v1/admin/products?pageSize=100", adminToken);
        page.EnsureSuccessStatusCode();
        var rows = (await page.Content.ReadFromJsonAsync<PagedMirror<AdminProductRowMirror>>())!.Items;
        return Assert.Single(rows, r => r.CategoryName == categoryName && r.Name.Contains(marker, StringComparison.Ordinal)).Id;
    }

    private async Task<CommodityMirror> CommodityRowAsync(string adminToken, string categoryName)
    {
        var overview = await ReadOverviewAsync(adminToken);
        return Assert.Single(overview.Commodities, c => c.CategoryName == categoryName);
    }

    // ---- Seeding ----

    /// <summary>Ports are unique per test so corridor assertions are exact even though the
    /// database is shared with suites running in parallel.</summary>
    private static string UniquePort(string tag) => Cap($"Port-{tag}-{Guid.NewGuid():N}", 24);

    /// <summary>Truncates to a column's limit. A range operator on a shorter string throws,
    /// so every generated name is capped rather than assumed long enough.</summary>
    private static string Cap(string value, int max = 40) =>
        value.Length <= max ? value : value[..max];

    private async Task<Guid> ReviewOneBusinessAsync(string adminToken, string marker, string tag)
    {
        var sellerToken = await CreateCompanyUserAsync($"adm_{tag}_{Guid.NewGuid():N}");
        var businessId = await SubmitBusinessForVerificationAsync(sellerToken, marker);
        (await PostAuthenticatedAsync(
            $"/api/v1/admin/businesses/{businessId}/verification/review", adminToken,
            new { approve = true })).EnsureSuccessStatusCode();
        return businessId;
    }

    private async Task<Guid> SubmitBusinessForVerificationAsync(string token, string marker = "")
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var tradingName = Cap($"Admin Supplier {marker}{tag}");

        var registered = await PostAuthenticatedAsync("/api/v1/businesses", token, new
        {
            legalName = $"{tradingName} Ltd",
            tradingName,
            country = "NG",
            businessType = (int)BusinessType.LimitedCompany,
        });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        var businessId = await SupplierBusinessIdAsync(token);

        var upload = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/businesses/{businessId}/documents")
        {
            Content = new MultipartFormDataContent
            {
                { new StringContent(((int)DocumentType.BusinessRegistration).ToString()), "docType" },
                { PdfPart(), "file", "certificate.pdf" },
            },
        };
        upload.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await _client.SendAsync(upload)).EnsureSuccessStatusCode();

        var submit = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/businesses/{businessId}/verification-submissions");
        submit.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await _client.SendAsync(submit)).EnsureSuccessStatusCode();

        return businessId;
    }

    private async Task<(string BuyerToken, string SellerToken)> SeedTradeAsync(string tag)
    {
        var sellerToken = await VerifyBusinessAsync(
            await CreateCompanyUserAsync($"adm_seller_{tag}_{Guid.NewGuid():N}"));

        var buyerToken = await RegisterAndLoginAsync($"adm_buyer_{tag}_{Guid.NewGuid():N}");
        return (buyerToken, sellerToken);
    }

    /// <summary>Seller first, so a catalogue listing can be attributed to a verified business.</summary>
    private async Task<(string SellerToken, string BuyerToken)> SeedTradeForCatalogAsync(string tag)
    {
        var sellerToken = await VerifyBusinessAsync(
            await CreateCompanyUserAsync($"adm_cat_{tag}_{Guid.NewGuid():N}"));

        var buyerToken = await RegisterAndLoginAsync($"adm_catb_{tag}_{Guid.NewGuid():N}");
        return (sellerToken, buyerToken);
    }

    /// <summary>
    /// The catalogue needs categories to file products and RFQs against, and the shared test
    /// database ships with none — the factory seeds roles and an administrator, nothing else.
    /// Created through the DbContext rather than the API because there is no admin endpoint for it.
    /// </summary>
    private async Task<List<CategoryMirror>> CategoriesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Data.AgroTradeDbContext>();

        if (!await db.Categories.AnyAsync())
        {
            foreach (var name in new[] { "Cocoa", "Rubber", "Grains", "Coffee" })
            {
                db.Categories.Add(new Domain.Trade.Category
                {
                    Name = name,
                    Slug = name.ToLowerInvariant(),
                    IsActive = true,
                });
            }

            await db.SaveChangesAsync();
        }

        var all = await db.Categories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategoryMirror { Id = c.Id, ParentId = c.ParentId, Name = c.Name, IsActive = c.IsActive })
            .ToListAsync();

        // Only leaves. The tree's parents are groupings rather than things a buyer requests, and a
        // product filed against one would distort both halves of the panel.
        var parentIds = all.Where(c => c.ParentId.HasValue).Select(c => c.ParentId!.Value).ToHashSet();
        return all.Where(c => !parentIds.Contains(c.Id)).ToList();
    }

    private async Task<CategoryMirror> PickCategoryAsync()
    {
        var categories = await CategoriesAsync();
        Assert.True(categories.Count >= 3, "The commodity contrast needs three distinct categories.");
        return categories[0];
    }

    private async Task<(Guid Id, string Name)> CreateProductAsync(
        string token, CategoryMirror category, string name, ListingStatus status)
    {
        var full = Cap($"{name} {Guid.NewGuid():N}");
        var response = await PostAuthenticatedAsync("/api/v1/products", token, new
        {
            name = full,
            categoryId = category.Id,
            originCountry = "NG",
            price = 2500m,
            currency = "USD",
            availableQuantity = 40m,
            minimumOrderQuantity = 5m,
            listingStatus = (int)status,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<ProductMirror>())!.Id;
        return (id, full);
    }

    /// <summary>
    /// Creates an RFQ and returns its id plus a token unique to it.
    /// </summary>
    /// <remarks>
    /// The token exists because the admin queues are searched by free text rather than by id, and
    /// the shared test database holds hundreds of rows accumulated over many runs. Paging until a
    /// known id turns up would make a test's assertions depend on how many other tests ran before
    /// it; searching for something unique to this row is the only stable way to find it.
    /// </remarks>
    private async Task<(Guid Id, string Token)> CreateRfqAsync(string token, CategoryMirror category)
    {
        var marker = Guid.NewGuid().ToString("N");
        var response = await PostAuthenticatedAsync("/api/v1/rfqs", token, new
        {
            title = Cap($"demand for {category.Name} {marker}", 80),
            categoryId = category.Id,
            quantity = 5m,
            unit = (int)PriceUnit.PerTon,
            destinationCountry = "NG",
            destinationPortOrCity = "Lagos",
            preferredIncoterm = (int)Incoterms.Cif,
            preferredCurrency = "USD",
            expiresAt = DateTime.UtcNow.AddDays(30),
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return ((await response.Content.ReadFromJsonAsync<RfqMirror>())!.Id, marker);
    }

    /// <summary>
    /// One category's row from the dashboard, asserting it exists.
    /// </summary>
    /// <remarks>
    /// Asserts rather than defaulting to zeroes: a row that has silently dropped off the panel is
    /// exactly the failure this helper is here to catch, and returning a zeroed row would hide it.
    /// </remarks>
    private async Task<CommodityMirror> CommodityRowAsync(string categoryName)
    {
        var overview = await ReadOverviewAsync(await AdminTokenAsync());
        return Assert.Single(overview.Commodities, c => c.CategoryName == categoryName);
    }

    private async Task<string> VerifyBusinessAsync(string token)
    {
        var businessId = await SubmitBusinessForVerificationAsync(token);
        (await PostAuthenticatedAsync(
            $"/api/v1/admin/businesses/{businessId}/verification/review", await AdminTokenAsync(),
            new { approve = true })).EnsureSuccessStatusCode();
        return token;
    }

    private async Task<Guid> SeedOrderAsync(
        string buyerToken,
        string sellerToken,
        string port,
        decimal total = 12000m,
        string currency = "USD")
    {
        var rfq = await PostAuthenticatedAsync("/api/v1/rfqs", buyerToken, new
        {
            title = $"admin trade {Guid.NewGuid():N}",
            quantity = 5m,
            unit = (int)PriceUnit.PerTon,
            destinationCountry = "NG",
            destinationPortOrCity = "Lagos",
            preferredIncoterm = (int)Incoterms.Cif,
            preferredCurrency = currency,
            expiresAt = DateTime.UtcNow.AddDays(30),
        });
        rfq.EnsureSuccessStatusCode();
        var rfqDto = await rfq.Content.ReadFromJsonAsync<RfqMirror>();

        var quotation = await PostAuthenticatedAsync($"/api/v1/rfqs/{rfqDto!.Id}/quotations", sellerToken, new
        {
            currency,
            incoterms = (int)Incoterms.Cif,
            portOfLoading = port,
            portOfDischarge = "Lagos (LOS)",
            leadTimeDays = 14,
            paymentTerms = (int)PaymentTerms.TtPartialDeposit,
            total,
        });
        quotation.EnsureSuccessStatusCode();
        var quotationDto = await quotation.Content.ReadFromJsonAsync<QuotationMirror>();

        (await PostAuthenticatedAsync(
            $"/api/v1/quotations/{quotationDto!.Id}/accept", buyerToken, new { })).EnsureSuccessStatusCode();

        var order = await PostAuthenticatedAsync("/api/v1/orders/from-quotation", buyerToken, new
        {
            quotationId = quotationDto.Id,
            placeImmediately = true,
        });
        Assert.Equal(HttpStatusCode.Created, order.StatusCode);
        return (await order.Content.ReadFromJsonAsync<OrderMirror>())!.Id;
    }

    private async Task<ShipmentMirror> BookAsync(Guid orderId, string sellerToken, object body)
    {
        var response = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/shipments", sellerToken, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ShipmentMirror>())!;
    }

    private async Task AppendEventAsync(Guid shipmentId, string sellerToken, ShipmentStatus status)
    {
        (await PostAuthenticatedAsync(
            $"/api/v1/shipments/{shipmentId}/tracking-events", sellerToken,
            new { status = (int)status })).EnsureSuccessStatusCode();
    }

    private Task<HttpResponseMessage> UploadDocumentAsync(
        Guid orderId, string token, DocumentType docType, string reference)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(((int)docType).ToString()), "docType" },
            { new StringContent(reference), "documentReference" },
            { PdfPart(), "file", $"{docType}.pdf" },
        };
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/{orderId}/documents")
        {
            Content = form,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private static ByteArrayContent PdfPart()
    {
        var part = new ByteArrayContent("%PDF-1.4 admin registration"u8.ToArray());
        part.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return part;
    }

    // ---- Reads ----

    private async Task<OverviewMirror> ReadOverviewAsync(string adminToken)
    {
        var response = await GetAuthenticatedAsync("/api/v1/admin/overview", adminToken);
        response.EnsureSuccessStatusCode();
        var overview = await response.Content.ReadFromJsonAsync<OverviewMirror>();
        return overview!;
    }

    private async Task<PagedMirror<AuditMirror>> AuditLogAsync(
        string adminToken, string? action = null, string? q = null, int page = 1, int pageSize = 20)
    {
        var query = new List<string>();
        if (action is not null)
        {
            query.Add($"action={Uri.EscapeDataString(action)}");
        }

        if (q is not null)
        {
            query.Add($"q={Uri.EscapeDataString(q)}");
        }

        query.Add($"page={page}");
        query.Add($"pageSize={pageSize}");

        var response = await GetAuthenticatedAsync(
            "/api/v1/admin/audit-log?" + string.Join("&", query), adminToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PagedMirror<AuditMirror>>())!;
    }

    /// <summary>
    /// Supply and demand figures for three named categories, treating an absent row as zeroes.
    /// A category with no products and no RFQs is legitimately missing from the panel, and that is
    /// different from it being reported as zero.
    /// </summary>
    private async Task<(CommodityMirror Listed, CommodityMirror DraftOnly, CommodityMirror WantedOnly)>
        CommodityRowsAsync(string adminToken, params string[] names)
    {
        var overview = await ReadOverviewAsync(adminToken);
        var rows = overview.Commodities.ToDictionary(c => c.CategoryName);

        static CommodityMirror Row(IReadOnlyDictionary<string, CommodityMirror> rows, string name) =>
            rows.GetValueOrDefault(name) ?? new CommodityMirror { CategoryName = name };

        return (Row(rows, names[0]), Row(rows, names[1]), Row(rows, names[2]));
    }

    private async Task<CorridorMirror> CorridorAsync(string adminToken, string port, string currency)
    {
        var overview = await ReadOverviewAsync(adminToken);
        return Assert.Single(overview.Corridors, c => c.OriginPort == port && c.Currency == currency);
    }

    private async Task<ShipmentRowMirror> ShipmentRowAsync(string adminToken, string reference)
    {
        var response = await GetAuthenticatedAsync(
            $"/api/v1/admin/shipments?q={Uri.EscapeDataString(reference)}", adminToken);
        response.EnsureSuccessStatusCode();
        return Assert.Single((await response.Content.ReadFromJsonAsync<PagedMirror<ShipmentRowMirror>>())!.Items);
    }

    private async Task<string> GetOrderNumberAsync(string token, Guid orderId)
    {
        var response = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}", token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderMirror>())!.OrderNumber;
    }

    // ---- HTTP + identity helpers ----

    private Task<HttpResponseMessage> GetAuthenticatedAsync(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PostAuthenticatedAsync(string url, string token, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PatchAuthenticatedAsync(string url, string token, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, url) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private async Task<string> CreateCompanyUserAsync(string prefix)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = $"{prefix}@admin.tests.agrotrade.local",
            Email = $"{prefix}@admin.tests.agrotrade.local",
            EmailConfirmed = true,
        };
        var created = await users.CreateAsync(user, Password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, RoleNames.Company);
        return await LoginAsync(user.Email!, Password);
    }

    private async Task<string> RegisterAndLoginAsync(string prefix)
    {
        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = $"{prefix}@admin.tests.agrotrade.local",
            password = Password,
            displayName = prefix,
            homeCountry = "NG",
            defaultCurrency = "USD",
            roles = new[] { RoleNames.Importer },
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        return (await register.Content.ReadFromJsonAsync<AuthResponse>())!.AccessToken;
    }

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!.AccessToken;
    }

    private Task<string> AdminTokenAsync() =>
        LoginAsync(TestingWebAppFactory.AdminEmail, TestingWebAppFactory.AdminPassword);

    /// <summary>
    /// The seeded administrator's id, read straight from the database.
    /// </summary>
    /// <remarks>
    /// Read from the DbContext rather than decoded out of a token: the audit row's reviewer is
    /// attributed from <c>User.GetUserId()</c> at request time, so the id is the thing that matters
    /// and this avoids depending on JWT claim shapes.
    /// </remarks>
    private async Task<Guid> AdminUserIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Data.AgroTradeDbContext>();
        var id = await db.Users
            .AsNoTracking()
            .Where(u => u.Email == TestingWebAppFactory.AdminEmail)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync();
        Assert.NotNull(id);
        return id!.Value;
    }

    private async Task<Guid> SupplierBusinessIdAsync(string token)
    {
        var response = await GetAuthenticatedAsync("/api/v1/businesses/mine", token);
        response.EnsureSuccessStatusCode();
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var first = root.ValueKind == System.Text.Json.JsonValueKind.Array ? root[0] : root.GetProperty("items")[0];
        return first.GetProperty("id").GetGuid();
    }

    // ---- DTO mirrors ----

    public sealed class PagedMirror<T>
    {
        public List<T> Items { get; init; } = [];
        public int Page { get; init; }
        public int PageSize { get; init; }
        public int Total { get; init; }
        public int TotalPages { get; init; }
    }

    public sealed class AuditMirror
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public string? ActorDisplayName { get; init; }
        public string Action { get; init; } = "";
        public string EntityType { get; init; } = "";
        public Guid? EntityId { get; init; }
        public string Summary { get; init; } = "";
        public string? Details { get; init; }
        public DateTime OccurredAtUtc { get; init; }
    }

    public sealed class FunnelStageMirror
    {
        public int Status { get; init; }
        public string Label { get; init; } = "";
        public int Count { get; init; }
    }

    public sealed class CorridorMirror
    {
        public string? OriginPort { get; init; }
        public string? DestinationCountry { get; init; }
        public string Currency { get; init; } = "";
        public int OrderCount { get; init; }
        public decimal GrossOrderValue { get; init; }
        public decimal NetSettled { get; init; }
        public decimal Outstanding { get; init; }
        public int MismatchedLedgerEntries { get; init; }
    }

    public sealed class CommodityMirror
    {
        public string CategoryName { get; init; } = "";
        public int ProductCount { get; init; }
        public int PublishedCount { get; init; }
        public int RfqCount { get; init; }
        public int OpenRfqCount { get; init; }
    }

    public sealed class DemandMirror
    {
        public int OpenRfqs { get; init; }
        public int TotalRfqs { get; init; }
        public int UnansweredOpenRfqs { get; init; }
        public decimal? AverageBudgetCeiling { get; init; }
        public string PreferredCurrency { get; init; } = "";
    }

    public sealed class CountsMirror
    {
        public int Users { get; init; }
        public int Businesses { get; init; }
        public int VerifiedBusinesses { get; init; }
        public int Products { get; init; }
        public int PublishedProducts { get; init; }
    public int SuspendedProducts { get; init; }
        public int TradeRequests { get; init; }
        public int Quotations { get; init; }
        public int Orders { get; init; }
        public int Shipments { get; init; }
        public int Transactions { get; init; }
        public int ShipmentsInException { get; init; }
    }

    public sealed class OverviewMirror
    {
        public CountsMirror Counts { get; init; } = new();
        public List<FunnelStageMirror> BusinessVerificationFunnel { get; init; } = [];
        public List<FunnelStageMirror> DocumentVerificationFunnel { get; init; } = [];
        public List<FunnelStageMirror> OrderFunnel { get; init; } = [];
        public List<FunnelStageMirror> PaymentFunnel { get; init; } = [];
        public List<CorridorMirror> Corridors { get; init; } = [];
        public List<CommodityMirror> Commodities { get; init; } = [];
        public DemandMirror Demand { get; init; } = new();
    }

    public sealed class UserRowMirror
    {
        public Guid Id { get; init; }
        public string Email { get; init; } = "";
        public string? DisplayName { get; init; }
        public bool EmailConfirmed { get; init; }
        public bool LockedOut { get; init; }
        public string[] Roles { get; init; } = [];
        public string? BusinessName { get; init; }
        public DateTime? CreatedAtUtc { get; init; }
    }

    public sealed class OrderRowMirror
    {
        public Guid Id { get; init; }
        public string OrderNumber { get; init; } = "";
        public int Status { get; init; }
        public int PaymentStatus { get; init; }
        public string Currency { get; init; } = "";
        public decimal Total { get; init; }
        public decimal AmountPaid { get; init; }
        public string? BuyerDisplayName { get; init; }
        public string? SellerTradingName { get; init; }
        public string? DestinationCountry { get; init; }
        public DateTime? PlacedAtUtc { get; init; }
        public int ShipmentCount { get; init; }
    }

    public sealed class ShipmentRowMirror
    {
        public Guid Id { get; init; }
        public string Reference { get; init; } = "";
        public Guid OrderId { get; init; }
        public string OrderNumber { get; init; } = "";
        public int Status { get; init; }
        public DateTime? StatusSinceUtc { get; init; }
        public int Mode { get; init; }
        public string CarrierName { get; init; } = "";
        public string? DestinationCountry { get; init; }
        public bool HasGuestTracking { get; init; }
        public int MilestoneCount { get; init; }
        public DateTime? EstimatedArrivalUtc { get; init; }
    }

    public sealed class ShipmentMirror
    {
        public Guid Id { get; init; }
        public Guid OrderId { get; init; }
        public string Reference { get; init; } = "";
        public int Status { get; init; }
    }

    public sealed class OrderMirror
    {
        public Guid Id { get; init; }
        public string OrderNumber { get; init; } = "";
    }

    public sealed class RfqMirror
    {
        public Guid Id { get; init; }
    }

    public sealed class ProductMirror
    {
        public Guid Id { get; init; }
    }

    public sealed class AdminProductRowMirror
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = "";
        public string Slug { get; init; } = "";
        public string? CategoryName { get; init; }
        public string? SellerTradingName { get; init; }
        public string OriginCountry { get; init; } = "";
        public string Currency { get; init; } = "";
        public decimal Price { get; init; }
        public int ListingStatus { get; init; }
        public int ModerationStatus { get; init; }
        public string? ModerationReason { get; init; }
        public DateTime? SuspendedAtUtc { get; init; }
        public Guid? SuspendedByUserId { get; init; }
        public bool IsVisibleInMarketplace { get; init; }
    }

    public sealed class AdminRfqRowMirror
    {
        public Guid Id { get; init; }
        public string RfqNumber { get; init; } = "";
        public string Title { get; init; } = "";
        public int Status { get; init; }
        public string PreferredCurrency { get; init; } = "";
        public decimal? BudgetMax { get; init; }
        public string? CategoryName { get; init; }
        public string? DestinationCountry { get; init; }
        public string? BuyerDisplayName { get; init; }
        public int QuotationCount { get; init; }
    }

    public sealed class QuotationMirror
    {
        public Guid Id { get; init; }
    }

    public sealed class DocumentMirror
    {
        public Guid Id { get; init; }
        public int Status { get; init; }
    }

    public sealed class AuthResponse
    {
        public string AccessToken { get; init; } = "";
    }

    public sealed class CategoryMirror
    {
        public Guid Id { get; init; }
        public Guid? ParentId { get; init; }
        public string Name { get; init; } = "";
        public bool IsActive { get; init; }
    }
}