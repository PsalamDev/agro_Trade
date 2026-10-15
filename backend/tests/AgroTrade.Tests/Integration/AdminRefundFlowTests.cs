using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AgroTrade.Domain.Enums;
using AgroTrade.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AgroTrade.Tests.Integration;

/// <summary>
/// Refunds (roadmap §5.5): an administrator gives money back on a settled payment. The refund
/// is booked as a compensating ledger entry — negative, so the order's net settled figure falls
/// with it — and a full refund of the payment's remaining refundable amount moves the payment
/// to <see cref="PaymentStatus.Refunded"/> while a Paid invoice reopens.
///
/// <para>
/// The load-bearing claim here is that a refund is a ledger movement, not an edit. The order
/// rolls back up from the signed entries, the invoice reopens when the order is no longer
/// fully paid, and a refund can never exceed what the payment still has refundable — so a
/// doubled or retried request refuses rather than silently over-refunds.
/// </para>
/// </summary>
public sealed class AdminRefundFlowTests : IClassFixture<TestingWebAppFactory>
{
    private const string Password = "StrongPass123";

    private readonly TestingWebAppFactory _factory;
    private readonly HttpClient _client;

    public AdminRefundFlowTests(TestingWebAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureSchema();
        _client = factory.CreateClient();
    }

    // ---- The refund path ----

    [Fact]
    public async Task FullRefundReturnsTheMoneyAndReopensASettledInvoice()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("fullrefund");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var adminToken = await AdminTokenAsync();

