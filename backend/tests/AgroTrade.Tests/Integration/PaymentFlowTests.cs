using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AgroTrade.Domain.Enums;
using AgroTrade.Domain.Trade;
using AgroTrade.Infrastructure.Identity;
using AgroTrade.Infrastructure.Payments;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgroTrade.Tests.Integration;

/// <summary>
/// Payments, task 2.4: the buyer raises a gateway charge, settlement arrives later as a signed
/// webhook, and a redelivered or out-of-order event cannot double-count or undo the payment
/// (plan §02 — Payments).
///
/// <para>
/// The signature is produced by the real <see cref="SandboxPaymentGateway"/> resolved from DI,
/// not by a copy of the algorithm in the test. A test that reimplemented the HMAC would keep
/// passing if the gateway's scheme changed underneath it.
/// </para>
/// </summary>
public sealed class PaymentFlowTests : IClassFixture<TestingWebAppFactory>
{
    private const string Password = "StrongPass123";

    private readonly TestingWebAppFactory _factory;
    private readonly HttpClient _client;
    private readonly SandboxPaymentGateway _sandbox;

    public PaymentFlowTests(TestingWebAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureSchema();
        _client = factory.CreateClient();
        _sandbox = factory.Services.GetRequiredService<SandboxPaymentGateway>();
    }

    // ---- Initiation ----

    [Fact]
    public async Task BuyerInitiatesCharge_ItStaysPendingUntilTheGatewaySettles()
    {
        var (buyerToken, _) = await SeedTradeAsync("initiate");
        var orderId = await SeedOrderAsync(buyerToken);

        var initiated = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments", buyerToken, new { });
        Assert.Equal(HttpStatusCode.Created, initiated.StatusCode);

        var payment = await initiated.Content.ReadFromJsonAsync<PaymentDtoMirror>();
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Pending, payment!.Status);
        Assert.Equal(PaymentProvider.SandboxGateway, payment.Provider);
        Assert.Equal(PaymentMethod.Gateway, payment.Method);
        Assert.Equal(12000m, payment.Amount);
        Assert.Equal("USD", payment.Currency);
        Assert.StartsWith("PAY-", payment.Reference);
        Assert.NotNull(payment.NextActionUrl);

