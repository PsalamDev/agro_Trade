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
/// The remaining administrative routes: the ledger, the analytics reports, the settings store and
/// account suspension.
///
/// <para>
/// Four claims carry the weight here. The ledger is read from the <i>ledger</i>, so an administrator
/// reconciles against the same immutable rows the money actually moved through. A report is windowed
/// on both sides of its reconciliation, so a period cannot show orders whose payments sit outside it.
/// A setting is only a setting if something <i>reads</i> it: closing registration really stops
/// registration, and the configured default role is really what an undecided registrant receives. And
/// suspension is a platform decision that takes effect on the next authenticated request, not just
/// the next sign-in.
/// </para>
/// </summary>
public sealed class AdminOperationsFlowTests : IClassFixture<TestingWebAppFactory>
{
    private const string Password = "StrongPass123";
    private const string RegistrationOpenKey = "registration.open";
    private const string DefaultRoleKey = "registration.default_role";

    private readonly TestingWebAppFactory _factory;
    private readonly HttpClient _client;

    public AdminOperationsFlowTests(TestingWebAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureSchema();
        _client = factory.CreateClient();
    }

    // ---- Access ----

    [Fact]
    public async Task AdminOperations_RequireAuthentication()
    {
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await _client.GetAsync("/api/v1/admin/transactions")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await _client.GetAsync("/api/v1/admin/settings")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await _client.GetAsync("/api/v1/admin/reports/corridors")).StatusCode);
    }

    [Fact]
    public async Task AdminOperations_RejectOrdinaryUsers()
    {
        var buyerToken = await RegisterAndLoginAsync($"ops_forbid_{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/api/v1/admin/transactions", buyerToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/api/v1/admin/settings", buyerToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/api/v1/admin/reports/rfq", buyerToken)).StatusCode);
    }

    // ---- Ledger ----

    [Fact]
    public async Task Ledger_ListsASettlement_AndSearchesByReference()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("ledger");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var orderNumber = await GetOrderNumberAsync(buyerToken, orderId);
        var remittance = $"UTR-{Guid.NewGuid():N}";

        (await PostAsync($"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 4000m, remittanceReference = remittance })).EnsureSuccessStatusCode();

        var byReference = await TransactionsAsync(await AdminTokenAsync(), q: remittance);
        var row = Assert.Single(byReference.Items);

        Assert.Equal((int)TransactionType.Payment, row.Type);
        Assert.Equal(4000m, row.Amount);
        Assert.Equal("USD", row.Currency);
        Assert.Equal(orderId, row.OrderId);
        Assert.Equal(orderNumber, row.OrderNumber);
        Assert.Equal(remittance, row.ExternalReference);

        // The same entry is reachable by the order number an operator reads off a ticket.
        var byOrderNumber = await TransactionsAsync(await AdminTokenAsync(), q: orderNumber);
        Assert.Contains(byOrderNumber.Items, t => t.Id == row.Id);

        // And by the order id, without needing to know the reference at all.
        var byOrder = await TransactionsAsync(await AdminTokenAsync(), orderId: orderId);
        Assert.Contains(byOrder.Items, t => t.Id == row.Id);
    }

    [Fact]
    public async Task Ledger_FiltersByType()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("ledger-type");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var remittance = $"UTR-{Guid.NewGuid():N}";

        (await PostAsync($"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 4000m, remittanceReference = remittance })).EnsureSuccessStatusCode();

        var adminToken = await AdminTokenAsync();

        var payments = await TransactionsAsync(adminToken, q: remittance, type: (int)TransactionType.Payment);
        Assert.Contains(payments.Items, t => t.ExternalReference == remittance);

        var refunds = await TransactionsAsync(adminToken, q: remittance, type: (int)TransactionType.Refund);
        Assert.DoesNotContain(refunds.Items, t => t.ExternalReference == remittance);
    }

    // ---- Exports ----

    [Fact]
    public async Task Exports_RequireAdministrator()
    {
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await _client.GetAsync("/api/v1/admin/audit-log/export")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await _client.GetAsync("/api/v1/admin/transactions/export")).StatusCode);

        var buyerToken = await RegisterAndLoginAsync($"ops_export_forbid_{Guid.NewGuid():N}");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await GetAsync("/api/v1/admin/audit-log/export", buyerToken)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await GetAsync("/api/v1/admin/transactions/export", buyerToken)).StatusCode);
    }

    [Fact]
    public async Task AuditExport_IsACsvFile_ForTheFilteredEntry()
    {
        var token = await VerifyBusinessAsync(
            await RegisterAndLoginAsync($"ops_export_audit_{Guid.NewGuid():N}"));
        var businessId = await SupplierBusinessIdAsync(token);

        var response = await GetAsync(
            $"/api/v1/admin/audit-log/export?q={businessId}", await AdminTokenAsync());

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.EndsWith(".csv", response.Content.Headers.ContentDisposition?.FileName);

        var lines = CsvLines(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "OccurredAtUtc,Action,EntityType,EntityId,Actor,ActorUserId,Summary,Details",
            lines[0]);

        // The review of this one business is the only entry naming its id, so exactly one row comes
        // back - and the id sits in the column the heading promises, not merely somewhere in the file.
        var row = Assert.Single(lines.Skip(1));
        Assert.Equal(businessId.ToString(), row.Split(',')[3]);
        Assert.Contains("business.verification.approved", row);
    }

    [Fact]
    public async Task LedgerExport_IsACsvFile_ForTheSettlement()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("ledger-export");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var orderNumber = await GetOrderNumberAsync(buyerToken, orderId);
        var remittance = $"UTR-{Guid.NewGuid():N}";

        (await PostAsync($"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 4000m, remittanceReference = remittance })).EnsureSuccessStatusCode();

        var response = await GetAsync(
            $"/api/v1/admin/transactions/export?q={Uri.EscapeDataString(remittance)}",
            await AdminTokenAsync());

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);

        var lines = CsvLines(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "OccurredAtUtc,Reference,Type,Amount,Currency,OrderNumber,PaymentId,ExternalReference,Narrative",
            lines[0]);

        var row = Assert.Single(lines.Skip(1));
        // Type is the word an operator reads, and the amount is the signed figure with no thousands
        // separator, so the column can be summed straight from the file.
        Assert.Contains("Payment", row);
        Assert.Contains("4000.00", row);
        Assert.Contains("USD", row);
        Assert.Contains(orderNumber, row);
        Assert.Contains(remittance, row);
    }

    [Fact]
    public async Task Exports_AreValidFiles_WhenNothingMatches()
    {
        var adminToken = await AdminTokenAsync();
        var missing = $"no-such-{Guid.NewGuid():N}";

        var audit = await GetAsync(
            $"/api/v1/admin/audit-log/export?q={Uri.EscapeDataString(missing)}", adminToken);
        audit.EnsureSuccessStatusCode();
        Assert.Equal(
            "OccurredAtUtc,Action,EntityType,EntityId,Actor,ActorUserId,Summary,Details",
            CsvLines(await audit.Content.ReadAsStringAsync()).Single());

        var ledger = await GetAsync(
            $"/api/v1/admin/transactions/export?q={Uri.EscapeDataString(missing)}", adminToken);
        ledger.EnsureSuccessStatusCode();
        Assert.Equal(
            "OccurredAtUtc,Reference,Type,Amount,Currency,OrderNumber,PaymentId,ExternalReference,Narrative",
            CsvLines(await ledger.Content.ReadAsStringAsync()).Single());
    }

    // ---- Reports ----

    [Fact]
    public async Task Reports_UnknownKind_IsBadRequest()
    {
        var response = await GetAsync("/api/v1/admin/reports/nonsense", await AdminTokenAsync());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reports_InvertedWindow_IsBadRequest()
    {
        var adminToken = await AdminTokenAsync();
        var response = await GetAsync(
            $"/api/v1/admin/reports/corridors?from={Uri.EscapeDataString(DateTime.UtcNow.ToString("O"))}" +
            $"&to={Uri.EscapeDataString(DateTime.UtcNow.AddDays(-1).ToString("O"))}",
            adminToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CorridorReport_CountsTradeInTheWindow()
    {
        var port = UniquePort("rep");
        var (buyerToken, sellerToken) = await SeedTradeAsync("report");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken, port, total: 12000m);

        (await PostAsync($"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 12000m, remittanceReference = $"UTR-{Guid.NewGuid():N}" })).EnsureSuccessStatusCode();

        var adminToken = await AdminTokenAsync();
        var now = DateTime.UtcNow;
        var report = await ReportAsync(adminToken, "corridors", now.AddDays(-1), now.AddDays(1));

        Assert.Equal("corridors", report.Kind);
        Assert.Equal(now.AddDays(-1).Date, report.FromUtc!.Value.Date);

        var corridor = Assert.Single(report.Corridors, c => c.OriginPort == port && c.Currency == "USD");
        Assert.Equal(12000m, corridor.GrossOrderValue);
        Assert.Equal(12000m, corridor.NetSettled);

        // A window from before the order existed must not include it: the report is a window, not a
        // filter on some other column.
        var past = await ReportAsync(adminToken, "corridors", new DateTime(2000, 1, 1), new DateTime(2001, 1, 1));
        Assert.DoesNotContain(past.Corridors, c => c.OriginPort == port);
    }

    [Fact]
    public async Task VerificationFunnelReport_IsWindowedOnRegistration()
    {
        var sellerToken = await CreateCompanyUserAsync($"ops_funnel_{Guid.NewGuid():N}");
        await SubmitBusinessForVerificationAsync(sellerToken);

        var adminToken = await AdminTokenAsync();
        var now = DateTime.UtcNow;

        var current = await ReportAsync(adminToken, "verification-funnel", now.AddDays(-1), now.AddDays(1));
        Assert.Equal("verification-funnel", current.Kind);
        Assert.NotEmpty(current.Stages);
        // Every status is present, including the empty ones; a funnel that hides its zeros cannot show
        // a drop.
        Assert.True(current.Stages.Count >= 4, "Expected the funnel to carry its empty stages.");
        Assert.Contains(current.Stages, s => s.Count > 0);

        var past = await ReportAsync(adminToken, "verification-funnel", new DateTime(2000, 1, 1), new DateTime(2001, 1, 1));
        Assert.All(past.Stages, s => Assert.Equal(0, s.Count));
    }

    [Fact]
    public async Task RfqReport_SummarisesDemand()
    {
        var category = await PickCategoryAsync();
        var buyerToken = await RegisterAndLoginAsync($"ops_rfq_{Guid.NewGuid():N}");
        var (rfqId, _) = await CreateRfqAsync(buyerToken, category);
        Assert.NotEqual(Guid.Empty, rfqId);

        var adminToken = await AdminTokenAsync();
        var now = DateTime.UtcNow;
        var report = await ReportAsync(adminToken, "rfq", now.AddDays(-1), now.AddDays(1));

        Assert.Equal("rfq", report.Kind);
        Assert.NotNull(report.Rfq);
        Assert.True(report.Rfq!.Demand.TotalRfqs >= 1);
        Assert.NotEmpty(report.Rfq.ByStatus);
    }

    [Fact]
    public async Task CommoditiesReport_IsASeparatelyShapedPayload()
    {
        var category = await PickCategoryAsync();
        var buyerToken = await RegisterAndLoginAsync($"ops_commod_{Guid.NewGuid():N}");
        await CreateRfqAsync(buyerToken, category);

        var adminToken = await AdminTokenAsync();
        var now = DateTime.UtcNow;
        var report = await ReportAsync(adminToken, "commodities", now.AddDays(-1), now.AddDays(1));

        Assert.Equal("commodities", report.Kind);
        // The other report payloads stay empty, so a client switches on Kind and reads one shape.
        Assert.Empty(report.Corridors);
        Assert.Null(report.Rfq);
        var row = Assert.Single(report.Commodities, c => c.CategoryName == category.Name);
        Assert.True(row.RfqCount >= 1);
    }

    // ---- Settings ----

    [Fact]
    public async Task Settings_ReturnCatalogueValues()
    {
        var settings = await ReadSettingsAsync(await AdminTokenAsync());

        var open = Assert.Single(settings, s => s.Key == RegistrationOpenKey);
        Assert.Equal("true", open.Value);
        Assert.Equal("boolean", open.Kind);
        Assert.False(string.IsNullOrWhiteSpace(open.Label));

        var role = Assert.Single(settings, s => s.Key == DefaultRoleKey);
        Assert.Equal("Buyer", role.Value);
        Assert.Equal("role", role.Kind);
    }

    [Fact]
    public async Task Settings_UpdatePersistsAndIsAudited()
    {
        var adminToken = await AdminTokenAsync();
        var adminId = await AdminUserIdAsync();

        try
        {
            var updated = await PutSettingsAsync(adminToken,
                new { key = DefaultRoleKey, value = "buyer" });

            var changed = Assert.Single(updated, s => s.Key == DefaultRoleKey);
            // Stored in the platform's own casing, not the caller's.
            Assert.Equal("Buyer", changed.Value);
            Assert.True(changed.IsOverridden);
            Assert.Equal(adminId, changed.UpdatedByUserId);

            // And it survives a re-read, rather than only being echoed by the write.
            var reread = await ReadSettingsAsync(adminToken);
            Assert.Equal("Buyer", Assert.Single(reread, s => s.Key == DefaultRoleKey).Value);

            var audit = await AuditAsync(adminToken, "settings.updated");
            Assert.Contains(audit.Items, a => a.Details is not null && a.Details.Contains(DefaultRoleKey));
        }
        finally
        {
            await PutSettingsAsync(adminToken, new { key = DefaultRoleKey, value = "Buyer" });
        }
    }

    [Fact]
    public async Task Settings_UnknownKey_IsBadRequest()
    {
        var response = await PutSettingsRawAsync(await AdminTokenAsync(),
            new { key = "not.a.setting", value = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Settings_InvalidBoolean_IsBadRequest()
    {
        var response = await PutSettingsRawAsync(await AdminTokenAsync(),
            new { key = RegistrationOpenKey, value = "maybe" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Settings_EmptyBatch_IsBadRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/settings")
        {
            Content = JsonContent.Create(new { settings = Array.Empty<object>() }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AdminTokenAsync());
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ClosingRegistration_StopsSignups_AndReopeningRestoresThem()
    {
        var adminToken = await AdminTokenAsync();
        try
        {
            var closed = await PutSettingsAsync(adminToken, new { key = RegistrationOpenKey, value = "false" });
            Assert.Contains(closed, s => s.Key == RegistrationOpenKey && s.Value == "false");

            var blocked = await RegisterRawAsync($"ops_closed_{Guid.NewGuid():N}@admin.tests.agrotrade.local", roles: []);
            Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);

            // Reopen inside the try so the assertion that follows reflects the restored state.
            var reopened = await PutSettingsAsync(adminToken, new { key = RegistrationOpenKey, value = "true" });
            Assert.Contains(reopened, s => s.Key == RegistrationOpenKey && s.Value == "true");

            var allowed = await RegisterRawAsync($"ops_open_{Guid.NewGuid():N}@admin.tests.agrotrade.local", roles: []);
            Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        }
        finally
        {
            await PutSettingsAsync(adminToken, new { key = RegistrationOpenKey, value = "true" });
        }
    }

    [Fact]
    public async Task DefaultRegistrationRole_IsWhatAnUndecidedRegistrantGets()
    {
        var adminToken = await AdminTokenAsync();
        try
        {
            await PutSettingsAsync(adminToken, new { key = DefaultRoleKey, value = "buyer" });

            var register = await RegisterRawAsync($"ops_default_{Guid.NewGuid():N}@admin.tests.agrotrade.local", roles: []);
            Assert.Equal(HttpStatusCode.Created, register.StatusCode);
            var body = await register.Content.ReadFromJsonAsync<AuthEnvelope>();
            Assert.Equal(new[] { "Buyer" }, body!.User.Roles);
        }
        finally
        {
            await PutSettingsAsync(adminToken, new { key = DefaultRoleKey, value = "Buyer" });
        }
    }

    // ---- Account status ----

    [Fact]
    public async Task Suspension_BlocksLoginAndInvalidatesExistingSessions()
    {
        var email = $"ops_suspend_{Guid.NewGuid():N}@admin.tests.agrotrade.local";
        var register = await RegisterRawAsync(email, roles: [RoleNames.Buyer]);
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var registered = await register.Content.ReadFromJsonAsync<AuthEnvelope>();
        var userId = registered!.User.Id;

        var adminToken = await AdminTokenAsync();

        var suspend = await PatchAsync($"/api/v1/admin/users/{userId}/status", adminToken,
            new { action = (int)UserStatusAction.Suspend, reason = "Repeated policy breaches." });
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);
        var suspended = await suspend.Content.ReadFromJsonAsync<UserRowMirror>();
        Assert.False(suspended!.IsActive);

        // The token that was valid before the suspension no longer opens a request: the per-request
        // account-state check fails it, so the session is closed rather than merely unable to renew.
        // Either 401 or 403 closes the session — the exact one is the auth pipeline's choice between
        // challenging and forbidding a principal whose credentials are intact but no longer welcome —
        // and what matters here is that it is not 200.
        var closed = (await GetAsync("/api/v1/auth/me", registered.AccessToken)).StatusCode;
        Assert.True(
            closed is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Expected the suspended session to be refused, got {(int)closed}.");

        // And a fresh login is refused, signalled as 403 with ACCOUNT_SUSPENDED rather than the 401
        // of bad credentials: the credentials were right, the account is not welcome.
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);

        // Reactivation restores both.
        var activate = await PatchAsync($"/api/v1/admin/users/{userId}/status", adminToken,
            new { action = (int)UserStatusAction.Activate });
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        Assert.True((await activate.Content.ReadFromJsonAsync<UserRowMirror>())!.IsActive);

        var relogin = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, relogin.StatusCode);
    }

    [Fact]
    public async Task Suspension_IsVisibleInTheUserQueue()
    {
        var email = $"ops_queue_{Guid.NewGuid():N}@admin.tests.agrotrade.local";
        var register = await RegisterRawAsync(email, roles: [RoleNames.Buyer]);
        var registered = await register.Content.ReadFromJsonAsync<AuthEnvelope>();
        var adminToken = await AdminTokenAsync();

        await PatchAsync($"/api/v1/admin/users/{registered!.User.Id}/status", adminToken,
            new { action = (int)UserStatusAction.Suspend, reason = "Test suspension." });

        var response = await GetAsync($"/api/v1/admin/users?q={Uri.EscapeDataString(email)}", adminToken);
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<PagedMirror<UserRowMirror>>();
        var row = Assert.Single(page!.Items, u => u.Email == email);
        Assert.False(row.IsActive);
    }

    [Fact]
    public async Task BuyerId_IsAssignedOnRegistration_AndFindableByNumberOrName()
    {
        var email = $"ops_buyerid_{Guid.NewGuid():N}@admin.tests.agrotrade.local";
        var displayName = $"Searchable Buyer {Guid.NewGuid():N}";
        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = Password,
            displayName,
            homeCountry = "NG",
            defaultCurrency = "USD",
            roles = new[] { RoleNames.Buyer },
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var buyerId = (await register.Content.ReadFromJsonAsync<AuthEnvelope>())!.User.BuyerId;
        Assert.NotNull(buyerId);
        Assert.Matches(@"^BUY-ID-AG-\d{6}$", buyerId!);

        var adminToken = await AdminTokenAsync();

        // Found by account number...
        var byNumber = await GetAsync($"/api/v1/admin/users?q={Uri.EscapeDataString(buyerId!)}", adminToken);
        byNumber.EnsureSuccessStatusCode();
        var numberPage = await byNumber.Content.ReadFromJsonAsync<PagedMirror<UserRowMirror>>();
        var byNumberRow = Assert.Single(numberPage!.Items, u => u.Email == email);
        Assert.Equal(buyerId, byNumberRow.BuyerId);

        // ...and by name, independently.
        var byName = await GetAsync($"/api/v1/admin/users?q={Uri.EscapeDataString(displayName)}", adminToken);
        byName.EnsureSuccessStatusCode();
        var namePage = await byName.Content.ReadFromJsonAsync<PagedMirror<UserRowMirror>>();
        Assert.Single(namePage!.Items, u => u.Email == email);
    }

    [Fact]
    public async Task BuyerIdFilter_MatchesAccountsWithANumberAndExcludesTheRest()
    {
        var email = $"ops_buyerfilter_{Guid.NewGuid():N}@admin.tests.agrotrade.local";
        var register = await RegisterRawAsync(email, roles: [RoleNames.Buyer]);
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var buyerId = (await register.Content.ReadFromJsonAsync<AuthEnvelope>())!.User.BuyerId;
        Assert.NotNull(buyerId);

        var adminToken = await AdminTokenAsync();

        // With the filter on, the buyer is found and every row carries a number.
        var withNumber = await GetAsync(
            $"/api/v1/admin/users?hasBuyerId=true&q={Uri.EscapeDataString(email)}", adminToken);
        withNumber.EnsureSuccessStatusCode();
        var withPage = await withNumber.Content.ReadFromJsonAsync<PagedMirror<UserRowMirror>>();
        var row = Assert.Single(withPage!.Items, u => u.Email == email);
        Assert.Equal(buyerId, row.BuyerId);
        Assert.All(withPage.Items, u => Assert.NotNull(u.BuyerId));

        // With it off, the same buyer is not a match: the two sides are complements.
        var withoutNumber = await GetAsync(
            $"/api/v1/admin/users?hasBuyerId=false&q={Uri.EscapeDataString(email)}", adminToken);
        withoutNumber.EnsureSuccessStatusCode();
        var withoutPage = await withoutNumber.Content.ReadFromJsonAsync<PagedMirror<UserRowMirror>>();
        Assert.Equal(0, withoutPage!.Total);
    }

    [Fact]
    public async Task Suspension_RequiresAReason()
    {
        var email = $"ops_noreason_{Guid.NewGuid():N}@admin.tests.agrotrade.local";
        var register = await RegisterRawAsync(email, roles: [RoleNames.Buyer]);
        var registered = await register.Content.ReadFromJsonAsync<AuthEnvelope>();

        var response = await PatchAsync($"/api/v1/admin/users/{registered!.User.Id}/status", await AdminTokenAsync(),
            new { action = (int)UserStatusAction.Suspend });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Suspension_CannotTargetYourself()
    {
        var response = await PatchAsync($"/api/v1/admin/users/{await AdminUserIdAsync()}/status", await AdminTokenAsync(),
            new { action = (int)UserStatusAction.Suspend, reason = "Locking myself out." });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Suspension_RepeatedOrOnAnActiveAccount_IsConflict()
    {
        var email = $"ops_repeat_{Guid.NewGuid():N}@admin.tests.agrotrade.local";
        var register = await RegisterRawAsync(email, roles: [RoleNames.Buyer]);
        var registered = await register.Content.ReadFromJsonAsync<AuthEnvelope>();
        var adminToken = await AdminTokenAsync();
        var url = $"/api/v1/admin/users/{registered!.User.Id}/status";

        (await PatchAsync(url, adminToken, new { action = (int)UserStatusAction.Suspend, reason = "First." }))
            .EnsureSuccessStatusCode();

        Assert.Equal(
            HttpStatusCode.Conflict,
            (await PatchAsync(url, adminToken, new { action = (int)UserStatusAction.Suspend, reason = "Again." })).StatusCode);

        (await PatchAsync(url, adminToken, new { action = (int)UserStatusAction.Activate })).EnsureSuccessStatusCode();

        Assert.Equal(
            HttpStatusCode.Conflict,
            (await PatchAsync(url, adminToken, new { action = (int)UserStatusAction.Activate })).StatusCode);
    }

    [Fact]
    public async Task Suspension_IsAuditedAgainstTheAccount()
    {
        var email = $"ops_audit_{Guid.NewGuid():N}@admin.tests.agrotrade.local";
        var register = await RegisterRawAsync(email, roles: [RoleNames.Buyer]);
        var registered = await register.Content.ReadFromJsonAsync<AuthEnvelope>();
        var adminToken = await AdminTokenAsync();

        await PatchAsync($"/api/v1/admin/users/{registered!.User.Id}/status", adminToken,
            new { action = (int)UserStatusAction.Suspend, reason = "Documented reason." });

        var audit = await AuditAsync(adminToken, "user.suspended");
        var entry = Assert.Single(audit.Items, a => a.EntityId == registered.User.Id);
        Assert.Equal("Documented reason.", entry.Details);
    }

    // ---- Product publishing ----

    [Fact]
    public async Task CreateProduct_PublishesForANamedBusiness_AndIsAudited()
    {
        var sellerToken = await VerifyBusinessAsync(
            await CreateCompanyUserAsync($"ops_adminprod_seller_{Guid.NewGuid():N}"));
        var businessId = await SupplierBusinessIdAsync(sellerToken);
        var category = await PickCategoryAsync();
        var adminToken = await AdminTokenAsync();
        var name = Cap($"Admin listing {Guid.NewGuid():N}");

        var response = await PostAsync(
            $"/api/v1/admin/products?businessId={businessId}", adminToken, new
            {
                name,
                categoryId = category.Id,
                originCountry = "NG",
                price = 3100m,
                currency = "USD",
                availableQuantity = 25m,
                minimumOrderQuantity = 5m,
                listingStatus = (int)ListingStatus.Active,
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ProductMirror>();
        Assert.NotNull(created);
        Assert.Equal(name, created!.Name);
        Assert.Equal(businessId, created.SellerBusinessId);
        Assert.Equal((int)ListingStatus.Active, created.ListingStatus);

        // It lands in the moderation queue the admin works from...
        var queue = await GetAsync($"/api/v1/admin/products?q={Uri.EscapeDataString(name)}", adminToken);
        queue.EnsureSuccessStatusCode();
        var queuePage = await queue.Content.ReadFromJsonAsync<PagedMirror<ProductMirror>>();
        Assert.Contains(queuePage!.Items, p => p.Id == created.Id);

        // ...and the publish is recorded against the administrator.
        var audit = await AuditAsync(adminToken, "product.created");
        var entry = Assert.Single(audit.Items, a => a.EntityId == created.Id);
        Assert.Equal("Product", entry.EntityType);
    }

    [Fact]
    public async Task CreateProduct_AppliesTheVerifiedRuleForActiveListings()
    {
        // A real business that is still awaiting review: valid to publish a draft for, not to go live.
        var sellerToken = await CreateCompanyUserAsync($"ops_adminprod_unverified_{Guid.NewGuid():N}");
        var businessId = await SubmitBusinessForVerificationAsync(sellerToken);
        var category = await PickCategoryAsync();
        var adminToken = await AdminTokenAsync();

        var draft = await PostAsync(
            $"/api/v1/admin/products?businessId={businessId}", adminToken, new
            {
                name = Cap($"Draft listing {Guid.NewGuid():N}"),
                categoryId = category.Id,
                originCountry = "NG",
                price = 1m,
                currency = "USD",
                availableQuantity = 1m,
                minimumOrderQuantity = 1m,
                listingStatus = (int)ListingStatus.Draft,
            });
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);

        var active = await PostAsync(
            $"/api/v1/admin/products?businessId={businessId}", adminToken, new
            {
                name = Cap($"Active listing {Guid.NewGuid():N}"),
                categoryId = category.Id,
                originCountry = "NG",
                price = 1m,
                currency = "USD",
                availableQuantity = 1m,
                minimumOrderQuantity = 1m,
                listingStatus = (int)ListingStatus.Active,
            });
        Assert.Equal(HttpStatusCode.Forbidden, active.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_RejectsNonAdministrators()
    {
        var buyerToken = await RegisterAndLoginAsync($"ops_adminprod_forbid_{Guid.NewGuid():N}");
        var category = await PickCategoryAsync();

        // The policy refuses before the body or business is considered.
        var response = await PostAsync(
            $"/api/v1/admin/products?businessId={Guid.NewGuid()}", buyerToken, new
            {
                name = "should never exist",
                categoryId = category.Id,
                originCountry = "NG",
                price = 1m,
                currency = "USD",
                availableQuantity = 1m,
                minimumOrderQuantity = 1m,
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_WithoutABusiness_IsBadRequest()
    {
        var adminToken = await AdminTokenAsync();
        var category = await PickCategoryAsync();

        var response = await PostAsync("/api/v1/admin/products", adminToken, new
        {
            name = "no target business",
            categoryId = category.Id,
            originCountry = "NG",
            price = 1m,
            currency = "USD",
            availableQuantity = 1m,
            minimumOrderQuantity = 1m,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Reads ----

    private async Task<PagedMirror<TransactionMirror>> TransactionsAsync(
        string adminToken, string? q = null, int? type = null, Guid? orderId = null)
    {
        var query = new List<string>();
        if (q is not null)
        {
            query.Add($"q={Uri.EscapeDataString(q)}");
        }

        if (type.HasValue)
        {
            query.Add($"type={type.Value}");
        }

        if (orderId.HasValue)
        {
            query.Add($"orderId={orderId.Value}");
        }

        query.Add("pageSize=50");

        var response = await GetAsync("/api/v1/admin/transactions?" + string.Join("&", query), adminToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PagedMirror<TransactionMirror>>())!;
    }

    private async Task<ReportMirror> ReportAsync(
        string adminToken, string kind, DateTime? from = null, DateTime? to = null)
    {
        var query = new List<string>();
        if (from.HasValue)
        {
            query.Add($"from={Uri.EscapeDataString(from.Value.ToString("O"))}");
        }

        if (to.HasValue)
        {
            query.Add($"to={Uri.EscapeDataString(to.Value.ToString("O"))}");
        }

        var url = $"/api/v1/admin/reports/{kind}" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
        var response = await GetAsync(url, adminToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReportMirror>())!;
    }

    private async Task<List<SettingMirror>> ReadSettingsAsync(string adminToken)
    {
        var response = await GetAsync("/api/v1/admin/settings", adminToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<SettingMirror>>())!;
    }

    private async Task<List<SettingMirror>> PutSettingsAsync(string adminToken, object change)
    {
        var response = await PutSettingsRawAsync(adminToken, change);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<SettingMirror>>())!;
    }

    private Task<HttpResponseMessage> PutSettingsRawAsync(string adminToken, object change)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/settings")
        {
            Content = JsonContent.Create(new { settings = new[] { change } }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        return _client.SendAsync(request);
    }

    private async Task<PagedMirror<AuditMirror>> AuditAsync(string adminToken, string action)
    {
        var response = await GetAsync(
            $"/api/v1/admin/audit-log?action={Uri.EscapeDataString(action)}&pageSize=50", adminToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PagedMirror<AuditMirror>>())!;
    }

    // ---- Seeding ----

    private static string UniquePort(string tag) => Cap($"Port-{tag}-{Guid.NewGuid():N}", 24);

    private static string Cap(string value, int max = 40) =>
        value.Length <= max ? value : value[..max];

    private async Task<(string BuyerToken, string SellerToken)> SeedTradeAsync(string tag)
    {
        var sellerToken = await VerifyBusinessAsync(
            await CreateCompanyUserAsync($"ops_seller_{tag}_{Guid.NewGuid():N}"));
        var buyerToken = await RegisterAndLoginAsync($"ops_buyer_{tag}_{Guid.NewGuid():N}");
        return (buyerToken, sellerToken);
    }

    private async Task<string> VerifyBusinessAsync(string token)
    {
        var businessId = await SubmitBusinessForVerificationAsync(token);
        (await PostAsync($"/api/v1/admin/businesses/{businessId}/verification/review", await AdminTokenAsync(),
            new { approve = true })).EnsureSuccessStatusCode();
        return token;
    }

    private async Task<Guid> SubmitBusinessForVerificationAsync(string token)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var tradingName = Cap($"Ops Supplier {tag}");

        var registered = await PostAsync("/api/v1/businesses", token, new
        {
            legalName = $"{tradingName} Ltd",
            tradingName,
            country = "NG",
            businessType = (int)BusinessType.LimitedCompany,
        });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        var businessId = await SupplierBusinessIdAsync(token);

        var upload = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/businesses/{businessId}/documents")
        {
            Content = new MultipartFormDataContent
            {
                { new StringContent(((int)DocumentType.BusinessRegistration).ToString()), "docType" },
                { PdfPart(), "file", "certificate.pdf" },
            },
        };
        upload.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await _client.SendAsync(upload)).EnsureSuccessStatusCode();

        var submit = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/businesses/{businessId}/verification-submissions");
        submit.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await _client.SendAsync(submit)).EnsureSuccessStatusCode();

        return businessId;
    }

    private async Task<Guid> SeedOrderAsync(
        string buyerToken, string sellerToken, string? port = null, decimal total = 12000m, string currency = "USD")
    {
        port ??= UniquePort("ord");

        var rfq = await PostAsync("/api/v1/rfqs", buyerToken, new
        {
            title = $"ops trade {Guid.NewGuid():N}",
            quantity = 5m,
            unit = (int)PriceUnit.PerTon,
            destinationCountry = "NG",
            destinationPortOrCity = "Lagos",
            preferredIncoterm = (int)Incoterms.Cif,
            preferredCurrency = currency,
            expiresAt = DateTime.UtcNow.AddDays(30),
        });
        rfq.EnsureSuccessStatusCode();
        var rfqDto = await rfq.Content.ReadFromJsonAsync<IdMirror>();

        var quotation = await PostAsync($"/api/v1/rfqs/{rfqDto!.Id}/quotations", sellerToken, new
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
        var quotationDto = await quotation.Content.ReadFromJsonAsync<IdMirror>();

        (await PostAsync($"/api/v1/quotations/{quotationDto!.Id}/accept", buyerToken, new { }))
            .EnsureSuccessStatusCode();

        var order = await PostAsync("/api/v1/orders/from-quotation", buyerToken, new
        {
            quotationId = quotationDto.Id,
            placeImmediately = true,
        });
        Assert.Equal(HttpStatusCode.Created, order.StatusCode);
        return (await order.Content.ReadFromJsonAsync<IdMirror>())!.Id;
    }

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

        return await db.Categories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => new CategoryMirror { Id = c.Id, Name = c.Name })
            .ToListAsync();
    }

    private async Task<CategoryMirror> PickCategoryAsync()
    {
        var categories = await CategoriesAsync();
        Assert.NotEmpty(categories);
        return categories[0];
    }

    private async Task<(Guid Id, string Marker)> CreateRfqAsync(string token, CategoryMirror category)
    {
        var marker = Guid.NewGuid().ToString("N");
        var response = await PostAsync("/api/v1/rfqs", token, new
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
        return ((await response.Content.ReadFromJsonAsync<IdMirror>())!.Id, marker);
    }

    private static ByteArrayContent PdfPart()
    {
        var part = new ByteArrayContent("%PDF-1.4 admin operations"u8.ToArray());
        part.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return part;
    }

    // ---- HTTP + identity helpers ----

    private Task<HttpResponseMessage> GetAsync(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PostAsync(string url, string token, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PatchAsync(string url, string token, object body)
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
        var response = await RegisterRawAsync(
            $"{prefix}@admin.tests.agrotrade.local", roles: [RoleNames.Buyer]);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthEnvelope>())!.AccessToken;
    }

    private Task<HttpResponseMessage> RegisterRawAsync(string email, IReadOnlyList<string> roles) =>
        _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = Password,
            displayName = email,
            homeCountry = "NG",
            defaultCurrency = "USD",
            roles,
        });

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthEnvelope>())!.AccessToken;
    }

    private Task<string> AdminTokenAsync() =>
        LoginAsync(TestingWebAppFactory.AdminEmail, TestingWebAppFactory.AdminPassword);

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
        var response = await GetAsync("/api/v1/businesses/mine", token);
        response.EnsureSuccessStatusCode();
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var first = root.ValueKind == System.Text.Json.JsonValueKind.Array
            ? root[0]
            : root.GetProperty("items")[0];
        return first.GetProperty("id").GetGuid();
    }

    private async Task<string> GetOrderNumberAsync(string token, Guid orderId)
    {
        var response = await GetAsync($"/api/v1/orders/{orderId}", token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderMirror>())!.OrderNumber;
    }

    /// <summary>
    /// Splits an export body into lines on the CRLF the writer emits, dropping the trailing newline
    /// and the UTF-8 BOM the file deliberately starts with for Excel.
    /// </summary>
    private static string[] CsvLines(string body)
    {
        var trimmed = body.TrimStart('\uFEFF').TrimEnd('\r', '\n');
        return trimmed.Length == 0 ? [] : trimmed.Split("\r\n");
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

    public sealed class TransactionMirror
    {
        public Guid Id { get; init; }
        public string Reference { get; init; } = "";
        public int Type { get; init; }
        public decimal Amount { get; init; }
        public string Currency { get; init; } = "";
        public Guid OrderId { get; init; }
        public string OrderNumber { get; init; } = "";
        public Guid? PaymentId { get; init; }
        public DateTime OccurredAtUtc { get; init; }
        public string? Narrative { get; init; }
        public string? ExternalReference { get; init; }
        public DateTime? CreatedAtUtc { get; init; }
    }

    public sealed class ReportMirror
    {
        public string Kind { get; init; } = "";
        public DateTime? FromUtc { get; init; }
        public DateTime? ToUtc { get; init; }
        public List<CorridorMirror> Corridors { get; init; } = [];
        public List<CommodityMirror> Commodities { get; init; } = [];
        public List<FunnelStageMirror> Stages { get; init; } = [];
        public RfqReportMirror? Rfq { get; init; }
    }

    public sealed class RfqReportMirror
    {
        public DemandMirror Demand { get; init; } = new();
        public List<FunnelStageMirror> ByStatus { get; init; } = [];
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

    public sealed class SettingMirror
    {
        public string Key { get; init; } = "";
        public string Value { get; init; } = "";
        public string DefaultValue { get; init; } = "";
        public string Kind { get; init; } = "";
        public bool IsOverridden { get; init; }
        public string Label { get; init; } = "";
        public string Description { get; init; } = "";
        public Guid? UpdatedByUserId { get; init; }
        public DateTime? UpdatedAtUtc { get; init; }
    }

    public sealed class UserRowMirror
    {
        public Guid Id { get; init; }
        public string Email { get; init; } = "";
        public string? DisplayName { get; init; }
        public string? BuyerId { get; init; }
        public bool EmailConfirmed { get; init; }
        public bool LockedOut { get; init; }
        public bool IsActive { get; init; }
        public string[] Roles { get; init; } = [];
        public string? BusinessName { get; init; }
        public DateTime? CreatedAtUtc { get; init; }
    }

    public sealed class AuditMirror
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public string Action { get; init; } = "";
        public string EntityType { get; init; } = "";
        public Guid? EntityId { get; init; }
        public string Summary { get; init; } = "";
        public string? Details { get; init; }
        public DateTime OccurredAtUtc { get; init; }
    }

    public sealed class AuthEnvelope
    {
        public string AccessToken { get; init; } = "";
        public AuthUserMirror User { get; init; } = new();
    }

    public sealed class AuthUserMirror
    {
        public Guid Id { get; init; }
        public string Email { get; init; } = "";
        public string? BuyerId { get; init; }
        public bool IsActive { get; init; }
        public List<string> Roles { get; init; } = [];
    }

    public sealed class IdMirror
    {
        public Guid Id { get; init; }
    }

    public sealed class OrderMirror
    {
        public Guid Id { get; init; }
        public string OrderNumber { get; init; } = "";
    }

    public sealed class ProductMirror
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = "";
        public Guid SellerBusinessId { get; init; }
        public int ListingStatus { get; init; }
        public int ModerationStatus { get; init; }
    }

    public sealed class CategoryMirror
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = "";
    }
}