        (await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/invoice", sellerToken, new { })).EnsureSuccessStatusCode();

        var manual = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 12000m, remittanceReference = "UTR-REFUND-001" });
        Assert.Equal(HttpStatusCode.Created, manual.StatusCode);
        var payment = await manual.Content.ReadFromJsonAsync<PaymentDtoMirror>();

        // Sanity: the money landed and closed the invoice, exactly as the settle tests establish.
        var invoice = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/invoice", buyerToken);
        Assert.Equal(
            InvoiceStatus.Paid,
            (await invoice.Content.ReadFromJsonAsync<InvoiceDtoMirror>())!.Status);

        // Full refund is the default when no amount is supplied.
        var refund = await PostAuthenticatedAsync(
            $"/api/v1/admin/payments/{payment!.Id}/refund", adminToken,
            new { reason = "Goods never dispatched." });
        Assert.Equal(HttpStatusCode.OK, refund.StatusCode);
        var refunded = await refund.Content.ReadFromJsonAsync<PaymentDtoMirror>();
        Assert.NotNull(refunded);
        Assert.Equal(PaymentStatus.Refunded, refunded!.Status);
        Assert.NotNull(refunded.RefundedAtUtc);
        Assert.Equal(12000m, refunded.RefundedAmount);

        // The ledger: a compensating entry, negative, with its own reference. The order's
        // financial record now tells a reader that the money both arrived and went back.
        var entries = await GetLedgerAsync(orderId, buyerToken);
        Assert.Equal(2, entries.Count);
        var entry = entries[1];
        Assert.StartsWith("TXN-", entry.Reference);
        Assert.Equal(TransactionType.Refund, entry.Type);
        Assert.Equal(-12000m, entry.Amount);
        Assert.Equal(payment.Id, entry.PaymentId);
        Assert.StartsWith("PAY-", entry.PaymentReference);
        Assert.Contains("Refund of", entry.Narrative!, StringComparison.Ordinal);
        Assert.Contains(payment.Reference, entry.Narrative!, StringComparison.Ordinal);
        Assert.Contains("Goods never dispatched.", entry.Narrative!, StringComparison.Ordinal);

        // The order is back below fully paid, and so is the invoice.
        var order = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}", buyerToken);
        Assert.Equal(
            PaymentStatus.None,
            (await order.Content.ReadFromJsonAsync<OrderPaymentMirror>())!.PaymentStatus);

        var reopened = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/invoice", buyerToken);
        var reopenedDto = await reopened.Content.ReadFromJsonAsync<InvoiceDtoMirror>();
        Assert.Equal(InvoiceStatus.Issued, reopenedDto!.Status);
        Assert.Null(reopenedDto.PaidAtUtc);

        // The buyer sees the refund on their own payment list.
        var payments = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/payments", buyerToken);
        var list = await payments.Content.ReadFromJsonAsync<List<PaymentDtoMirror>>();
        var mine = Assert.Single(list!);
        Assert.Equal(PaymentStatus.Refunded, mine.Status);
        Assert.Equal(12000m, mine.RefundedAmount);
        Assert.NotNull(mine.RefundedAtUtc);

        // Both parties are told money moved on their order.
        Assert.True(
            await HasNotificationAsync(buyerToken, NotificationType.Refund),
            "The buyer was not notified of the refund.");
        Assert.True(
            await HasNotificationAsync(sellerToken, NotificationType.Refund),
            "The seller was not notified of the refund.");

        // And the act is written to the audit trail, attributed to whoever signed in.
        Assert.True(
            await AuditLogHasRefundAsync(adminToken, payment.Id),
            "The refund was not recorded in the audit trail.");
    }

    [Fact]
    public async Task APartialRefundLeavesThePaymentPaidAndCapsTheNextRefund()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("partialrefund");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var adminToken = await AdminTokenAsync();

        var orderDetail = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}", buyerToken);
        var orderNumber = (await orderDetail.Content.ReadFromJsonAsync<OrderPaymentMirror>())!.OrderNumber;

        await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 12000m, remittanceReference = "UTR-PART-REF-001" });
        var payments = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/payments", buyerToken);
        var paymentId = (await payments.Content.ReadFromJsonAsync<List<PaymentDtoMirror>>())![0].Id;

        // Money back, but not all of it: most of the payment is still money the seller holds.
        var partial = await PostAuthenticatedAsync(
            $"/api/v1/admin/payments/{paymentId}/refund", adminToken,
            new { amount = 4000m });
        Assert.Equal(HttpStatusCode.OK, partial.StatusCode);
        var afterPartial = await partial.Content.ReadFromJsonAsync<PaymentDtoMirror>();
        Assert.Equal(PaymentStatus.Paid, afterPartial!.Status);
        Assert.Null(afterPartial.RefundedAtUtc);
        Assert.Equal(4000m, afterPartial.RefundedAmount);

        var order = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}", buyerToken);
        Assert.Equal(
            PaymentStatus.PartiallyPaid,
            (await order.Content.ReadFromJsonAsync<OrderPaymentMirror>())!.PaymentStatus);

        // The admin list nets refunds out of the settled column, so the table shows the money
        // the seller still holds, not the gross figure the order began with.
        var adminOrders = await GetAuthenticatedAsync(
            $"/api/v1/admin/orders?q={orderNumber}&pageSize=25", adminToken);
        var adminRow = (await adminOrders.Content.ReadFromJsonAsync<AdminOrdersMirror>())!.Items
            .Single(o => o.OrderNumber == orderNumber);
        Assert.Equal(12000m, adminRow.Total);
        Assert.Equal(8000m, adminRow.AmountPaid);

        // The admin read endpoint shows exactly what is left to refund.
        var adminPayments = await GetAuthenticatedAsync(
            $"/api/v1/admin/orders/{orderId}/payments", adminToken);
        var adminList = await adminPayments.Content.ReadFromJsonAsync<List<PaymentDtoMirror>>();
        var forRefund = Assert.Single(adminList!);
        Assert.Equal(4000m, forRefund.RefundedAmount);
        Assert.Equal(12000m, forRefund.Amount);
        Assert.Equal(8000m, forRefund.Amount - (forRefund.RefundedAmount ?? 0m));

        // Asking for more than is refundable refuses rather than refunding into the negative.
        var tooMuch = await PostAuthenticatedAsync(
            $"/api/v1/admin/payments/{paymentId}/refund", adminToken,
            new { amount = 9000m });
        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
        Assert.Contains("REFUND_EXCEEDS_PAYMENT", await tooMuch.Content.ReadAsStringAsync());

        // Refunding the rest closes the payment's story.
        var rest = await PostAuthenticatedAsync(
            $"/api/v1/admin/payments/{paymentId}/refund", adminToken,
            new { amount = 8000m });
        Assert.Equal(HttpStatusCode.OK, rest.StatusCode);
        Assert.Equal(
            PaymentStatus.Refunded,
            (await rest.Content.ReadFromJsonAsync<PaymentDtoMirror>())!.Status);

        var entries = await GetLedgerAsync(orderId, buyerToken);
        Assert.Equal(3, entries.Count);
        Assert.Equal(12000m - 4000m - 8000m, entries.Sum(e => e.Amount));
    }

    [Fact]
    public async Task AZeroOrNegativeAmountIsRejected()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("negative");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var adminToken = await AdminTokenAsync();

        await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 5000m, remittanceReference = "UTR-NEG-REF-001" });
        var payments = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/payments", buyerToken);
        var paymentId = (await payments.Content.ReadFromJsonAsync<List<PaymentDtoMirror>>())![0].Id;

        // A null amount means "everything remaining" (the full-refund default), so only amounts
        // that cannot exist are refused. Refunding zero or less would book a ledger row that
        // moved nothing, or moved money the wrong way.
        foreach (var amount in new[] { 0m, -100m })
        {
            var response = await PostAuthenticatedAsync(
                $"/api/v1/admin/payments/{paymentId}/refund", adminToken,
                new { amount });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("INVALID_REFUND_AMOUNT", await response.Content.ReadAsStringAsync());
        }

        // Nothing moved: the ledger still holds only the settlement.
        Assert.Single(await GetLedgerAsync(orderId, buyerToken));
    }

    // ---- Refusals ----

    [Fact]
    public async Task APendingOrFailedPaymentCannotBeRefunded()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("notsettled");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var adminToken = await AdminTokenAsync();

        // A charge raised but not settled is money in flight, not money received.
        var gateway = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments", buyerToken, new { amount = 5000m });
        Assert.Equal(HttpStatusCode.Created, gateway.StatusCode);
        var pending = await gateway.Content.ReadFromJsonAsync<PaymentDtoMirror>();

        var attempt = await PostAuthenticatedAsync(
            $"/api/v1/admin/payments/{pending!.Id}/refund", adminToken, new { });
        Assert.Equal(HttpStatusCode.Conflict, attempt.StatusCode);
        Assert.Contains("PAYMENT_NOT_REFUNDABLE", await attempt.Content.ReadAsStringAsync());

        // The gateway refused the money: it never landed, so there is still nothing to refund.
        (await PostAuthenticatedAsync(
            $"/api/v1/sandbox/payments/{pending.Id}/settle", buyerToken,
            new { outcome = "failed" })).EnsureSuccessStatusCode();

        var failed = await PostAuthenticatedAsync(
            $"/api/v1/admin/payments/{pending.Id}/refund", adminToken, new { });
        Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);

        // A payment that never settled never wrote a ledger row, so there is nothing financial
        // to inspect and nothing to refund against.
        Assert.Empty(await GetLedgerAsync(orderId, buyerToken));
    }

    [Fact]
    public async Task AFullyRefundedPaymentCannotBeRefundedAgain()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("twice");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var adminToken = await AdminTokenAsync();

        await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 6000m, remittanceReference = "UTR-TWICE-001" });
        var payments = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/payments", buyerToken);
        var paymentId = (await payments.Content.ReadFromJsonAsync<List<PaymentDtoMirror>>())![0].Id;

        (await PostAuthenticatedAsync(
            $"/api/v1/admin/payments/{paymentId}/refund", adminToken, new { })).EnsureSuccessStatusCode();

        // A doubled full refund would put the order *negative* — the compensating entry exists
        // precisely so a second pass is refused instead of inventing money that left twice.
        var second = await PostAuthenticatedAsync(
            $"/api/v1/admin/payments/{paymentId}/refund", adminToken, new { });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("PAYMENT_NOT_REFUNDABLE", await second.Content.ReadAsStringAsync());

        var entries = await GetLedgerAsync(orderId, buyerToken);
        Assert.Equal(2, entries.Count);
        Assert.Equal(0m, entries.Sum(e => e.Amount));
    }

    [Fact]
    public async Task RefundOfAnUnknownPaymentIs404()
    {
        var (buyerToken, _) = await SeedTradeAsync("unknown");
        var adminToken = await AdminTokenAsync();

        var response = await PostAuthenticatedAsync(
            $"/api/v1/admin/payments/{Guid.NewGuid()}/refund", adminToken, new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("PAYMENT_NOT_FOUND", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OnlyAnAdministratorCanRefund()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("forbid");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 5000m, remittanceReference = "UTR-FORBID-001" });
        var payments = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/payments", buyerToken);
        var paymentId = (await payments.Content.ReadFromJsonAsync<List<PaymentDtoMirror>>())![0].Id;

        // The buyer may read their payment list but must not be able to give the money back;
        // refunds are a money movement, and money movements need a second pair of hands.
        var response = await PostAuthenticatedAsync(
            $"/api/v1/admin/payments/{paymentId}/refund", buyerToken, new { amount = 5000m });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(await GetLedgerAsync(orderId, buyerToken));
    }

    // ---- Seeding ----

    private async Task<(string BuyerToken, string SellerToken)> SeedTradeAsync(string tag)
    {
        var sellerToken = await VerifyBusinessAsync(
            await CreateCompanyUserAsync($"refund_seller_{tag}_{Guid.NewGuid():N}"));

        var buyerToken = await RegisterAndLoginAsync($"refund_buyer_{tag}_{Guid.NewGuid():N}");
        return (buyerToken, sellerToken);
    }

    private async Task<Guid> SeedOrderAsync(string buyerToken, string sellerToken, string currency = "USD")
    {
        var rfq = await PostAuthenticatedAsync("/api/v1/rfqs", buyerToken, new
        {
            title = $"refund trade {Guid.NewGuid():N}",
            quantity = 5m,
            unit = (int)PriceUnit.PerTon,
            destinationCountry = "NG",
            destinationPortOrCity = "Lagos",
            preferredIncoterm = (int)Incoterms.Cif,
            preferredCurrency = currency,
            expiresAt = DateTime.UtcNow.AddDays(30),
        });
        rfq.EnsureSuccessStatusCode();
        var rfqDto = await rfq.Content.ReadFromJsonAsync<RfqDetailDto>();

        var quotation = await PostAuthenticatedAsync($"/api/v1/rfqs/{rfqDto!.Id}/quotations", sellerToken, new
        {
            currency,
            incoterms = (int)Incoterms.Cif,
            portOfLoading = "Mombasa (MBA)",
            portOfDischarge = "Lagos (LOS)",
            leadTimeDays = 14,
            paymentTerms = (int)PaymentTerms.TtPartialDeposit,
            total = 12000m,
        });
        quotation.EnsureSuccessStatusCode();
        var quotationDto = await quotation.Content.ReadFromJsonAsync<QuotationDto>();

        (await PostAuthenticatedAsync(
            $"/api/v1/quotations/{quotationDto!.Id}/accept", buyerToken, new { })).EnsureSuccessStatusCode();

        var order = await PostAuthenticatedAsync("/api/v1/orders/from-quotation", buyerToken, new
        {
            quotationId = quotationDto.Id,
            placeImmediately = true,
        });
        Assert.Equal(HttpStatusCode.Created, order.StatusCode);
        return (await order.Content.ReadFromJsonAsync<OrderDetailMirror>())!.Id;
    }

    private async Task<string> VerifyBusinessAsync(string token)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];

        var registered = await PostAuthenticatedAsync("/api/v1/businesses", token, new
        {
            legalName = $"Refund Supplier {tag} Ltd",
            tradingName = $"Refund Supplier {tag}",
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

        var adminToken = await AdminTokenAsync();
        (await PostAuthenticatedAsync(
            $"/api/v1/admin/businesses/{businessId}/verification/review", adminToken,
            new { approve = true })).EnsureSuccessStatusCode();

        return token;
    }

    // ---- Reads ----

    private async Task<List<TransactionDtoMirror>> GetLedgerAsync(Guid orderId, string token)
    {
        var response = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/transactions", token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<TransactionDtoMirror>>() ?? [];
    }

    private async Task<bool> HasNotificationAsync(string token, NotificationType type)
    {
        var response = await GetAuthenticatedAsync("/api/v1/me/notifications?pageSize=100", token);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var items = root.ValueKind == System.Text.Json.JsonValueKind.Array
            ? root.EnumerateArray().ToArray()
            : root.GetProperty("items").EnumerateArray().ToArray();

        return items.Any(i => i.GetProperty("type").GetInt32() == (int)type);
    }

    private async Task<bool> AuditLogHasRefundAsync(string adminToken, Guid paymentId)
    {
        var response = await GetAuthenticatedAsync(
            $"/api/v1/admin/audit-log?action=PaymentRefund&pageSize=50", adminToken);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var items = root.ValueKind == System.Text.Json.JsonValueKind.Array
            ? root.EnumerateArray().ToArray()
            : root.GetProperty("items").EnumerateArray().ToArray();

        return items.Any(i => i.GetProperty("entityId").GetGuid() == paymentId);
    }

    // ---- HTTP helpers ----

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

    private static ByteArrayContent PdfPart()
    {
        var part = new ByteArrayContent("%PDF-1.4 registration"u8.ToArray());
        part.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return part;
    }

    private async Task<string> CreateCompanyUserAsync(string prefix)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = $"{prefix}@refund.tests.agrotrade.local",
            Email = $"{prefix}@refund.tests.agrotrade.local",
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
            email = $"{prefix}@refund.tests.agrotrade.local",
            password = Password,
            displayName = prefix,
            homeCountry = "NG",
            defaultCurrency = "USD",
            roles = new[] { RoleNames.Buyer },
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        return (await register.Content.ReadFromJsonAsync<AuthResponseDto>())!.AccessToken;
    }

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!.AccessToken;
    }

    private Task<string> AdminTokenAsync() =>
        LoginAsync(TestingWebAppFactory.AdminEmail, TestingWebAppFactory.AdminPassword);

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

    public sealed class PaymentDtoMirror
    {
        public Guid Id { get; init; }
        public Guid OrderId { get; init; }
        public string Reference { get; init; } = "";
        public decimal Amount { get; init; }
        public string Currency { get; init; } = "";
        public PaymentProvider Provider { get; init; }
        public PaymentMethod Method { get; init; }
        public PaymentStatus Status { get; init; }
        public string? ProviderRef { get; init; }
        public string? RemittanceReference { get; init; }
        public string? BankName { get; init; }
        public string? NextActionUrl { get; init; }
        public string? FailureReason { get; init; }
        public DateTime? ValueDateUtc { get; init; }
        public DateTime? PaidAtUtc { get; init; }
        public DateTime? RefundedAtUtc { get; init; }
        public decimal? RefundedAmount { get; init; }
    }

    public sealed class TransactionDtoMirror
    {
        public Guid Id { get; init; }
        public Guid OrderId { get; init; }
        public string Reference { get; init; } = "";
        public TransactionType Type { get; init; }
        public decimal Amount { get; init; }
        public string Currency { get; init; } = "";
        public Guid? PaymentId { get; init; }
        public string? PaymentReference { get; init; }
        public string? ExternalReference { get; init; }
        public DateTime OccurredAtUtc { get; init; }
        public string? Narrative { get; init; }
    }

    public sealed class InvoiceDtoMirror
    {
        public Guid OrderId { get; init; }
        public string InvoiceNumber { get; init; } = "";
        public decimal Amount { get; init; }
        public string Currency { get; init; } = "";
        public InvoiceStatus Status { get; init; }
        public DateTime? PaidAtUtc { get; init; }
    }

    public sealed class OrderPaymentMirror
    {
        public Guid Id { get; init; }
        public string OrderNumber { get; init; } = "";
        public PaymentStatus PaymentStatus { get; init; }
    }

    public sealed class OrderDetailMirror
    {
        public Guid Id { get; init; }
        public string OrderNumber { get; init; } = "";
    }

    public sealed class AdminOrdersMirror
    {
        public List<AdminOrderRowMirror> Items { get; init; } = [];
    }

    public sealed class AdminOrderRowMirror
    {
        public string OrderNumber { get; init; } = "";
        public decimal Total { get; init; }
        public decimal AmountPaid { get; init; }
    }

    public sealed class QuotationDto
    {
        public Guid Id { get; init; }
    }

    public sealed class RfqDetailDto
    {
        public Guid Id { get; init; }
    }

    public sealed class AuthResponseDto
    {
        public string AccessToken { get; init; } = "";
    }
}