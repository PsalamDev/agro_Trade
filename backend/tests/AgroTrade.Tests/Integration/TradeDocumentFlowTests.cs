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
/// Trade documents, task 2.3: either party uploads against an order, the file lands in
/// Uploaded, and an admin queue verifies or rejects it (plan §02 — Documents).
/// </summary>
public sealed class TradeDocumentFlowTests : IClassFixture<TestingWebAppFactory>
{
    private const string Password = "StrongPass123";

    private readonly TestingWebAppFactory _factory;
    private readonly HttpClient _client;

    public TradeDocumentFlowTests(TestingWebAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureSchema();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SellerUpload_LandsInUploaded_AndIsVisibleToBothParties()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("upload");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var uploaded = await UploadDocumentAsync(orderId, sellerToken, DocumentType.CommercialInvoice, "INV-2026-0042");
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);

        var document = await uploaded.Content.ReadFromJsonAsync<TradeDocumentDtoMirror>();
        Assert.NotNull(document);
        Assert.Equal(DocumentStatus.Uploaded, document!.Status);
        Assert.Equal(DocumentType.CommercialInvoice, document.DocType);
        Assert.Equal("INV-2026-0042", document.DocumentReference);
        Assert.NotNull(document.FileUrl);
        Assert.NotNull(document.UploadedByDisplayName);

