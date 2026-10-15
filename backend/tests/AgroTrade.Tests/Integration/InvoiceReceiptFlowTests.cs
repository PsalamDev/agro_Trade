using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AgroTrade.Domain.Enums;
using AgroTrade.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AgroTrade.Tests.Integration;

/// <summary>
/// Invoice -> payment -> receipt: the seller issues an invoice against a placed order, the
/// payment settles it, and a receipt is produced for the money that arrived.
/// </summary>
public sealed class InvoiceReceiptFlowTests : IClassFixture<TestingWebAppFactory>
{
    private const string Password = "StrongPass123";

    private readonly TestingWebAppFactory _factory;
    private readonly HttpClient _client;

    public InvoiceReceiptFlowTests(TestingWebAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureSchema();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SellerIssuesAnInvoiceForTheOrderTotal()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("issue");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var response = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/invoice", sellerToken,
            new { dueDateUtc = DateTime.UtcNow.AddDays(30) });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var invoice = await response.Content.ReadFromJsonAsync<InvoiceDtoMirror>();
        Assert.NotNull(invoice);
        Assert.Equal(orderId, invoice!.OrderId);
        Assert.Equal(12000m, invoice.Amount);
        Assert.Equal("USD", invoice.Currency);
        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        Assert.StartsWith("INV-", invoice.InvoiceNumber);