        // A pending charge is not a payment: the order must still read as outstanding, which is
        // the whole reason settlement is asynchronous.
        var order = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}", buyerToken);
        var orderDto = await order.Content.ReadFromJsonAsync<OrderPaymentMirror>();
        Assert.Equal(PaymentStatus.Pending, orderDto!.PaymentStatus);
    }

    [Fact]
    public async Task RetryWithTheSameIdempotencyKey_ReturnsTheOriginalCharge()
    {
        var (buyerToken, _) = await SeedTradeAsync("retry");
        var orderId = await SeedOrderAsync(buyerToken);
        var key = $"retry-{Guid.NewGuid():N}";

        var first = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments", buyerToken, new { idempotencyKey = key });
        var firstDto = await first.Content.ReadFromJsonAsync<PaymentDtoMirror>();

        var second = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments", buyerToken, new { idempotencyKey = key });
        var secondDto = await second.Content.ReadFromJsonAsync<PaymentDtoMirror>();

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(firstDto!.Id, secondDto!.Id);
        Assert.Single(await ListPaymentsAsync(orderId, buyerToken));
    }

    [Fact]
    public async Task IdempotencyKeyReusedForADifferentAmount_IsRejected()
    {
        var (buyerToken, _) = await SeedTradeAsync("keyreuse");
        var orderId = await SeedOrderAsync(buyerToken);
        var key = $"reuse-{Guid.NewGuid():N}";

        var first = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments", buyerToken,
            new { amount = 4000m, idempotencyKey = key });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // A retry guard that only matched on the key would silently return the 4,000 payment for
        // a 6,000 request. That hides a client bug behind a success.
        var second = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments", buyerToken,
            new { amount = 6000m, idempotencyKey = key });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task PaymentBeyondTheOutstandingBalance_IsRejected()
    {
        var (buyerToken, _) = await SeedTradeAsync("overpay");
        var orderId = await SeedOrderAsync(buyerToken);

        var response = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments", buyerToken, new { amount = 99999m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await ListPaymentsAsync(orderId, buyerToken));
    }

    [Fact]
    public async Task UnsupportedCurrency_IsRefusedBeforeAPaymentRowExists()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("corridor");
        var orderId = await SeedOrderAsync(buyerToken, currency: "EUR");

        // The sandbox settles USD only. A EUR order must be refused up front rather than raising
        // a charge that would never arrive.
        var response = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments", buyerToken, new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await ListPaymentsAsync(orderId, buyerToken));
        Assert.Contains("CORRIDOR_UNSUPPORTED", await response.Content.ReadAsStringAsync());
        Assert.NotNull(sellerToken);
    }

    [Fact]
    public async Task SellerCannotPayOnAnOrder_AndAThirdPartyGetsNotFound()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("party");
        var orderId = await SeedOrderAsync(buyerToken);

        // The seller is a party to the order but not the payer.
        var sellerAttempt = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments", sellerToken, new { });
        Assert.Equal(HttpStatusCode.NotFound, sellerAttempt.StatusCode);

        var outsiderToken = await RegisterAndLoginAsync($"pay_outsider_{Guid.NewGuid():N}");
        var outsiderRead = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/payments", outsiderToken);

        // 404 rather than 403: a 403 confirms the order exists, which is itself private.
        Assert.Equal(HttpStatusCode.NotFound, outsiderRead.StatusCode);
    }

    // ---- Webhook: signature ----

    [Fact]
    public async Task WebhookWithABadSignature_IsRejected()
    {
        var (buyerToken, _) = await SeedTradeAsync("signature");
        var orderId = await SeedOrderAsync(buyerToken);
        var payment = await InitiateAsync(orderId, buyerToken);

        var envelope = _sandbox.BuildEvent(payment.ProviderRef!, "succeeded", DateTime.UtcNow);

        var bad = await PostWebhookAsync(envelope.Payload, "not-a-signature");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("INVALID_SIGNATURE", await bad.Content.ReadAsStringAsync());

        // Also refused when the body is tampered with but the old signature is replayed, which
        // is the actual attack the signature exists to stop.
        var tampered = envelope.Payload.Replace("payment.succeeded", "payment.failed");
        var replayed = await PostWebhookAsync(tampered, envelope.Signature);
        Assert.Equal(HttpStatusCode.BadRequest, replayed.StatusCode);

        Assert.Equal(PaymentStatus.Pending, (await GetPaymentAsync(payment.Id, buyerToken))!.Status);
    }

    [Fact]
    public async Task WebhookWithNoSignature_IsRejected()
    {
        var (buyerToken, _) = await SeedTradeAsync("nosig");
        var orderId = await SeedOrderAsync(buyerToken);
        var payment = await InitiateAsync(orderId, buyerToken);

        var envelope = _sandbox.BuildEvent(payment.ProviderRef!, "succeeded", DateTime.UtcNow);
        var response = await PostWebhookAsync(envelope.Payload, signature: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task WebhookBodyInCamelCase_IsAccepted()
    {
        var (buyerToken, _) = await SeedTradeAsync("casing");
        var orderId = await SeedOrderAsync(buyerToken);
        var payment = await InitiateAsync(orderId, buyerToken);

        // Hand-written camelCase, the way every real gateway posts. Parsing this with
        // case-sensitive defaults would yield nulls and reject a perfectly good event as
        // malformed, so it is pinned here rather than left to the sandbox's own casing.
        var body = System.Text.Json.JsonSerializer.Serialize(new
        {
            id = $"evt_casing_{Guid.NewGuid():N}",
            type = "payment.succeeded",
            chargeRef = payment.ProviderRef,
            occurredAtUtc = DateTime.UtcNow,
        });
        var signature = _sandbox.Sign(body);

        var response = await PostWebhookAsync(body, signature);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ack = await response.Content.ReadFromJsonAsync<WebhookAckMirror>();
        Assert.Equal(PaymentWebhookEvent.Outcomes.Applied, ack!.Outcome);
        Assert.Equal(PaymentStatus.Paid, (await GetPaymentAsync(payment.Id, buyerToken))!.Status);
    }

    [Fact]
    public async Task WebhookForAnUnknownCharge_IsAcceptedButNotApplied()
    {
        var envelope = _sandbox.BuildEvent("sbx_does_not_exist", "succeeded", DateTime.UtcNow);

        var response = await PostWebhookAsync(envelope.Payload, envelope.Signature);

        // 200 on purpose. The gateway has nothing to fix by retrying, and a 4xx would keep
        // redelivering an event that will never match anything.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ack = await response.Content.ReadFromJsonAsync<WebhookAckMirror>();
        Assert.False(ack!.Duplicate);
        Assert.Equal(PaymentWebhookEvent.Outcomes.UnmatchedPayment, ack.Outcome);
        Assert.Null(ack.PaymentId);
    }

    // ---- Webhook: the three defences ----

    [Fact]
    public async Task RedeliveredEvent_IsDeduplicatedAndCountedOnce()
    {
        var (buyerToken, _) = await SeedTradeAsync("dupe");
        var orderId = await SeedOrderAsync(buyerToken);
        var payment = await InitiateAsync(orderId, buyerToken);

        var envelope = _sandbox.BuildEvent(payment.ProviderRef!, "succeeded", DateTime.UtcNow);

        var first = await PostWebhookAsync(envelope.Payload, envelope.Signature);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstAck = await first.Content.ReadFromJsonAsync<WebhookAckMirror>();
        Assert.False(firstAck!.Duplicate);
        Assert.Equal(PaymentWebhookEvent.Outcomes.Applied, firstAck.Outcome);

        // Same bytes, same id. This is what a provider retrying a timed-out delivery looks like.
        var second = await PostWebhookAsync(envelope.Payload, envelope.Signature);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondAck = await second.Content.ReadFromJsonAsync<WebhookAckMirror>();
        Assert.True(secondAck!.Duplicate);

        var settled = await GetPaymentAsync(payment.Id, buyerToken);
        Assert.Equal(PaymentStatus.Paid, settled!.Status);

        // One payment row and one ledger entry, however many deliveries arrived.
        Assert.Single(await ListPaymentsAsync(orderId, buyerToken));
        Assert.Equal(1, await CountLedgerRowsAsync(payment.Id));

        var order = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}", buyerToken);
        Assert.Equal(PaymentStatus.Paid, (await order.Content.ReadFromJsonAsync<OrderPaymentMirror>())!.PaymentStatus);
    }

    [Fact]
    public async Task ASucceededEventCannotBeUndoneByALaterFailingOne()
    {
        var (buyerToken, _) = await SeedTradeAsync("order");
        var orderId = await SeedOrderAsync(buyerToken);
        var payment = await InitiateAsync(orderId, buyerToken);

        var succeededAt = DateTime.UtcNow;
        var success = _sandbox.BuildEvent(payment.ProviderRef!, "succeeded", succeededAt);
        await PostWebhookAsync(success.Payload, success.Signature);

        // A failure that occurred before the success but arrives after it. Applying it would
        // un-settle money the platform already told both parties it had received.
        var staleFailure = _sandbox.BuildEvent(
            payment.ProviderRef!, "failed", succeededAt.AddMinutes(-5), "Issuer declined.");
        var response = await PostWebhookAsync(staleFailure.Payload, staleFailure.Signature);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ack = await response.Content.ReadFromJsonAsync<WebhookAckMirror>();
        Assert.Equal(PaymentWebhookEvent.Outcomes.Stale, ack!.Outcome);

        var settled = await GetPaymentAsync(payment.Id, buyerToken);
        Assert.Equal(PaymentStatus.Paid, settled!.Status);
        Assert.Null(settled.FailureReason);
        Assert.Equal(1, await CountLedgerRowsAsync(payment.Id));
    }

    [Fact]
    public async Task ASettledPaymentIsNotUnsettledByANewerFailingEvent()
    {
        var (buyerToken, _) = await SeedTradeAsync("terminal");
        var orderId = await SeedOrderAsync(buyerToken);
        var payment = await InitiateAsync(orderId, buyerToken);

        var settledAt = DateTime.UtcNow;
        var success = _sandbox.BuildEvent(payment.ProviderRef!, "succeeded", settledAt);
        await PostWebhookAsync(success.Payload, success.Signature);

        // Newest word by timestamp, so the ordering check waves it through. It is still wrong to
        // apply: money that settled on a card rail does not un-settle, and a gateway that really
        // clawed it back is describing a refund, which needs a compensating ledger entry. Letting
        // this through would flip the payment to Failed while the ledger kept claiming the
        // settlement — the order's paid figure and the financial record, disagreeing permanently
        // and invisibly.
        var newerFailure = _sandbox.BuildEvent(
            payment.ProviderRef!, "failed", settledAt.AddMinutes(30), "Reversed.");
        var response = await PostWebhookAsync(newerFailure.Payload, newerFailure.Signature);

        // 200: the gateway has nothing to fix by retrying an event we have deliberately declined.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ack = await response.Content.ReadFromJsonAsync<WebhookAckMirror>();
        Assert.Equal(PaymentWebhookEvent.Outcomes.SupersedesSettlement, ack!.Outcome);
        Assert.Equal(PaymentStatus.Paid.ToString(), ack.Status);

        var settled = await GetPaymentAsync(payment.Id, buyerToken);
        Assert.Equal(PaymentStatus.Paid, settled!.Status);
        Assert.Null(settled.FailureReason);

        // Exactly one ledger row, still describing a real settlement, and the order still counts
        // the money. The whole point: these two figures must never disagree.
        Assert.Equal(1, await CountLedgerRowsAsync(payment.Id));
        var order = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}", buyerToken);
        Assert.Equal(
            PaymentStatus.Paid,
            (await order.Content.ReadFromJsonAsync<OrderPaymentMirror>())!.PaymentStatus);
    }

    [Fact]
    public async Task TheDeclinedSettlementIsStillRecordedForAudit()
    {
        var (buyerToken, _) = await SeedTradeAsync("terminalaudit");
        var orderId = await SeedOrderAsync(buyerToken);
        var payment = await InitiateAsync(orderId, buyerToken);

        var settledAt = DateTime.UtcNow;
        var success = _sandbox.BuildEvent(payment.ProviderRef!, "succeeded", settledAt);
        await PostWebhookAsync(success.Payload, success.Signature);

        var newerFailure = _sandbox.BuildEvent(
            payment.ProviderRef!, "failed", settledAt.AddMinutes(10), "Reversed.");
        await PostWebhookAsync(newerFailure.Payload, newerFailure.Signature);

        // Refusing to act on an event is not the same as pretending it never arrived. If a real
        // gateway really does report a reversal, this row is the evidence that it said so.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<AgroTrade.Infrastructure.Data.AgroTradeDbContext>();

        var declined = await db.PaymentWebhookEvents.AsNoTracking()
            .SingleAsync(e => e.ExternalEventId == newerFailure.EventId);

        Assert.Equal(PaymentWebhookEvent.Outcomes.SupersedesSettlement, declined.Outcome);
        Assert.Equal(payment.Id, declined.PaymentId);
        Assert.NotNull(declined.ProcessedAtUtc);
    }

    [Fact]
    public async Task ARetryRepairsAPaymentWhoseLedgerRowWasNeverWritten()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("repair");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var key = $"repair-{Guid.NewGuid():N}";

        var first = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 4000m, remittanceReference = "UTR-REPAIR-001", idempotencyKey = key });
        var payment = await first.Content.ReadFromJsonAsync<PaymentDtoMirror>();

        // Simulate the torn commit: the payment row is durable, its ledger entry is not. This is
        // reachable because PAY- and TXN- references come from two sequences with two
        // transactions, so the two writes cannot be atomic.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<AgroTrade.Infrastructure.Data.AgroTradeDbContext>();
            await db.Transactions.Where(t => t.PaymentId == payment!.Id).ExecuteDeleteAsync();
        }

        Assert.Equal(0, await CountLedgerRowsAsync(payment!.Id));

        // The retry is the repair. Without it the order would keep a settled payment with no
        // financial record behind it, permanently and invisibly.
        var retry = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 4000m, remittanceReference = "UTR-REPAIR-001", idempotencyKey = key });
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);

        Assert.Equal(1, await CountLedgerRowsAsync(payment.Id));
        var entries = await GetLedgerAsync(orderId, sellerToken);
        Assert.Equal("UTR-REPAIR-001", Assert.Single(entries).ExternalReference);

        // And the repair is not itself a second payment.
        Assert.Equal(1, await PaymentCountAsync(orderId));
    }

    [Fact]
    public async Task AnOrdinaryReplayWritesNoSecondLedgerRow()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("replay");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var key = $"replay-{Guid.NewGuid():N}";

        var first = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
            new { amount = 4000m, remittanceReference = "UTR-REPLAY-001", idempotencyKey = key });
        var payment = await first.Content.ReadFromJsonAsync<PaymentDtoMirror>();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var response = await PostAuthenticatedAsync(
                $"/api/v1/orders/{orderId}/payments/manual", sellerToken,
                new { amount = 4000m, remittanceReference = "UTR-REPLAY-001", idempotencyKey = key });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        // The repair on the replay path must be a no-op when nothing is broken, or a client that
        // retries out of caution would quietly duplicate its own ledger.
        Assert.Equal(1, await CountLedgerRowsAsync(payment!.Id));
        Assert.Equal(1, await PaymentCountAsync(orderId));
    }

    [Fact]
    public async Task StaleEventCarryingANonUtcOffset_IsStillRecognisedAsStale()
    {
        var (buyerToken, _) = await SeedTradeAsync("offset");
        var orderId = await SeedOrderAsync(buyerToken);
        var payment = await InitiateAsync(orderId, buyerToken);

        var settledAt = DateTime.UtcNow;

        var success = _sandbox.BuildEvent(payment.ProviderRef!, "succeeded", settledAt);
        await PostWebhookAsync(success.Payload, success.Signature);

        // A gateway in a +01:00 zone reports an event that really happened before the success.
        // Expressed with its offset it carries wall-clock ticks an hour ahead, so a comparison
        // on raw ticks — which ignores DateTime.Kind — would rank it as the newer event and let
        // a failure undo a payment the platform has already reported as received.
        var staleInstant = settledAt.AddMinutes(-20);
        var offset = TimeSpan.FromHours(1);
        var localWallClock = new DateTimeOffset(staleInstant.ToLocalTime()).ToOffset(offset);

        var body = System.Text.Json.JsonSerializer.Serialize(new
        {
            id = $"evt_offset_{Guid.NewGuid():N}",
            type = "payment.failed",
            chargeRef = payment.ProviderRef,
            occurredAtUtc = localWallClock.ToString("o"),
            failureReason = "Declined in an earlier window.",
        });

        var response = await PostWebhookAsync(body, _sandbox.Sign(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ack = await response.Content.ReadFromJsonAsync<WebhookAckMirror>();
        Assert.Equal(PaymentWebhookEvent.Outcomes.Stale, ack!.Outcome);

        var settled = await GetPaymentAsync(payment.Id, buyerToken);
        Assert.Equal(PaymentStatus.Paid, settled!.Status);
        Assert.Equal(1, await CountLedgerRowsAsync(payment.Id));
    }

    [Fact]
    public async Task FailedCharge_LeavesTheOrderOutstandingAndIsNotInTheLedger()
    {
        var (buyerToken, _) = await SeedTradeAsync("failed");
        var orderId = await SeedOrderAsync(buyerToken);
        var payment = await InitiateAsync(orderId, buyerToken);

        var failure = _sandbox.BuildEvent(
            payment.ProviderRef!, "failed", DateTime.UtcNow, "The issuer declined the charge.");
        await PostWebhookAsync(failure.Payload, failure.Signature);

        var settled = await GetPaymentAsync(payment.Id, buyerToken);
        Assert.Equal(PaymentStatus.Failed, settled!.Status);
        Assert.Equal("The issuer declined the charge.", settled.FailureReason);

        // A failed charge is not money in, so it must not appear in the ledger or move the
        // order's paid figure.
        Assert.Equal(0, await CountLedgerRowsAsync(payment.Id));
        var order = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}", buyerToken);
        Assert.Equal(PaymentStatus.None, (await order.Content.ReadFromJsonAsync<OrderPaymentMirror>())!.PaymentStatus);
    }

    [Fact]
    public async Task PartialPayment_LeavesTheOrderPartiallyPaid()
    {
        var (buyerToken, _) = await SeedTradeAsync("partial");
        var orderId = await SeedOrderAsync(buyerToken);

        var payment = await InitiateAsync(orderId, buyerToken, new { amount = 5000m });
        Assert.Equal(5000m, payment.Amount);

        var success = _sandbox.BuildEvent(payment.ProviderRef!, "succeeded", DateTime.UtcNow);
        await PostWebhookAsync(success.Payload, success.Signature);

        var order = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}", buyerToken);
        Assert.Equal(
            PaymentStatus.PartiallyPaid,
            (await order.Content.ReadFromJsonAsync<OrderPaymentMirror>())!.PaymentStatus);

        // The remaining 7,000 is still payable, so a second charge is allowed.
        var rest = await InitiateAsync(orderId, buyerToken, new { amount = 7000m });
        Assert.Equal(7000m, rest.Amount);
    }

    [Fact]
    public async Task SecondAttemptOnAnAlreadyFailedCharge_IsAllowed()
    {
        var (buyerToken, _) = await SeedTradeAsync("retry_failed");
        var orderId = await SeedOrderAsync(buyerToken);

        var failed = await InitiateAsync(orderId, buyerToken);
        var failure = _sandbox.BuildEvent(failed.ProviderRef!, "failed", DateTime.UtcNow, "Declined.");
        await PostWebhookAsync(failure.Payload, failure.Signature);

        // The first charge failed, so the buyer must be able to try again rather than being
        // locked out by a dead payment on the order.
        var retry = await InitiateAsync(orderId, buyerToken);
        Assert.NotEqual(failed.Id, retry.Id);
        Assert.Equal(PaymentStatus.Pending, retry.Status);
    }

    // ---- Sandbox checkout ----

    [Fact]
    public async Task SandboxSettlement_SettlesThroughTheRealWebhookPath()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("settle");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var payment = await InitiateAsync(orderId, buyerToken);

        var settle = await PostAuthenticatedAsync(
            $"/api/v1/sandbox/payments/{payment.Id}/settle", buyerToken, new { outcome = "succeeded" });

        Assert.Equal(HttpStatusCode.OK, settle.StatusCode);
        var settled = await settle.Content.ReadFromJsonAsync<PaymentDtoMirror>();
        Assert.Equal(PaymentStatus.Paid, settled!.Status);
        Assert.NotNull(settled.PaidAtUtc);
        Assert.Equal(1, await CountLedgerRowsAsync(payment.Id));

        // Both parties can see it, and the seller is told. Being paid is the commercially
        // important half of this event.
        var order = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}", sellerToken);
        Assert.Equal(HttpStatusCode.OK, order.StatusCode);
        Assert.Equal(
            PaymentStatus.Paid,
            (await order.Content.ReadFromJsonAsync<OrderPaymentMirror>())!.PaymentStatus);
    }

    [Fact]
    public async Task SettlingTwice_IsRejected()
    {
        var (buyerToken, _) = await SeedTradeAsync("settle2");
        var orderId = await SeedOrderAsync(buyerToken);
        var payment = await InitiateAsync(orderId, buyerToken);

        var first = await PostAuthenticatedAsync(
            $"/api/v1/sandbox/payments/{payment.Id}/settle", buyerToken, new { outcome = "succeeded" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await PostAuthenticatedAsync(
            $"/api/v1/sandbox/payments/{payment.Id}/settle", buyerToken, new { outcome = "failed" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        // Still paid, and still counted once.
        Assert.Equal(PaymentStatus.Paid, (await GetPaymentAsync(payment.Id, buyerToken))!.Status);
        Assert.Equal(1, await CountLedgerRowsAsync(payment.Id));
    }

    [Fact]
    public async Task SettlingIsVisibleToBothPartiesButNotToAnOutsider()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("settle3");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var payment = await InitiateAsync(orderId, buyerToken);

        await PostAuthenticatedAsync(
            $"/api/v1/sandbox/payments/{payment.Id}/settle", buyerToken, new { outcome = "succeeded" });

        foreach (var token in new[] { buyerToken, sellerToken })
        {
            var read = await GetAuthenticatedAsync($"/api/v1/payments/{payment.Id}", token);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.Equal(PaymentStatus.Paid, (await read.Content.ReadFromJsonAsync<PaymentDtoMirror>())!.Status);
        }

        var outsiderToken = await RegisterAndLoginAsync($"pay_outs_{Guid.NewGuid():N}");
        var outsiderRead = await GetAuthenticatedAsync($"/api/v1/payments/{payment.Id}", outsiderToken);
        Assert.Equal(HttpStatusCode.NotFound, outsiderRead.StatusCode);
    }

    [Fact]
    public async Task ConcurrentUpdatesToOnePayment_CannotOverwriteEachOther()
    {
        var (buyerToken, _) = await SeedTradeAsync("rowver");
        var orderId = await SeedOrderAsync(buyerToken);
        var payment = await InitiateAsync(orderId, buyerToken);

        using var scopeA = _factory.Services.CreateScope();
        using var scopeB = _factory.Services.CreateScope();
        var dbA = scopeA.ServiceProvider
            .GetRequiredService<AgroTrade.Infrastructure.Data.AgroTradeDbContext>();
        var dbB = scopeB.ServiceProvider
            .GetRequiredService<AgroTrade.Infrastructure.Data.AgroTradeDbContext>();

        // Two writers read the same payment at the same version — the situation two gateway events
        // arriving together create. Without the rowversion the second write silently clobbers the
        // first, which for status is how a settled payment could flip to Failed while the ledger
        // kept its settlement.
        var payA = await dbA.Payments.SingleAsync(p => p.Id == payment.Id);
        var payB = await dbB.Payments.SingleAsync(p => p.Id == payment.Id);

        payA.Notes = "first writer";
        await dbA.SaveChangesAsync();

        payB.Notes = "second writer";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync());
    }

    // ---- Seeding ----

    private async Task<(string BuyerToken, string SellerToken)> SeedTradeAsync(string tag)
    {
        var sellerToken = await VerifyBusinessAsync(
            await CreateCompanyUserAsync($"pay_seller_{tag}_{Guid.NewGuid():N}"));

        var buyerToken = await RegisterAndLoginAsync($"pay_buyer_{tag}_{Guid.NewGuid():N}");
        return (buyerToken, sellerToken);
    }

    /// <summary>
    /// Registers a business for this user and drives it through verification, which a seller must
    /// clear before it can quote.
    /// </summary>
    private async Task<string> VerifyBusinessAsync(string token)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];

        var registered = await PostAuthenticatedAsync("/api/v1/businesses", token, new
        {
            legalName = $"Pay Supplier {tag} Ltd",
            tradingName = $"Pay Supplier {tag}",
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

    private async Task<Guid> SeedOrderAsync(string buyerToken, string? sellerToken = null, string currency = "USD")
    {
        // A caller that already has a verified seller passes one in; otherwise a fresh seller is
        // created and verified here. Registering a second business for an existing seller would
        // fail, so the two paths must not be confused.
        sellerToken ??= await VerifyBusinessAsync(
            await CreateCompanyUserAsync($"pay_supp_{Guid.NewGuid():N}"));

        var rfq = await PostAuthenticatedAsync("/api/v1/rfqs", buyerToken, new
        {
            title = $"payment trade {Guid.NewGuid():N}",
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

    private async Task<PaymentDtoMirror> InitiateAsync(Guid orderId, string token, object? body = null)
    {
        var response = await PostAuthenticatedAsync($"/api/v1/orders/{orderId}/payments", token, body ?? new { });
        Assert.True(response.IsSuccessStatusCode,
            $"Payment initiation failed ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<PaymentDtoMirror>())!;
    }

    private async Task<PaymentDtoMirror?> GetPaymentAsync(Guid paymentId, string token)
    {
        var response = await GetAuthenticatedAsync($"/api/v1/payments/{paymentId}", token);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<PaymentDtoMirror>();
    }

    private async Task<List<PaymentDtoMirror>> ListPaymentsAsync(Guid orderId, string token)
    {
        var response = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/payments", token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<PaymentDtoMirror>>() ?? [];
    }

    /// <summary>Counts ledger rows for one payment, read straight from the database.</summary>
    private async Task<int> CountLedgerRowsAsync(Guid paymentId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgroTrade.Infrastructure.Data.AgroTradeDbContext>();
        return await db.Transactions.AsNoTracking().CountAsync(t => t.PaymentId == paymentId);
    }

    private async Task<List<TransactionDtoMirror>> GetLedgerAsync(Guid orderId, string token)
    {
        var response = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/transactions", token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<TransactionDtoMirror>>() ?? [];
    }

    private async Task<int> PaymentCountAsync(Guid orderId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgroTrade.Infrastructure.Data.AgroTradeDbContext>();
        return await db.Payments.AsNoTracking().CountAsync(p => p.OrderId == orderId);
    }

    // ---- Webhook delivery ----

    private Task<HttpResponseMessage> PostWebhookAsync(string payload, string? signature)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, "/api/v1/payments/webhooks/SandboxGateway")
        {
            // The exact bytes signed. Re-serialising here would produce a body whose signature
            // does not match, which is the point.
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

        if (signature is not null)
        {
            request.Headers.Add("X-Sandbox-Signature", signature);
        }

        return _client.SendAsync(request);
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
            UserName = $"{prefix}@pay.tests.agrotrade.local",
            Email = $"{prefix}@pay.tests.agrotrade.local",
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
            email = $"{prefix}@pay.tests.agrotrade.local",
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
        public string OrderNumber { get; init; } = "";
        public string Reference { get; init; } = "";
        public decimal Amount { get; init; }
        public string Currency { get; init; } = "";
        public PaymentProvider Provider { get; init; }
        public PaymentMethod Method { get; init; }
        public PaymentStatus Status { get; init; }
        public string? ProviderRef { get; init; }
        public string? NextActionUrl { get; init; }
        public string? FailureReason { get; init; }
        public DateTime? PaidAtUtc { get; init; }
    }

    public sealed class WebhookAckMirror
    {
        public bool Duplicate { get; init; }
        public string Outcome { get; init; } = "";
        public Guid? PaymentId { get; init; }
        public string? Status { get; init; }
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
