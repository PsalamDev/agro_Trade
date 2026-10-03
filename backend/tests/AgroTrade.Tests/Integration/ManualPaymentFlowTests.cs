using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AgroTrade.Domain.Enums;
using AgroTrade.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgroTrade.Tests.Integration;

/// <summary>
/// Off-platform payment recording and the transaction ledger, task 2.5: the seller records a bank
/// transfer that has already landed, and the ledger holds one immutable row per money movement
/// (plan §02 — Payments, Transactions).
///
/// <para>
/// The load-bearing claim here is that a manual payment is recorded as <i>already settled</i>. A
/// pending manual payment would be a record that exists only to be updated, and the balance
/// figure the buyer sees would be fiction until somebody remembered to finish it.
/// </para>
/// </summary>
public sealed class ManualPaymentFlowTests : IClassFixture<TestingWebAppFactory>
{
    private const string Password = "StrongPass123";

    private readonly TestingWebAppFactory _factory;
    private readonly HttpClient _client;

    public ManualPaymentFlowTests(TestingWebAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureSchema();
        _client = factory.CreateClient();
    }

    // ---- Recording ----

    [Fact]
    public async Task SellerRecordsABankTransfer_AndTheOrderRollsUpToPaid()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("record");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var response = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 4000m, remittanceReference = "UTR-AAA-001" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payment = await response.Content.ReadFromJsonAsync<PaymentDtoMirror>();
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Paid, payment!.Status);
        Assert.Equal(PaymentProvider.ManualTransfer, payment.Provider);
        Assert.Equal(PaymentMethod.BankTransfer, payment.Method);
        Assert.Equal("UTR-AAA-001", payment.RemittanceReference);
        Assert.Equal("USD", payment.Currency);
        Assert.StartsWith("PAY-", payment.Reference);
        Assert.NotNull(payment.PaidAtUtc);

        // No gateway was involved, so there is nothing for the payer to be redirected to.
        Assert.Null(payment.NextActionUrl);
        Assert.Null(payment.ProviderRef);

        var order = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}", buyerToken);
        Assert.Equal(
            PaymentStatus.PartiallyPaid,
            (await order.Content.ReadFromJsonAsync<OrderPaymentMirror>())!.PaymentStatus);
    }

    [Fact]
    public async Task FullPaymentRollsTheOrderToPaidInFull()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("full");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 12000m, remittanceReference = "UTR-FULL-001" });

        var order = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}", buyerToken);
        Assert.Equal(
            PaymentStatus.Paid,
            (await order.Content.ReadFromJsonAsync<OrderPaymentMirror>())!.PaymentStatus);
    }

    [Fact]
    public async Task DepositThenBalance_SettlesInTwoTraces()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("deposit");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        // The common T/T partial-deposit term: two separate transfers, two separate trace ids.
        var deposit = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 4000m, remittanceReference = "UTR-DEP-001" });
        Assert.Equal(HttpStatusCode.Created, deposit.StatusCode);

        var balance = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 8000m, remittanceReference = "UTR-BAL-001" });
        Assert.Equal(HttpStatusCode.Created, balance.StatusCode);

        Assert.Equal(2, await CountLedgerRowsAsync(orderId));
        var order = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}", buyerToken);
        Assert.Equal(
            PaymentStatus.Paid,
            (await order.Content.ReadFromJsonAsync<OrderPaymentMirror>())!.PaymentStatus);
    }

    [Fact]
    public async Task LetterOfCredit_IsRecordedAsHonoured()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("lc");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var response = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new
            {
                amount = 12000m,
                method = (int)PaymentMethod.LetterOfCredit,
                remittanceReference = "LC-REF-77",
                bankName = "Zenith Bank",
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payment = await response.Content.ReadFromJsonAsync<PaymentDtoMirror>();
        Assert.Equal(PaymentMethod.LetterOfCredit, payment!.Method);
        Assert.Equal(PaymentStatus.Paid, payment.Status);

        var ledger = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/transactions", buyerToken);
        var entries = await ledger.Content.ReadFromJsonAsync<List<TransactionDtoMirror>>();
        Assert.Single(entries!);
        Assert.Contains("Letter of credit", entries![0].Narrative!, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Who may record ----

    [Fact]
    public async Task BuyerCannotRecordAManualPayment()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("buyerattempt");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        // The buyer sent the money, so this is the request that looks reasonable and is exactly
        // the one that must fail: self-attestation is how an order gets marked paid against funds
        // that never arrived.
        var response = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", buyerToken,
            new { amount = 12000m, remittanceReference = "UTR-SELF-001" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await GetLedgerAsync(orderId, buyerToken));
    }

    [Fact]
    public async Task AnOutsiderCannotRecordOrReadPayments()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("outsider");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var outsiderToken = await RegisterAndLoginAsync($"mp_outs_{Guid.NewGuid():N}");

        var write = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", outsiderToken,
            new { amount = 100m, remittanceReference = "UTR-OUT-001" });
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);

        Assert.Empty(await GetLedgerAsync(orderId, sellerToken));
    }

    // ---- Refusals ----

    [Fact]
    public async Task RecordingBeyondTheOutstandingBalance_IsRejected()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("over");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var response = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 50000m, remittanceReference = "UTR-OVER-001" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("AMOUNT_EXCEEDS_BALANCE", await response.Content.ReadAsStringAsync());
        Assert.Empty(await GetLedgerAsync(orderId, sellerToken));
    }

    [Fact]
    public async Task RecordingOnAFullyPaidOrder_IsRejected()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("alreadypaid");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 12000m, remittanceReference = "UTR-PAY-001" });

        var second = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 500m, remittanceReference = "UTR-PAY-002" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("NOTHING_OUTSTANDING", await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AMissingRemittanceReference_IsRejected()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("noref");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        // The entire content of this record is "this trace id arrived". Without one there is
        // nothing to reconcile against and nothing to distinguish it from any other transfer.
        var response = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken, new { amount = 1000m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await GetLedgerAsync(orderId, sellerToken));
    }

    [Fact]
    public async Task ABlankRemittanceReference_IsRejected()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("blankref");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var response = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 1000m, remittanceReference = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AGatewayMethodIsRejected()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("gwmethod");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        // Hand-typing a gateway charge id would invent an identity no charge will ever match, and
        // a webhook could then settle a payment that was never raised.
        var response = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new
            {
                amount = 1000m,
                method = (int)PaymentMethod.Gateway,
                remittanceReference = "UTR-GW-001",
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ANonPositiveAmountIsRejected()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("neg");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        foreach (var amount in new[] { 0m, -500m })
        {
            var response = await PostAuthenticatedAsync(
                $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
                new { amount, remittanceReference = $"UTR-NEG-{amount}" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Empty(await GetLedgerAsync(orderId, sellerToken));
    }

    [Fact]
    public async Task AValueDateInTheFuture_IsRejected()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("future");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        // A value date ahead of now describes money that has not arrived. Recording it anyway
        // puts a ledger entry dated later than today's settlements into a chronological sequence,
        // and the order would read as paid against funds that do not exist yet.
        var response = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new
            {
                amount = 1000m,
                remittanceReference = "UTR-FUT-001",
                valueDateUtc = DateTime.UtcNow.AddDays(3),
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("VALUE_DATE_IN_FUTURE", await response.Content.ReadAsStringAsync());
        Assert.Empty(await GetLedgerAsync(orderId, sellerToken));
    }

    [Fact]
    public async Task AValueDateInThePast_IsAcceptedAndStampedOnTheLedger()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("past");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var valueDate = DateTime.UtcNow.AddDays(-4);
        var response = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new
            {
                amount = 1000m,
                remittanceReference = "UTR-PAST-001",
                valueDateUtc = valueDate,
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payment = await response.Content.ReadFromJsonAsync<PaymentDtoMirror>();

        // The ledger entry is dated when the money landed, not when it was typed in. A week of
        // backdated transfers would otherwise sort in the order they were entered.
        var entries = await GetLedgerAsync(orderId, sellerToken);
        Assert.Single(entries);
        Assert.True(
            Math.Abs((entries[0].OccurredAtUtc - valueDate).TotalMinutes) < 1,
            $"Expected the ledger to carry the value date {valueDate:O}, got {entries[0].OccurredAtUtc:O}.");
        Assert.NotNull(payment!.ValueDateUtc);
    }

    [Fact]
    public async Task RecordingAgainstACancelledOrder_IsRejected()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("cancelled");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        // Placed orders can be cancelled by the buyer before dispatch.
        var cancel = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/transitions", buyerToken,
            new { to = (int)OrderStatus.Cancelled, reason = "Changed our mind." });
        Assert.True(
            cancel.IsSuccessStatusCode,
            $"Cancel failed ({(int)cancel.StatusCode}): {await cancel.Content.ReadAsStringAsync()}");

        var response = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 1000m, remittanceReference = "UTR-CANCEL-001" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("ORDER_CLOSED", await response.Content.ReadAsStringAsync());
        Assert.Empty(await GetLedgerAsync(orderId, sellerToken));
    }

    // ---- Duplicate guards ----

    [Fact]
    public async Task TheSameRemittanceReferenceCannotBeRecordedTwice()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("duperef");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var first = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 4000m, remittanceReference = "UTR-SAME-001" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // The likeliest mistake on a bank reconciliation: a second pass over the statement, or a
        // double-click. Without this the order's paid figure doubles silently.
        var second = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 4000m, remittanceReference = "UTR-SAME-001" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("REMITTANCE_REFERENCE_REUSED", await second.Content.ReadAsStringAsync());

        var entries = await GetLedgerAsync(orderId, sellerToken);
        Assert.Single(entries);
        Assert.Equal(4000m, entries[0].Amount);
    }

    [Fact]
    public async Task TheSameRemittanceReferenceOnADifferentOrder_IsAllowed()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("samescoped");
        var firstOrder = await SeedOrderAsync(buyerToken, sellerToken);
        var secondOrder = await SeedOrderAsync(buyerToken, sellerToken);

        await PostAuthenticatedAsync(
            $"/api/v1/orders/{firstOrder}/payments/manual", sellerToken,
            new { amount = 1000m, remittanceReference = "UTR-SHARED-001" });

        // Trace ids are only unique within a bank, so two different banks can legitimately issue
        // the same string. Uniqueness is scoped per order for that reason.
        var second = await PostAuthenticatedAsync(
            $"/api/v1/orders/{secondOrder}/payments/manual", sellerToken,
            new { amount = 1000m, remittanceReference = "UTR-SHARED-001" });

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task RetryWithTheSameIdempotencyKey_ReturnsTheOriginalPayment()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("retry");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var key = $"manual-{Guid.NewGuid():N}";

        var first = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 4000m, remittanceReference = "UTR-RETRY-001", idempotencyKey = key });
        var firstDto = await first.Content.ReadFromJsonAsync<PaymentDtoMirror>();

        var second = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 4000m, remittanceReference = "UTR-RETRY-001", idempotencyKey = key });
        var secondDto = await second.Content.ReadFromJsonAsync<PaymentDtoMirror>();

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(firstDto!.Id, secondDto!.Id);
        Assert.Single(await GetLedgerAsync(orderId, sellerToken));
    }

    [Fact]
    public async Task IdempotencyKeyReusedForADifferentAmount_IsRejected()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("keyreuse");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var key = $"reuse-{Guid.NewGuid():N}";

        await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 4000m, remittanceReference = "UTR-KR-001", idempotencyKey = key });

        // A guard matching only on the key would return the 4,000 payment for a 6,000 request,
        // hiding a client bug behind a success.
        var second = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 6000m, remittanceReference = "UTR-KR-002", idempotencyKey = key });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Single(await GetLedgerAsync(orderId, sellerToken));
    }

    // ---- The ledger ----

    [Fact]
    public async Task TheLedgerHoldsOneEntryPerMovementWithTheBanksTraceId()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("ledger");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new
            {
                amount = 6000m,
                remittanceReference = "UTR-LEDGER-001",
                bankName = "First Bank",
            });

        var entries = await GetLedgerAsync(orderId, sellerToken);
        var entry = Assert.Single(entries);

        Assert.StartsWith("TXN-", entry.Reference);
        Assert.Equal(TransactionType.Payment, entry.Type);
        Assert.Equal(6000m, entry.Amount);
        Assert.Equal("USD", entry.Currency);

        // The one externally-verifiable detail on a manual transfer. Leaving it null would drop
        // the only thing an auditor could check the entry against.
        Assert.Equal("UTR-LEDGER-001", entry.ExternalReference);
        Assert.Contains("First Bank", entry.Narrative!, StringComparison.Ordinal);
        Assert.Contains("Bank transfer", entry.Narrative!, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(entry.PaymentId);
        Assert.StartsWith("PAY-", entry.PaymentReference);
    }

    [Fact]
    public async Task TheLedgerIsOrderedOldestFirst_AndTiesBreakOnReference()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("ordered");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new
            {
                amount = 1000m,
                remittanceReference = "UTR-LATE-001",
                valueDateUtc = DateTime.UtcNow.AddDays(-1),
            });
        await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new
            {
                amount = 1000m,
                remittanceReference = "UTR-EARLY-001",
                valueDateUtc = DateTime.UtcNow.AddDays(-5),
            });

        var entries = await GetLedgerAsync(orderId, sellerToken);
        Assert.Equal(2, entries.Count);

        // Backdated transfers must sort by when the money moved, not when it was typed in.
        Assert.Equal("UTR-EARLY-001", entries[0].ExternalReference);
        Assert.Equal("UTR-LATE-001", entries[1].ExternalReference);
        Assert.True(entries[0].OccurredAtUtc <= entries[1].OccurredAtUtc);
    }

    [Fact]
    public async Task TheLedgerIsVisibleToBothPartiesAndEmptyForAFreshOrder()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("bothsee");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        Assert.Empty(await GetLedgerAsync(orderId, buyerToken));

        await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 2000m, remittanceReference = "UTR-BOTH-001" });

        // Both parties read the financial record: the buyer needs it to reconcile their statement.
        Assert.Single(await GetLedgerAsync(orderId, buyerToken));
        Assert.Single(await GetLedgerAsync(orderId, sellerToken));
    }

    [Fact]
    public async Task AGatewayPaymentAndAManualPaymentBothAppearInTheLedger()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("mixed");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var gatewayPayment = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments", buyerToken, new { amount = 5000m });
        Assert.Equal(HttpStatusCode.Created, gatewayPayment.StatusCode);
        var gatewayDto = await gatewayPayment.Content.ReadFromJsonAsync<PaymentDtoMirror>();

        var settle = await PostAuthenticatedAsync(
            $"/api/v1/sandbox/payments/{gatewayDto!.Id}/settle", buyerToken,
            new { outcome = "succeeded" });
        Assert.Equal(HttpStatusCode.OK, settle.StatusCode);

        await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 7000m, remittanceReference = "UTR-MIX-001" });

        var entries = await GetLedgerAsync(orderId, buyerToken);
        Assert.Equal(2, entries.Count);

        // Whichever identity the counterparty gave the money, on the same ledger. Two rails, one
        // financial record — which is the point of task 2.5.
        Assert.Contains(entries, e => e.ExternalReference == gatewayDto.ProviderRef);
        Assert.Contains(entries, e => e.ExternalReference == "UTR-MIX-001");
        Assert.Equal(12000m, entries.Sum(e => e.Amount));

        var order = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}", buyerToken);
        Assert.Equal(
            PaymentStatus.Paid,
            (await order.Content.ReadFromJsonAsync<OrderPaymentMirror>())!.PaymentStatus);
    }

    // ---- Notifications ----

    [Fact]
    public async Task RecordingNotifiesTheBuyer_ButNotTheSellerWhoRecordedIt()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("notify");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var buyerBefore = await NotificationCountAsync(buyerToken);
        var sellerBefore = await NotificationCountAsync(sellerToken);

        await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 3000m, remittanceReference = "UTR-NOTIFY-001" });

        // The buyer is the party that sent the money and now needs to know it landed.
        Assert.True(
            await NotificationCountAsync(buyerToken) > buyerBefore,
            "The buyer was not notified that the seller recorded a payment.");

        // Telling the seller about the action they just performed is noise, not information.
        Assert.Equal(sellerBefore, await NotificationCountAsync(sellerToken));
    }

    // ---- Seeding ----

    private async Task<(string BuyerToken, string SellerToken)> SeedTradeAsync(string tag)
    {
        var sellerToken = await VerifyBusinessAsync(
            await CreateCompanyUserAsync($"mp_seller_{tag}_{Guid.NewGuid():N}"));

        var buyerToken = await RegisterAndLoginAsync($"mp_buyer_{tag}_{Guid.NewGuid():N}");
        return (buyerToken, sellerToken);
    }

    private async Task<Guid> SeedOrderAsync(string buyerToken, string sellerToken, string currency = "USD")
    {
        var rfq = await PostAuthenticatedAsync("/api/v1/rfqs", buyerToken, new
        {
            title = $"manual trade {Guid.NewGuid():N}",
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
            legalName = $"Manual Supplier {tag} Ltd",
            tradingName = $"Manual Supplier {tag}",
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

    private async Task<int> CountLedgerRowsAsync(Guid orderId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Data.AgroTradeDbContext>();
        return await db.Transactions.AsNoTracking().CountAsync(t => t.OrderId == orderId);
    }

    private async Task<int> NotificationCountAsync(string token)
    {
        var response = await GetAuthenticatedAsync("/api/v1/me/notifications?pageSize=100", token);
        if (!response.IsSuccessStatusCode)
        {
            return 0;
        }

        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        return root.ValueKind == System.Text.Json.JsonValueKind.Array
            ? root.GetArrayLength()
            : root.GetProperty("items").GetArrayLength();
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
            UserName = $"{prefix}@manual.tests.agrotrade.local",
            Email = $"{prefix}@manual.tests.agrotrade.local",
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
            email = $"{prefix}@manual.tests.agrotrade.local",
            password = Password,
            displayName = prefix,
            homeCountry = "NG",
            defaultCurrency = "USD",
            roles = new[] { RoleNames.Importer },
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