        // Both parties read the invoice: the buyer needs it to know what to pay.
        var read = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/invoice", buyerToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(invoice.InvoiceNumber, (await read.Content.ReadFromJsonAsync<InvoiceDtoMirror>())!.InvoiceNumber);
    }

    [Fact]
    public async Task BuyerCannotIssueAnInvoice()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("buyerinv");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var response = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/invoice", buyerToken, new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AStrangerCannotReadTheInvoiceOrItsReceipts()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("stranger");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        (await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/invoice", sellerToken, new { })).EnsureSuccessStatusCode();

        // Somebody who is neither buyer nor seller learns nothing: the order reads as absent, so
        // the endpoint cannot be used to confirm that a trade or an invoice exists.
        var stranger = await RegisterAndLoginAsync($"inv_stranger_{Guid.NewGuid():N}");

        var invoice = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/invoice", stranger);
        Assert.Equal(HttpStatusCode.NotFound, invoice.StatusCode);

        var receipts = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/receipts", stranger);
        Assert.Equal(HttpStatusCode.NotFound, receipts.StatusCode);
    }

    [Fact]
    public async Task OnlyOneInvoicePerOrder()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("dup");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        (await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/invoice", sellerToken, new { })).EnsureSuccessStatusCode();

        var second = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/invoice", sellerToken, new { });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task SettlingTheInvoiceAutoGeneratesAReceiptAndClosesTheInvoice()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("receipt");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        (await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/invoice", sellerToken, new { })).EnsureSuccessStatusCode();

        var manual = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 12000m, remittanceReference = "UTR-RCPT-001" });
        Assert.Equal(HttpStatusCode.Created, manual.StatusCode);
        var payment = await manual.Content.ReadFromJsonAsync<PaymentDtoMirror>();

        var receipts = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/receipts", buyerToken);
        receipts.EnsureSuccessStatusCode();
        var list = await receipts.Content.ReadFromJsonAsync<List<ReceiptDtoMirror>>();
        var receipt = Assert.Single(list!);

        Assert.StartsWith("RCPT-", receipt.ReceiptNumber);
        Assert.Equal(12000m, receipt.Amount);
        Assert.Equal("USD", receipt.Currency);
        Assert.Equal(payment!.Id, receipt.PaymentId);

        // A fully-paid order closes its invoice.
        var invoice = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/invoice", buyerToken);
        var invoiceDto = await invoice.Content.ReadFromJsonAsync<InvoiceDtoMirror>();
        Assert.Equal(InvoiceStatus.Paid, invoiceDto!.Status);
        Assert.NotNull(invoiceDto.PaidAtUtc);
    }

    [Fact]
    public async Task APartialPaymentProducesAReceiptButLeavesTheInvoiceOpen()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("partial");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        (await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/invoice", sellerToken, new { })).EnsureSuccessStatusCode();

        var deposit = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 4000m, remittanceReference = "UTR-PART-001" });
        Assert.Equal(HttpStatusCode.Created, deposit.StatusCode);

        var receipts = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/receipts", buyerToken);
        var list = await receipts.Content.ReadFromJsonAsync<List<ReceiptDtoMirror>>();
        Assert.Single(list!);
        Assert.Equal(4000m, list![0].Amount);

        var invoice = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/invoice", buyerToken);
        var invoiceDto = await invoice.Content.ReadFromJsonAsync<InvoiceDtoMirror>();
        Assert.Equal(InvoiceStatus.Issued, invoiceDto!.Status);
    }

    [Fact]
    public async Task AReceiptIsGeneratedOncePerSettlement()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("idem");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var manual = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 4000m, remittanceReference = "UTR-IDEM-001" });
        var payment = await manual.Content.ReadFromJsonAsync<PaymentDtoMirror>();

        // Asking for the receipt again returns the one settlement produced, not a second copy.
        var again = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/{payment!.Id}/receipt", buyerToken, new { });
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);

        var receipts = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/receipts", buyerToken);
        var list = await receipts.Content.ReadFromJsonAsync<List<ReceiptDtoMirror>>();
        Assert.Single(list!);
    }

    [Fact]
    public async Task AReceiptCannotBeIssuedForAnUnsettledPayment()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("unsettled");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        // A gateway charge that has been raised but not settled is not money that arrived.
        var gateway = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments", buyerToken, new { amount = 5000m });
        var payment = await gateway.Content.ReadFromJsonAsync<PaymentDtoMirror>();

        var response = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/{payment!.Id}/receipt", buyerToken, new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var receipts = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/receipts", buyerToken);
        Assert.Empty((await receipts.Content.ReadFromJsonAsync<List<ReceiptDtoMirror>>())!);
    }

    // ---- Seeding ----

    private async Task<(string BuyerToken, string SellerToken)> SeedTradeAsync(string tag)
    {
        var sellerToken = await VerifyBusinessAsync(
            await CreateCompanyUserAsync($"inv_seller_{tag}_{Guid.NewGuid():N}"));

        var buyerToken = await RegisterAndLoginAsync($"inv_buyer_{tag}_{Guid.NewGuid():N}");
        return (buyerToken, sellerToken);
    }

    private async Task<Guid> SeedOrderAsync(string buyerToken, string sellerToken, string currency = "USD")
    {
        var rfq = await PostAuthenticatedAsync("/api/v1/rfqs", buyerToken, new
        {
            title = $"invoice trade {Guid.NewGuid():N}",
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
            legalName = $"Invoice Supplier {tag} Ltd",
            tradingName = $"Invoice Supplier {tag}",
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
            UserName = $"{prefix}@invoice.tests.agrotrade.local",
            Email = $"{prefix}@invoice.tests.agrotrade.local",
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
            email = $"{prefix}@invoice.tests.agrotrade.local",
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

    public sealed class InvoiceDtoMirror
    {
        public Guid Id { get; init; }
        public Guid OrderId { get; init; }
        public string InvoiceNumber { get; init; } = "";
        public decimal Amount { get; init; }
        public string Currency { get; init; } = "";
        public InvoiceStatus Status { get; init; }
        public DateTime IssuedAtUtc { get; init; }
        public DateTime? DueDateUtc { get; init; }
        public DateTime? PaidAtUtc { get; init; }
    }

    public sealed class ReceiptDtoMirror
    {
        public Guid Id { get; init; }
        public Guid OrderId { get; init; }
        public Guid? PaymentId { get; init; }
        public Guid? InvoiceId { get; init; }
        public string ReceiptNumber { get; init; } = "";
        public decimal Amount { get; init; }
        public string Currency { get; init; } = "";
        public DateTime IssuedAtUtc { get; init; }
    }

    public sealed class PaymentDtoMirror
    {
        public Guid Id { get; init; }
        public Guid OrderId { get; init; }
        public string Reference { get; init; } = "";
        public decimal Amount { get; init; }
        public string Currency { get; init; } = "";
        public PaymentStatus Status { get; init; }
        public string? ProviderRef { get; init; }
    }

    public sealed class OrderDetailMirror
    {
        public Guid Id { get; init; }
        public string OrderNumber { get; init; } = "";
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