        // Both sides see the same paperwork: a commercial invoice is not a secret from the
        // party being invoiced.
        foreach (var token in new[] { buyerToken, sellerToken })
        {
            var listed = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/documents", token);
            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
            var docs = await listed.Content.ReadFromJsonAsync<List<TradeDocumentDtoMirror>>();
            Assert.Single(docs!);
            Assert.Equal(document.Id, docs![0].Id);
        }
    }

    [Fact]
    public async Task AdminReview_VerifiesDocument_AndRejectionRequiresNotes()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("review");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var adminToken = await AdminTokenAsync();

        var verified = await UploadDocumentAsync(orderId, sellerToken, DocumentType.PackingList, "PL-001");
        var verifiedDoc = await verified.Content.ReadFromJsonAsync<TradeDocumentDtoMirror>();

        var approve = await PostAuthenticatedAsync(
            $"/api/v1/admin/documents/{verifiedDoc!.Id}/review", adminToken,
            new { status = (int)DocumentStatus.Verified });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal(DocumentStatus.Verified, (await approve.Content.ReadFromJsonAsync<TradeDocumentDtoMirror>())!.Status);

        // Rejecting with no explanation leaves the uploader unable to act on the decision.
        var rejectNoNotes = await UploadDocumentAsync(orderId, sellerToken, DocumentType.BillOfLading, "BL-002");
        var rejectDoc = await rejectNoNotes.Content.ReadFromJsonAsync<TradeDocumentDtoMirror>();

        var reject = await PostAuthenticatedAsync(
            $"/api/v1/admin/documents/{rejectDoc!.Id}/review", adminToken,
            new { status = (int)DocumentStatus.Rejected });
        Assert.Equal(HttpStatusCode.BadRequest, reject.StatusCode);

        var withNotes = await PostAuthenticatedAsync(
            $"/api/v1/admin/documents/{rejectDoc.Id}/review", adminToken,
            new { status = (int)DocumentStatus.Rejected, notes = "Missing consignee signature." });
        Assert.Equal(HttpStatusCode.OK, withNotes.StatusCode);
        var rejectedDto = await withNotes.Content.ReadFromJsonAsync<TradeDocumentDtoMirror>();
        Assert.Equal(DocumentStatus.Rejected, rejectedDto!.Status);
        Assert.Equal("Missing consignee signature.", rejectedDto.ReviewNotes);
        Assert.NotNull(rejectedDto.ReviewedAtUtc);

        // Reviewing twice to the same verdict is a conflict, not a silent no-op.
        var again = await PostAuthenticatedAsync(
            $"/api/v1/admin/documents/{rejectedDto.Id}/review", adminToken,
            new { status = (int)DocumentStatus.Rejected, notes = "Again." });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task VerifiedDocument_CannotBeWithdrawn_ButUnverifiedOneCan()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("withdraw");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var adminToken = await AdminTokenAsync();

        var draftish = await UploadDocumentAsync(orderId, buyerToken, DocumentType.ImportPermit, "IP-001");
        var draftishDoc = await draftish.Content.ReadFromJsonAsync<TradeDocumentDtoMirror>();

        var deleted = await DeleteAuthenticatedAsync($"/api/v1/documents/{draftishDoc!.Id}", buyerToken);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var gone = await GetAuthenticatedAsync($"/api/v1/documents/{draftishDoc.Id}", buyerToken);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);

        var verifiedUpload = await UploadDocumentAsync(orderId, sellerToken, DocumentType.CertificateOfOrigin, "CO-002");
        var verifiedDoc = await verifiedUpload.Content.ReadFromJsonAsync<TradeDocumentDtoMirror>();
        var approve = await PostAuthenticatedAsync(
            $"/api/v1/admin/documents/{verifiedDoc!.Id}/review", adminToken,
            new { status = (int)DocumentStatus.Verified });
        approve.EnsureSuccessStatusCode();

        var blocked = await DeleteAuthenticatedAsync($"/api/v1/documents/{verifiedDoc.Id}", sellerToken);
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
    }

    [Fact]
    public async Task Outsider_CannotSeeOrUpload_AndLearnsNothing()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("outsider");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var (strangerToken, _) = await SeedTradeAsync("outsider2");

        // 404, not 403: a third party must not be able to learn that the order exists.
        var listed = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/documents", strangerToken);
        Assert.Equal(HttpStatusCode.NotFound, listed.StatusCode);

        var uploaded = await UploadDocumentAsync(orderId, strangerToken, DocumentType.PhytosanitaryCertificate, "X-1");
        Assert.Equal(HttpStatusCode.NotFound, uploaded.StatusCode);

        // A document on someone else's order is equally invisible, even by direct id.
        var existing = await UploadDocumentAsync(orderId, sellerToken, DocumentType.ExportPermit, "EP-9");
        var existingDoc = await existing.Content.ReadFromJsonAsync<TradeDocumentDtoMirror>();
        var read = await GetAuthenticatedAsync($"/api/v1/documents/{existingDoc!.Id}", strangerToken);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);

        // And the stranger cannot delete it either.
        var deleted = await DeleteAuthenticatedAsync($"/api/v1/documents/{existingDoc.Id}", strangerToken);
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
    }

    [Fact]
    public async Task AdminQueue_FiltersByStatusAndRejectsNonAdminAccess()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("queue");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var adminToken = await AdminTokenAsync();

        var pending = await UploadDocumentAsync(orderId, sellerToken, DocumentType.CommercialInvoice, "Q-INV");
        var pendingDoc = await pending.Content.ReadFromJsonAsync<TradeDocumentDtoMirror>();
        var approved = await UploadDocumentAsync(orderId, buyerToken, DocumentType.ImportPermit, "Q-IMP");
        var approvedDoc = await approved.Content.ReadFromJsonAsync<TradeDocumentDtoMirror>();

        var approve = await PostAuthenticatedAsync(
            $"/api/v1/admin/documents/{approvedDoc!.Id}/review", adminToken,
            new { status = (int)DocumentStatus.Verified });
        approve.EnsureSuccessStatusCode();

        var uploadedOnly = await GetAuthenticatedAsync(
            $"/api/v1/admin/documents?status={(int)DocumentStatus.Uploaded}&pageSize=100", adminToken);
        Assert.Equal(HttpStatusCode.OK, uploadedOnly.StatusCode);
        var uploadedPage = await uploadedOnly.Content.ReadFromJsonAsync<PagedResultDto<TradeDocumentDtoMirror>>();
        Assert.All(uploadedPage!.Items, d => Assert.Equal(DocumentStatus.Uploaded, d.Status));
        Assert.DoesNotContain(uploadedPage.Items, d => d.Id == approvedDoc.Id);
        Assert.Contains(uploadedPage.Items, d => d.Id == pendingDoc!.Id);

        // The queue carries the order context, so an admin can act without a second lookup.
        var withContext = Assert.Single(uploadedPage.Items, d => d.Id == pendingDoc!.Id);
        Assert.NotEmpty(withContext.OrderNumber);
        Assert.NotNull(withContext.UploadedByDisplayName);

        var byType = await GetAuthenticatedAsync(
            $"/api/v1/admin/documents?docType={(int)DocumentType.ImportPermit}&pageSize=100", adminToken);
        Assert.Equal(HttpStatusCode.OK, byType.StatusCode);
        var byTypePage = await byType.Content.ReadFromJsonAsync<PagedResultDto<TradeDocumentDtoMirror>>();
        Assert.All(byTypePage!.Items, d => Assert.Equal(DocumentType.ImportPermit, d.DocType));

        // The queue is admin-only; a signed-in non-admin gets a 403 from the policy.
        var asBuyer = await GetAuthenticatedAsync("/api/v1/admin/documents", buyerToken);
        Assert.Equal(HttpStatusCode.Forbidden, asBuyer.StatusCode);

        var asSeller = await GetAuthenticatedAsync("/api/v1/admin/documents", sellerToken);
        Assert.Equal(HttpStatusCode.Forbidden, asSeller.StatusCode);
    }

    [Fact]
    public async Task Upload_RejectsBadFileTypeMissingTypeAndClosedOrders()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("reject");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        // UploadGuard allowlists MIME types; a script is refused whatever the field says.
        var scriptPart = new ByteArrayContent("#!/bin/sh\necho nope"u8.ToArray());
        scriptPart.Headers.ContentType = new MediaTypeHeaderValue("application/x-sh");
        var badForm = new MultipartFormDataContent
        {
            { new StringContent(((int)DocumentType.CommercialInvoice).ToString()), "docType" },
            { scriptPart, "file", "payload.sh" },
        };
        var badType = await SendFormAsync($"/api/v1/orders/{orderId}/documents", buyerToken, badForm);
        Assert.Equal(HttpStatusCode.BadRequest, badType.StatusCode);

        var noType = await SendFormAsync(
            $"/api/v1/orders/{orderId}/documents", buyerToken,
            new MultipartFormDataContent { { PdfPart(), "file", "invoice.pdf" } });
        Assert.Equal(HttpStatusCode.BadRequest, noType.StatusCode);

        var noDocTypeValue = await SendFormAsync(
            $"/api/v1/orders/{orderId}/documents", buyerToken,
            new MultipartFormDataContent
            {
                { new StringContent("999"), "docType" },
                { PdfPart(), "file", "invoice.pdf" },
            });
        Assert.Equal(HttpStatusCode.BadRequest, noDocTypeValue.StatusCode);

        // Expiry before issue is impossible paperwork.
        var backwards = await SendFormAsync(
            $"/api/v1/orders/{orderId}/documents", buyerToken,
            new MultipartFormDataContent
            {
                { new StringContent(((int)DocumentType.CommercialInvoice).ToString()), "docType" },
                { new StringContent(DateTime.UtcNow.AddDays(-2).ToString("O")), "issuedOnUtc" },
                { new StringContent(DateTime.UtcNow.AddDays(-5).ToString("O")), "expiresOnUtc" },
                { PdfPart(), "file", "invoice.pdf" },
            });
        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);

        // Walk the order to Cancelled, then confirm paperwork is closed.
        var sellerOrderId = await SeedOrderAsync(buyerToken, sellerToken);
        await TransitionAsync(sellerOrderId, sellerToken, OrderStatus.Confirmed, null);
        var cancelled = await TransitionAsync(sellerOrderId, buyerToken, OrderStatus.Cancelled, "Changed our minds.");
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);

        var afterCancel = await UploadDocumentAsync(sellerOrderId, buyerToken, DocumentType.CommercialInvoice, "LATE-1");
        Assert.Equal(HttpStatusCode.BadRequest, afterCancel.StatusCode);
    }

    [Fact]
    public async Task ConcurrentOrderCreation_DoesNotCollideOnTheOrderNumber()
    {
        // Regression test. Numbers are MAX+1, so simultaneous creators used to compute the same
        // one and the loser died with an unhandled unique-index violation (a 500). The suite
        // running five classes in parallel against one database is enough to reproduce it.
        var (buyerToken, sellerToken) = await SeedTradeAsync("concurrent");

        // One RFQ per quotation. Accepting also writes the RFQ row and declines the RFQ's other
        // quotations, so sharing a single RFQ would make these calls conflict with each other
        // for a reason unrelated to order numbering. Each quote here stands alone.
        var quotationIds = new List<Guid>();
        foreach (var i in Enumerable.Range(0, 6))
        {
            var (_, otherSeller) = await SeedTradeAsync($"conc{i}");

            var rfq = await PostAuthenticatedAsync("/api/v1/rfqs", buyerToken, new
            {
                title = $"concurrent {i} {Guid.NewGuid():N}",
                quantity = 1m,
                unit = (int)PriceUnit.PerTon,
                destinationCountry = "NG",
                destinationPortOrCity = "Lagos",
                preferredIncoterm = (int)Incoterms.Cif,
                preferredCurrency = "USD",
                expiresAt = DateTime.UtcNow.AddDays(30),
            });
            Assert.True(rfq.IsSuccessStatusCode,
                $"RFQ create failed for {i} ({(int)rfq.StatusCode}): {await rfq.Content.ReadAsStringAsync()}");
            var rfqDto = await rfq.Content.ReadFromJsonAsync<RfqDetailDto>();

            var quotation = await PostAuthenticatedAsync($"/api/v1/rfqs/{rfqDto!.Id}/quotations", otherSeller, new
            {
                currency = "USD",
                incoterms = (int)Incoterms.Cif,
                portOfLoading = "Mombasa (MBA)",
                portOfDischarge = "Lagos (LOS)",
                leadTimeDays = 14,
                paymentTerms = (int)PaymentTerms.TtPartialDeposit,
                total = 1000m + i,
            });
            quotation.EnsureSuccessStatusCode();
            var quotationId = (await quotation.Content.ReadFromJsonAsync<QuotationDto>())!.Id;

            var accept = await PostAuthenticatedAsync($"/api/v1/quotations/{quotationId}/accept", buyerToken, new { });
            accept.EnsureSuccessStatusCode();

            quotationIds.Add(quotationId);
        }

        var conversions = await Task.WhenAll(quotationIds.Select(q =>
            PostAuthenticatedAsync("/api/v1/orders/from-quotation", buyerToken, new
            {
                quotationId = q,
                placeImmediately = true,
            })));

        var numbers = new List<string>();
        foreach (var response in conversions)
        {
            // The body is in the message because a lost race surfaces as an opaque 500, and
            // knowing that is what tells you the allocator regressed.
            Assert.True(
                response.IsSuccessStatusCode,
                $"Convert failed ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");

            numbers.Add((await response.Content.ReadFromJsonAsync<OrderDetailMirror>())!.OrderNumber);
        }

        // Every number allocated, and none handed out twice.
        Assert.Equal(numbers.Count, numbers.Distinct().Count());
        Assert.Equal(6, numbers.Count);
    }

    // ---- Fixtures ----

    /// <summary>
    /// Creates a seller and a buyer. The Company role is granted out of band (it is not
    /// self-registrable), and the seller's business must be verified before it can quote, so
    /// the real upload/submit/review queue is walked rather than shortcut.
    /// </summary>
    private async Task<(string BuyerToken, string SupplierToken)> SeedTradeAsync(string tag)
    {
        var sellerToken = await CreateCompanyUserAsync($"docs_seller_{tag}_{Guid.NewGuid():N}");

        var registered = await PostAuthenticatedAsync("/api/v1/businesses", sellerToken, new
        {
            legalName = $"Docs Supplier {tag} Ltd",
            tradingName = $"Docs Supplier {tag}",
            country = "NG",
            businessType = (int)BusinessType.LimitedCompany,
        });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        var businessId = await SupplierBusinessIdAsync(sellerToken);

        var uploadRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/businesses/{businessId}/documents")
        {
            Content = new MultipartFormDataContent
            {
                { new StringContent(((int)DocumentType.BusinessRegistration).ToString()), "docType" },
                { PdfPart(), "file", "certificate.pdf" },
            },
        };
        uploadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);
        var uploaded = await _client.SendAsync(uploadRequest);
        Assert.True(uploaded.IsSuccessStatusCode,
            $"Business document upload failed ({(int)uploaded.StatusCode}): {await uploaded.Content.ReadAsStringAsync()}");

        var submitRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/businesses/{businessId}/verification-submissions");
        submitRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);
        var submitted = await _client.SendAsync(submitRequest);
        submitted.EnsureSuccessStatusCode();

        var adminToken = await AdminTokenAsync();
        var review = await PostAuthenticatedAsync(
            $"/api/v1/admin/businesses/{businessId}/verification/review", adminToken, new { approve = true });
        review.EnsureSuccessStatusCode();

        var buyerToken = await RegisterAndLoginAsync($"docs_buyer_{tag}_{Guid.NewGuid():N}");
        return (buyerToken, sellerToken);
    }

    private async Task<Guid> SeedOrderAsync(string buyerToken, string sellerToken)
    {
        var rfq = await PostAuthenticatedAsync("/api/v1/rfqs", buyerToken, new
        {
            title = $"docs trade {Guid.NewGuid():N}",
            quantity = 5m,
            unit = (int)PriceUnit.PerTon,
            destinationCountry = "NG",
            destinationPortOrCity = "Lagos",
            preferredIncoterm = (int)Incoterms.Cif,
            preferredCurrency = "USD",
            expiresAt = DateTime.UtcNow.AddDays(30),
        });
        rfq.EnsureSuccessStatusCode();
        var rfqDto = await rfq.Content.ReadFromJsonAsync<RfqDetailDto>();

        var quotation = await PostAuthenticatedAsync($"/api/v1/rfqs/{rfqDto!.Id}/quotations", sellerToken, new
        {
            currency = "USD",
            incoterms = (int)Incoterms.Cif,
            portOfLoading = "Mombasa (MBA)",
            portOfDischarge = "Lagos (LOS)",
            leadTimeDays = 14,
            paymentTerms = (int)PaymentTerms.TtPartialDeposit,
            total = 12000m,
        });
        quotation.EnsureSuccessStatusCode();
        var quotationDto = await quotation.Content.ReadFromJsonAsync<QuotationDto>();

        var accept = await PostAuthenticatedAsync(
            $"/api/v1/quotations/{quotationDto!.Id}/accept", buyerToken, new { });
        accept.EnsureSuccessStatusCode();

        var order = await PostAuthenticatedAsync("/api/v1/orders/from-quotation", buyerToken, new
        {
            quotationId = quotationDto.Id,
            placeImmediately = true,
        });
        Assert.Equal(HttpStatusCode.Created, order.StatusCode);
        return (await order.Content.ReadFromJsonAsync<OrderDetailMirror>())!.Id;
    }

    private Task<HttpResponseMessage> TransitionAsync(Guid orderId, string token, OrderStatus to, string? reason)
    {
        var body = reason is null
            ? (object)new { to = (int)to }
            : new { to = (int)to, reason };
        return PostAuthenticatedAsync($"/api/v1/orders/{orderId}/transitions", token, body);
    }

    /// <summary>Each multipart part needs its own Content-Type; the guard reads the file part's.</summary>
    private static ByteArrayContent PdfPart()
    {
        var part = new ByteArrayContent("%PDF-1.4 trade document"u8.ToArray());
        part.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return part;
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
        return SendFormAsync($"/api/v1/orders/{orderId}/documents", token, form);
    }

    private Task<HttpResponseMessage> SendFormAsync(string url, string token, MultipartFormDataContent form)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private async Task<string> CreateCompanyUserAsync(string prefix)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = $"{prefix}@docs.tests.agrotrade.local",
            Email = $"{prefix}@docs.tests.agrotrade.local",
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
            email = $"{prefix}@docs.tests.agrotrade.local",
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
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { email, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!.AccessToken;
    }

    private Task<string> AdminTokenAsync() =>
        LoginAsync(TestingWebAppFactory.AdminEmail, TestingWebAppFactory.AdminPassword);

    private async Task<Guid> SupplierBusinessIdAsync(string token)
    {
        var response = await GetAuthenticatedAsync("/api/v1/businesses/mine", token);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var first = root.ValueKind == JsonValueKind.Array ? root[0] : root.GetProperty("items")[0];
        return first.GetProperty("id").GetGuid();
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

    private Task<HttpResponseMessage> DeleteAuthenticatedAsync(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    // ---- DTO mirrors ----

    public sealed class TradeDocumentDtoMirror
    {
        public Guid Id { get; init; }
        public Guid OrderId { get; init; }
        public DocumentType DocType { get; init; }
        public string? DocumentReference { get; init; }
        public string? FileUrl { get; init; }
        public string? FileName { get; init; }
        public long? FileSizeBytes { get; init; }
        public string? ContentType { get; init; }
        public DocumentStatus Status { get; init; }
        public string? ReviewNotes { get; init; }
        public DateTime? ReviewedAtUtc { get; init; }
        public DateTime? IssuedOnUtc { get; init; }
        public DateTime? ExpiresOnUtc { get; init; }
        public string OrderNumber { get; init; } = "";
        public string? OrderCounterparty { get; init; }
        public Guid? UploadedByUserId { get; init; }
        public string? UploadedByDisplayName { get; init; }
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

    public sealed class PagedResultDto<T>
    {
        public List<T> Items { get; init; } = [];
    }
}