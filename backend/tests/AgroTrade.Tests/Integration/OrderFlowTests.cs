using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AgroTrade.Domain.Enums;
using AgroTrade.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AgroTrade.Tests.Integration;

/// <summary>
/// Trade execution, task 2.1: converting an accepted quotation into an order without
/// losing any agreed trade term, creating an order by hand, and the authorisation rules
/// around reading them (plan §02 — Orders).
/// </summary>
public sealed class OrderFlowTests : IClassFixture<TestingWebAppFactory>
{
    private const string Password = "StrongPass123";

    private readonly TestingWebAppFactory _factory;
    private readonly HttpClient _client;

    public OrderFlowTests(TestingWebAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureSchema();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AcceptedQuotation_BecomesOrder_PreservingEveryTradeTerm()
    {
        var (buyerToken, supplierToken) = await SeedTradeAsync("preserve");
        var quotationId = await SeedAcceptedQuotationAsync(buyerToken, supplierToken, $"preserve {Guid.NewGuid():N}");

        var created = await PostAuthenticatedAsync("/api/v1/orders/from-quotation", buyerToken, new
        {
            quotationId,
            placeImmediately = true,
        });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var order = await created.Content.ReadFromJsonAsync<OrderDetailDto>();
        Assert.NotNull(order);

        // Identity and traceability back to the source trade
        Assert.StartsWith("AG-", order!.OrderNumber);
        Assert.Equal(quotationId, order.QuotationId);
        Assert.NotNull(order.TradeRequestId);

        // Every trade term survived the handoff
        Assert.Equal("USD", order.Currency);
        Assert.Equal(Incoterms.Cif, order.Incoterms);
        Assert.Equal("Mombasa (MBA)", order.PortOfLoading);
        Assert.Equal("Lagos (LOS)", order.PortOfDischarge);
        Assert.Equal(PaymentTerms.TtPartialDeposit, order.PaymentTerms);
        Assert.Equal("NG", order.DestinationCountry);

        // Money: total matches the agreed quotation, and nothing is owed yet
        Assert.Equal(43000m, order.Total);
        Assert.Equal(0m, order.AmountPaid);
        Assert.Equal(43000m, order.BalanceDue);
        Assert.Equal(PaymentStatus.None, order.PaymentStatus);

        // The agreed total becomes one snapshotted line at the RFQ quantity
        var line = Assert.Single(order.Items);
        Assert.Equal(20m, line.Quantity);
        Assert.Equal(43000m, line.LineTotal);
        Assert.Equal("USD", line.Currency);

        // And the RFQ is spent, so the same quote cannot be reused
        var rfq = await GetAsync($"/api/v1/rfqs/{order.TradeRequestId}", buyerToken);
        Assert.Equal(HttpStatusCode.OK, rfq.StatusCode);
        var rfqDto = await rfq.Content.ReadFromJsonAsync<RfqDetailDto>();
        Assert.Equal(RfqStatus.ConvertedToOrder, rfqDto!.Status);
    }

    [Fact]
    public async Task Quotation_CannotBeConvertedTwice()
    {
        var (buyerToken, supplierToken) = await SeedTradeAsync("double");
        var quotationId = await SeedAcceptedQuotationAsync(buyerToken, supplierToken, $"double {Guid.NewGuid():N}");

        var first = await PostAuthenticatedAsync("/api/v1/orders/from-quotation", buyerToken, new
        {
            quotationId,
            placeImmediately = true,
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await PostAuthenticatedAsync("/api/v1/orders/from-quotation", buyerToken, new
        {
            quotationId,
            placeImmediately = true,
        });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var problem = await second.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Equal("QUOTATION_ALREADY_CONVERTED", problem?.Code);
    }

    [Fact]
    public async Task UnacceptedQuotation_CannotBecomeOrder()
    {
        var (buyerToken, supplierToken) = await SeedTradeAsync("unaccepted");
        var quotationId = await SeedQuotationAsync(buyerToken, supplierToken, $"unaccepted {Guid.NewGuid():N}", accept: false);

        var response = await PostAuthenticatedAsync("/api/v1/orders/from-quotation", buyerToken, new { quotationId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Equal("QUOTATION_NOT_ACCEPTED", problem?.Code);
    }

    [Fact]
    public async Task OnlyTheRfqBuyer_CanConvertTheQuotation()
    {
        var (buyerToken, supplierToken) = await SeedTradeAsync("notmine");
        var quotationId = await SeedAcceptedQuotationAsync(buyerToken, supplierToken, $"notmine {Guid.NewGuid():N}");

        // A different importer tries to place the order
        var (otherToken, _) = await SeedTradeAsync("other");
        var response = await PostAuthenticatedAsync("/api/v1/orders/from-quotation", otherToken, new { quotationId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Equal("NOT_RFQ_BUYER", problem?.Code);
    }

    [Fact]
    public async Task ManualOrder_TotalsLineItemsAndAppliesDiscount()
    {
        var (buyerToken, supplierToken) = await SeedTradeAsync("manual");
        await SeedAcceptedQuotationAsync(buyerToken, supplierToken, $"manual {Guid.NewGuid():N}");
        var sellerBusinessId = await SupplierBusinessIdAsync(supplierToken);

        var response = await PostAuthenticatedAsync("/api/v1/orders", buyerToken, new
        {
            sellerBusinessId,
            currency = "EUR",
            incoterms = (int)Incoterms.Cfr,
            paymentTerms = (int)PaymentTerms.LetterOfCredit,
            portOfLoading = "Apapa (APP)",
            portOfDischarge = "Hamburg (HAM)",
            destinationCountry = "DE",
            transportMode = (int)ShipmentMode.Sea,
            discount = 500m,
            items = new object[]
            {
                new { productName = "Cocoa Butter Block 10 kg", quantity = 12m, unit = (int)PriceUnit.PerTon, unitPrice = 4200m },
                new { productName = "Cocoa Powder (Alkalised)", quantity = 8m, unit = (int)PriceUnit.PerTon, unitPrice = 3900m },
            },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderDetailDto>();
        Assert.NotNull(order);

        Assert.Equal("EUR", order!.Currency);
        Assert.Equal(Incoterms.Cfr, order.Incoterms);
        Assert.Equal("DE", order.DestinationCountry);
        Assert.Equal(2, order.Items.Count);
        Assert.Equal(81600m, order.Subtotal);   // 12*4200 + 8*3900
        Assert.Equal(500m, order.Discount);
        Assert.Equal(81100m, order.Total);      // subtotal - discount
        Assert.All(order.Items, i => Assert.Equal("EUR", i.Currency));
    }

    [Fact]
    public async Task ManualOrder_RejectsEmptyLinesNegativeQuantityAndOversizedDiscount()
    {
        var (buyerToken, supplierToken) = await SeedTradeAsync("invalid");
        await SeedAcceptedQuotationAsync(buyerToken, supplierToken, $"invalid {Guid.NewGuid():N}");
        var sellerBusinessId = await SupplierBusinessIdAsync(supplierToken);

        var noItems = await PostAuthenticatedAsync("/api/v1/orders", buyerToken, new
        {
            sellerBusinessId,
            currency = "EUR",
            incoterms = (int)Incoterms.Fob,
            paymentTerms = (int)PaymentTerms.TtAdvance,
            items = Array.Empty<object>(),
        });
        Assert.Equal(HttpStatusCode.BadRequest, noItems.StatusCode);
        Assert.Equal("VALIDATION_ERROR",
            (await noItems.Content.ReadFromJsonAsync<ProblemDetailsDto>())?.Code);

        var negative = await PostAuthenticatedAsync("/api/v1/orders", buyerToken, new
        {
            sellerBusinessId,
            currency = "EUR",
            incoterms = (int)Incoterms.Fob,
            paymentTerms = (int)PaymentTerms.TtAdvance,
            items = new object[] { new { productName = "Cocoa Beans", quantity = -5m, unit = (int)PriceUnit.PerTon, unitPrice = 2400m } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);

        var badIncoterm = await PostAuthenticatedAsync("/api/v1/orders", buyerToken, new
        {
            sellerBusinessId,
            currency = "EUR",
            incoterms = 99,
            paymentTerms = (int)PaymentTerms.TtAdvance,
            items = new object[] { new { productName = "Cocoa Beans", quantity = 5m, unit = (int)PriceUnit.PerTon, unitPrice = 2400m } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, badIncoterm.StatusCode);

        var tooMuchDiscount = await PostAuthenticatedAsync("/api/v1/orders", buyerToken, new
        {
            sellerBusinessId,
            currency = "EUR",
            incoterms = (int)Incoterms.Fob,
            paymentTerms = (int)PaymentTerms.TtAdvance,
            discount = 999999m,
            items = new object[] { new { productName = "Cocoa Beans", quantity = 1m, unit = (int)PriceUnit.PerTon, unitPrice = 10m } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, tooMuchDiscount.StatusCode);
        Assert.Equal("ORDER_DISCOUNT_TOO_LARGE",
            (await tooMuchDiscount.Content.ReadFromJsonAsync<ProblemDetailsDto>())?.Code);
    }

    [Fact]
    public async Task Order_VisibleToBuyerAndSeller_ButNotThirdParties()
    {
        var (buyerToken, supplierToken) = await SeedTradeAsync("visibility");
        var quotationId = await SeedAcceptedQuotationAsync(buyerToken, supplierToken, $"visibility {Guid.NewGuid():N}");

        var created = await PostAuthenticatedAsync("/api/v1/orders/from-quotation", buyerToken, new { quotationId });
        var order = await created.Content.ReadFromJsonAsync<OrderDetailDto>();
        Assert.NotNull(order);

        // Buyer can read it, and is told they are the buyer
        var asBuyer = await GetAsync($"/api/v1/orders/{order!.Id}", buyerToken);
        Assert.Equal(HttpStatusCode.OK, asBuyer.StatusCode);
        var buyerView = await asBuyer.Content.ReadFromJsonAsync<OrderDetailDto>();
        Assert.True(buyerView!.IsBuyer);
        Assert.False(buyerView.IsSeller);

        // Seller can read it, and sees it in the selling list
        var asSeller = await GetAsync($"/api/v1/orders/{order.Id}", supplierToken);
        Assert.Equal(HttpStatusCode.OK, asSeller.StatusCode);

        // The seller is told they are the seller, and sees who they are selling to rather than
        // the buyer being labelled "You" — which is what this page showed before it carried
        // the party flags.
        var sellerView = await asSeller.Content.ReadFromJsonAsync<OrderDetailDto>();
        Assert.False(sellerView!.IsBuyer);
        Assert.True(sellerView.IsSeller);
        Assert.NotEqual("You", sellerView.BuyerDisplayName);
        Assert.False(string.IsNullOrWhiteSpace(sellerView.BuyerDisplayName));

        var selling = await GetAsync("/api/v1/orders/selling", supplierToken);
        Assert.Equal(HttpStatusCode.OK, selling.StatusCode);
        var sellingPage = await selling.Content.ReadFromJsonAsync<PagedResultDto<OrderCardDto>>();
        Assert.Contains(sellingPage!.Items, i => i.Id == order.Id);

        // An unrelated business gets 404, not 403, so it cannot even learn the order exists
        var (strangerToken, _) = await SeedTradeAsync("stranger");
        var asStranger = await GetAsync($"/api/v1/orders/{order.Id}", strangerToken);
        Assert.Equal(HttpStatusCode.NotFound, asStranger.StatusCode);

        // Anonymous is rejected outright
        var anonymous = await _client.GetAsync($"/api/v1/orders/{order.Id}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task OrderList_FiltersByStatus_AndToleratesUnknownStatus()
    {
        var (buyerToken, supplierToken) = await SeedTradeAsync("filter");
        var quotationId = await SeedAcceptedQuotationAsync(buyerToken, supplierToken, $"filter {Guid.NewGuid():N}");
        await PostAuthenticatedAsync("/api/v1/orders/from-quotation", buyerToken, new { quotationId, placeImmediately = true });

        var placed = await GetAsync("/api/v1/orders?status=Placed", buyerToken);
        Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
        var placedPage = await placed.Content.ReadFromJsonAsync<PagedResultDto<OrderCardDto>>();
        Assert.All(placedPage!.Items, i => Assert.Equal(OrderStatus.Placed, i.Status));

        // An unrecognised filter yields an empty page rather than an error the UI can't act on
        var unknown = await GetAsync("/api/v1/orders?status=NotARealStatus", buyerToken);
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        var unknownPage = await unknown.Content.ReadFromJsonAsync<PagedResultDto<OrderCardDto>>();
        Assert.Empty(unknownPage!.Items);
    }

    // ---- Fixtures ----

    /// <summary>
    /// Creates a seller and a buyer. The seller role is <see cref="RoleNames.Company"/>,
    /// which is deliberately NOT self-registrable (see RoleNames.Registrable) — the single
    /// supplier account is granted out of band, so the test does the same via UserManager.
    /// The buyer self-registers with the Buyer role.
    /// </summary>
    private async Task<(string BuyerToken, string SupplierToken)> SeedTradeAsync(string tag)
    {
        var supplierToken = await CreateCompanyUserAsync($"seller_{tag}_{Guid.NewGuid():N}");

        var registered = await PostAuthenticatedAsync("/api/v1/businesses", supplierToken, new
        {
            legalName = $"Supplier {tag} Ltd",
            tradingName = $"Supplier {tag}",
            country = "NG",
            businessType = (int)BusinessType.LimitedCompany,
        });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        var businessId = await SupplierBusinessIdAsync(supplierToken);

        // Only a verified business may quote, so walk the real queue rather than shortcutting
        // it: upload a document, submit, then have an administrator approve.
        // Each multipart part needs its own Content-Type; UploadGuard reads the file part's
        // declared MIME type, not the request's.
        var pdfPart = new ByteArrayContent("%PDF-1.4 test registration certificate"u8.ToArray());
        pdfPart.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var documentForm = new MultipartFormDataContent
        {
            { new StringContent(((int)DocumentType.BusinessRegistration).ToString()), "docType" },
            { pdfPart, "file", "certificate.pdf" },
        };
        var uploadRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/businesses/{businessId}/documents") { Content = documentForm };
        uploadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", supplierToken);
        var uploaded = await _client.SendAsync(uploadRequest);
        Assert.True(uploaded.IsSuccessStatusCode,
            $"Document upload failed ({(int)uploaded.StatusCode}): {await uploaded.Content.ReadAsStringAsync()}");

        // No request body: the submission endpoint takes only the business id.
        var submitRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/businesses/{businessId}/verification-submissions");
        submitRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", supplierToken);
        var submitted = await _client.SendAsync(submitRequest);
        Assert.True(submitted.IsSuccessStatusCode,
            $"Verification submission failed ({(int)submitted.StatusCode}): {await submitted.Content.ReadAsStringAsync()}");

        var adminToken = await AdminTokenAsync();
        var reviewed = await PostAuthenticatedAsync(
            $"/api/v1/admin/businesses/{businessId}/verification/review", adminToken,
            new { approve = true, notes = "Approved by OrderFlowTests." });
        reviewed.EnsureSuccessStatusCode();

        var buyerToken = await RegisterAndLoginAsync($"buyer_{tag}_{Guid.NewGuid():N}");
        return (buyerToken, supplierToken);
    }

    /// <summary>
    /// Creates a user holding the Company role and logs them in. Mirrors how the demo
    /// supplier account exists in the real database.
    /// </summary>
    private async Task<string> CreateCompanyUserAsync(string prefix)
    {
        var email = $"{prefix}@orders.tests.agrotrade.local";

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = prefix,
            HomeCountry = "NG",
            DefaultCurrency = "USD",
            IsActive = true,
            EmailConfirmed = true,
        };

        var created = await userManager.CreateAsync(user, Password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        Assert.True(
            (await userManager.AddToRoleAsync(user, RoleNames.Company)).Succeeded,
            "Failed to grant the Company role.");

        return await LoginAsync(email, Password);
    }

    /// <summary>Signs in and returns the access token.</summary>
    private async Task<string> LoginAsync(string email, string password)
    {
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password,
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var dto = await login.Content.ReadFromJsonAsync<AuthResponseDto>();
        return dto!.AccessToken;
    }

    private async Task<Guid> SupplierBusinessIdAsync(string token)
    {
        var response = await GetAuthenticatedAsync("/api/v1/businesses/mine", token);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var first = root.ValueKind == JsonValueKind.Array ? root[0] : root.GetProperty("items")[0];
        return first.GetProperty("id").GetGuid();
    }

    private async Task<Guid> SeedQuotationAsync(string buyerToken, string supplierToken, string title, bool accept)
    {
        var rfq = await PostAuthenticatedAsync("/api/v1/rfqs", buyerToken, new
        {
            title,
            quantity = 20m,
            unit = (int)PriceUnit.PerTon,
            destinationCountry = "NG",
            destinationPortOrCity = "Lagos",
            preferredIncoterm = (int)Incoterms.Cif,
            preferredCurrency = "USD",
            expiresAt = DateTime.UtcNow.AddDays(30),
        });
        rfq.EnsureSuccessStatusCode();
        var rfqDto = await rfq.Content.ReadFromJsonAsync<RfqDetailDto>();

        var quotation = await PostAuthenticatedAsync($"/api/v1/rfqs/{rfqDto!.Id}/quotations", supplierToken, new
        {
            currency = "USD",
            incoterms = (int)Incoterms.Cif,
            portOfLoading = "Mombasa (MBA)",
            portOfDischarge = "Lagos (LOS)",
            leadTimeDays = 18,
            paymentTerms = (int)PaymentTerms.TtPartialDeposit,
            total = 43000m,
        });
        quotation.EnsureSuccessStatusCode();
        var quotationDto = await quotation.Content.ReadFromJsonAsync<QuotationDto>();

        if (accept)
        {
            var accepted = await PostAuthenticatedAsync(
                $"/api/v1/quotations/{quotationDto!.Id}/accept", buyerToken, new { });
            accepted.EnsureSuccessStatusCode();
        }

        return quotationDto!.Id;
    }

    private Task<Guid> SeedAcceptedQuotationAsync(string buyerToken, string supplierToken, string title) =>
        SeedQuotationAsync(buyerToken, supplierToken, title, accept: true);

    /// <summary>Self-registers a buyer (the Buyer role is self-registrable) and returns a token.</summary>
    private async Task<string> RegisterAndLoginAsync(string prefix)
    {
        var email = $"{prefix}@orders.tests.agrotrade.local";
        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = Password,
            displayName = prefix,
            homeCountry = "NG",
            defaultCurrency = "USD",
            roles = new[] { RoleNames.Buyer },
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var registered = await register.Content.ReadFromJsonAsync<AuthResponseDto>();
        return registered!.AccessToken;
    }

    // ---- HTTP helpers ----

    /// <summary>Logs in the seeded test administrator (used to run the verification queue).</summary>
    private Task<string> AdminTokenAsync() =>
        LoginAsync(TestingWebAppFactory.AdminEmail, TestingWebAppFactory.AdminPassword);

    private Task<HttpResponseMessage> PostAuthenticatedAsync(string url, string token, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> GetAsync(string url, string token) => GetAuthenticatedAsync(url, token);

    private Task<HttpResponseMessage> GetAuthenticatedAsync(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    // ---- DTO mirrors ----

    public sealed class OrderDetailDto
    {
        public Guid Id { get; init; }
        public string OrderNumber { get; init; } = "";
        public Guid? QuotationId { get; init; }
        public Guid? TradeRequestId { get; init; }
        public OrderStatus Status { get; init; }
        public PaymentStatus PaymentStatus { get; init; }
        public string BuyerDisplayName { get; init; } = "";
        public bool IsBuyer { get; init; }
        public bool IsSeller { get; init; }
        public string Currency { get; init; } = "";
        public decimal Subtotal { get; init; }
        public decimal Discount { get; init; }
        public decimal Total { get; init; }
        public decimal AmountPaid { get; init; }
        public decimal BalanceDue { get; init; }
        public Incoterms Incoterms { get; init; }
        public string? PortOfLoading { get; init; }
        public string? PortOfDischarge { get; init; }
        public PaymentTerms PaymentTerms { get; init; }
        public string? DestinationCountry { get; init; }
        public List<OrderItemDto> Items { get; init; } = [];
    }

    public sealed class OrderItemDto
    {
        public decimal Quantity { get; init; }
        public decimal UnitPrice { get; init; }
        public decimal LineTotal { get; init; }
        public string Currency { get; init; } = "";
    }

    public sealed class OrderCardDto
    {
        public Guid Id { get; init; }
        public string OrderNumber { get; init; } = "";
        public OrderStatus Status { get; init; }
    }

    public sealed class QuotationDto
    {
        public Guid Id { get; init; }
    }

    public sealed class RfqDetailDto
    {
        public Guid Id { get; init; }
        public RfqStatus Status { get; init; }
    }

    public sealed class AuthResponseDto
    {
        public string AccessToken { get; init; } = "";
    }

    public sealed class PagedResultDto<T>
    {
        public List<T> Items { get; init; } = [];
    }

    public sealed class ProblemDetailsDto
    {
        public string? Code { get; init; }
    }
}