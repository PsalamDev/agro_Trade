using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using AgroTrade.Domain.Enums;
using AgroTrade.Domain.Trade;
using AgroTrade.Infrastructure.Data;
using AgroTrade.Infrastructure.Identity;
using AgroTrade.Infrastructure.Payments;
using AgroTrade.Infrastructure.Trade;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace AgroTrade.Infrastructure.Payments;

/// <summary>
/// Payment initiation and gateway event handling (roadmap §5.5, task 2.4).
/// </summary>
/// <remarks>
/// <para>
/// The interesting half is <see cref="HandleWebhookAsync"/>, because a payment webhook is the
/// least trustworthy input the system has. It arrives unauthenticated in the sense that it
/// carries no session, it may be delivered many times, and it may be delivered out of order.
/// Three defences, in order:
/// </para>
/// <list type="number">
/// <item>
/// <b>Signature.</b> HMAC over the exact received bytes, compared in constant time. Without
/// this, anyone who can reach the URL can mark an invoice paid.
/// </item>
/// <item>
/// <b>Deduplication.</b> A unique index on (Provider, ExternalEventId) turns a redelivery into
/// a failed insert, which is recognised and answered 200 without touching the payment. The 200
/// matters as much as the dedupe: any other code is an instruction to retry.
/// </item>
/// <item>
/// <b>Ordering.</b> An event older than the payment's last applied one has been superseded and
/// is recorded but not applied, so a late <c>payment.failed</c> cannot undo a
/// <c>payment.succeeded</c>.
/// </item>
/// </list>
/// </remarks>
public sealed class PaymentService(
    AgroTradeDbContext db,
    IPaymentGatewayResolver gateways,
    ILogger<PaymentService> logger) : IPaymentService
{
    private const string ReferencePrefixStem = "PAY-";
    private const string TransactionPrefixStem = "TXN-";

    /// <summary>
    /// Shared with the sandbox gateway so both sides agree on the wire format. Case-insensitive
    /// by design, because a provider's casing is not ours to dictate.
    /// </summary>
    internal static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Normalises a provider timestamp to UTC.
    /// </summary>
    /// <remarks>
    /// The ordering decision compares instants, and <see cref="DateTime"/> comparison is a raw
    /// tick comparison that ignores <see cref="DateTime.Kind"/>. A gateway sending "14:00:00+01:00"
    /// therefore reads as an hour later than a genuine "13:00:00Z" — late enough for a stale
    /// <c>payment.failed</c> to overwrite a <c>payment.succeeded</c>, which is exactly what the
    /// ordering check exists to prevent. Converting first makes the comparison mean what it says.
    /// Unspecified is taken as UTC, the convention gateways send.
    /// </remarks>
    private static DateTime? ToUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } => value,
        { Kind: DateTimeKind.Local } => value.Value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
    };

    public async Task<PaymentDto> InitiateAsync(
        Guid orderId, Guid userId, InitiatePaymentRequest request, CancellationToken ct = default)
    {
        var order = await db.Orders
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw ApiException.NotFound("ORDER_NOT_FOUND", "Order not found.");

        // Only the buyer pays. A third party gets 404 rather than 403: the order's existence is
        // itself private, which is the same rule documents and reads use.
        if (order.BuyerUserId != userId)
        {
            throw ApiException.NotFound("ORDER_NOT_FOUND", "Order not found.");
        }

        // A cancelled or completed order will never be fulfilled, so taking money against it
        // would be a promise nobody can keep.
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Completed)
        {
            throw ApiException.Conflict(
                "ORDER_CLOSED",
                order.Status == OrderStatus.Cancelled
                    ? "This order was cancelled, so it cannot take a payment."
                    : "This order is already complete.");
        }

        var idempotencyKey = request.IdempotencyKey?.Trim();

        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            var existing = await db.Payments
                .FirstOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey, ct);

            if (existing is not null)
            {
                // The retry guard is only meaningful if it means the same request. A key reused
                // for a different order or amount is a client bug, and silently returning the
                // old payment would hide it.
                if (existing.OrderId != orderId)
                {
                    throw ApiException.Conflict(
                        "IDEMPOTENCY_KEY_REUSED",
                        "That idempotency key was already used for a different order.");
                }

                if (request.Amount is not null && existing.Amount != request.Amount.Value)
                {
                    throw ApiException.Conflict(
                        "IDEMPOTENCY_KEY_REUSED",
                        "That idempotency key was already used for a different amount.");
                }

                return await ToDtoAsync(existing, ct: ct);
            }
        }

        // Resolved per order rather than assumed, so opening a second corridor is a
        // registration and not a change here.
        var gateway = gateways.Resolve(order.Currency)
            ?? throw ApiException.BadRequest(
                "CORRIDOR_UNSUPPORTED",
                $"No payment gateway settles {order.Currency} yet.");

        var paid = order.Payments.Where(p => p.Status == PaymentStatus.Paid).Sum(p => p.Amount);
        var balanceDue = order.Total - paid;
        var amount = request.Amount ?? balanceDue;

        if (amount <= 0)
        {
            throw ApiException.Conflict(
                "NOTHING_OUTSTANDING",
                "This order is already paid in full.");
        }

        if (amount > balanceDue)
        {
            throw ApiException.BadRequest(
                "AMOUNT_EXCEEDS_BALANCE",
                $"Only {balanceDue:N2} {order.Currency} is outstanding on this order.");
        }

        var payerEmail = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        var payment = new Payment
        {
            OrderId = order.Id,
            Amount = amount,
            Currency = order.Currency,
            Provider = gateway.Provider,
            Method = PaymentMethod.Gateway,
            Status = PaymentStatus.Pending,
            InitiatedAtUtc = DateTime.UtcNow,
            Notes = request.Notes?.Trim(),
            // Generated when the caller supplies none. This covers retries of our own API; a
            // payer who genuinely wants to pay twice sends a different key and gets a second
            // payment, which is the correct outcome rather than a deduplicated mistake.
            IdempotencyKey = string.IsNullOrEmpty(idempotencyKey)
                ? Guid.NewGuid().ToString("N")
                : idempotencyKey,
        };

        // Tracked here so the allocator's save below actually inserts it. A new entity that is
        // never added is not pending, and SaveChanges would succeed having written nothing.
        db.Payments.Add(payment);

        // The reference is allocated before the charge, because the gateway echoes it back on
        // the event and the event is logged against it. The charge is raised with the payment
        // id as the gateway-side idempotency key, so a retry of this call cannot double-charge
        // even if the allocation and the insert end up attempted twice.
        //
        // This insert lands with a null ProviderRef, which the filtered index tolerates; the
        // charge id is written immediately afterwards.
        await SequenceAllocator.AllocateAsync(
            db,
            table: "Payments",
            column: "Reference",
            prefix: ReferencePrefix(),
            assign: reference => payment.Reference = reference,
            save: ct => db.SaveChangesAsync(ct),
            ct);

        var charge = await gateway.CreateChargeAsync(new GatewayChargeRequest
        {
            PaymentId = payment.Id,
            Reference = payment.Reference,
            Amount = payment.Amount,
            Currency = payment.Currency,
            IdempotencyKey = $"charge:{payment.Id:N}",
            PayerEmail = payerEmail,
        }, ct);

        if (charge.Status == GatewayStatuses.Failed)
        {
            // Nothing is recorded. A charge the gateway refused was never in flight, so leaving
            // a pending payment behind would only mislead the order's rollup.
            throw ApiException.BadRequest(
                "CHARGE_REJECTED",
                charge.FailureReason ?? "The payment gateway refused this charge.");
        }

        payment.ProviderRef = charge.ProviderRef;

        // Rolled up at initiation too, not only on settlement. A charge in flight is money the
        // platform is expecting, and an order that still reads as having no payment activity
        // misreports what is happening to it.
        await RollUpOrderAsync(order.Id, ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Payment {Reference} raised against order {OrderId} for {Amount} {Currency} (providerRef {ProviderRef})",
            payment.Reference, orderId, payment.Amount, payment.Currency, payment.ProviderRef);

        return await ToDtoAsync(payment, charge.NextActionUrl, ct);
    }

    public async Task<IReadOnlyList<PaymentDto>> ListForOrderAsync(
        Guid orderId, Guid userId, CancellationToken ct = default)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order is null || await PartyAsync(order, userId, ct) == OrderParty.Outsider)
        {
            throw ApiException.NotFound("ORDER_NOT_FOUND", "Order not found.");
        }

        return await db.Payments
            .AsNoTracking()
            .Where(p => p.OrderId == orderId)
            .OrderByDescending(p => p.CreatedAtUtc ?? p.InitiatedAtUtc)
            .Select(p => ToDto(p, order.OrderNumber))
            .ToListAsync(ct);
    }

    public async Task<PaymentDto> GetAsync(Guid paymentId, Guid userId, CancellationToken ct = default)
    {
        var payment = await db.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct)
            ?? throw ApiException.NotFound("PAYMENT_NOT_FOUND", "Payment not found.");

        var order = await db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == payment.OrderId, ct);

        if (order is null || await PartyAsync(order, userId, ct) == OrderParty.Outsider)
        {
            throw ApiException.NotFound("PAYMENT_NOT_FOUND", "Payment not found.");
        }

        return await ToDtoAsync(payment, ct: ct);
    }

    // ---- Webhook handling ----

    public async Task<WebhookAck> HandleWebhookAsync(
        PaymentProvider provider, string? signature, string rawBody, CancellationToken ct = default)
    {
        var gateway = gateways.ResolveProvider(provider)
            ?? throw ApiException.NotFound("WEBHOOK_UNKNOWN_PROVIDER", "Unknown payment provider.");

        if (!gateway.VerifyWebhook(rawBody, signature))
        {
            // 400, not 401: the caller is not a user of ours and cannot log in, and 401 would
            // invite a credential prompt loop from a provider that will never satisfy it.
            throw ApiException.BadRequest(
                "INVALID_SIGNATURE",
                "The webhook signature did not verify.");
        }

        SandboxWebhookPayload? payload;
        try
        {
            // Web defaults, not the plain default: those are case-sensitive and would only match
            // PascalCase. Every real gateway posts camelCase (Paystack, Stripe and the rest), and
            // under the strict defaults such a body deserialises to nulls and is rejected as
            // malformed — a failure that looks like bad input rather than a casing mismatch.
            // Case-insensitive matching accepts both conventions, which is what an inbound
            // third-party document requires.
            payload = JsonSerializer.Deserialize<SandboxWebhookPayload>(rawBody, WebJson);
        }
        catch (JsonException)
        {
            throw ApiException.BadRequest("WEBHOOK_MALFORMED", "The webhook body could not be read.");
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Id) || string.IsNullOrWhiteSpace(payload.ChargeRef))
        {
            throw ApiException.BadRequest("WEBHOOK_MALFORMED", "The webhook body is missing an id or charge reference.");
        }

        // The idempotency insert. A redelivery collides on the unique index and lands in the
        // catch below, which is the whole mechanism — not a pre-check, because a pre-check
        // races with a concurrent redelivery and both would then apply the event.
        var record = new PaymentWebhookEvent
        {
            Provider = provider,
            ExternalEventId = payload.Id,
            EventType = payload.Type,
            ProviderRef = payload.ChargeRef,
            Payload = rawBody,
            Signature = signature,
            ReceivedAtUtc = DateTime.UtcNow,
            OccurredAtUtc = ToUtc(payload.OccurredAtUtc),
        };

        db.PaymentWebhookEvents.Add(record);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Only the unique index is reachable here in normal operation. Detach and treat as
            // a redelivery: the event is already in the ledger, so the work was done once.
            Detach(record);

            var prior = await db.PaymentWebhookEvents
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Provider == provider && w.ExternalEventId == payload.Id, ct);

            logger.LogInformation(
                "Ignoring redelivered webhook {EventId} from {Provider} (first seen {ReceivedAt:o}, outcome {Outcome})",
                payload.Id, provider, prior?.ReceivedAtUtc, prior?.Outcome ?? "unprocessed");

            return new WebhookAck(true, prior?.Outcome ?? PaymentWebhookEvent.Outcomes.Duplicate, prior?.PaymentId, null);
        }

        return await ApplyAsync(record, payload, ct);
    }

    private async Task<WebhookAck> ApplyAsync(
        PaymentWebhookEvent record, SandboxWebhookPayload payload, CancellationToken ct)
    {
        var payment = await db.Payments
            .FirstOrDefaultAsync(p => p.Provider == record.Provider && p.ProviderRef == record.ProviderRef, ct);

        if (payment is null)
        {
            // A charge we hold no payment for. Answering 200 is deliberate: the gateway has
            // nothing to fix by retrying, and a 4xx would keep redelivering a message that will
            // never match. The event stays in the ledger so the mismatch is visible.
            record.Outcome = PaymentWebhookEvent.Outcomes.UnmatchedPayment;
            record.ProcessedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            logger.LogWarning(
                "Webhook {EventId} names charge {ProviderRef}, which matches no payment",
                record.ExternalEventId, record.ProviderRef);

            return new WebhookAck(false, record.Outcome, null, null);
        }

        record.PaymentId = payment.Id;

        if (payment.LastEventAtUtc is { } last && record.OccurredAtUtc is { } occurred && occurred < last)
        {
            record.Outcome = PaymentWebhookEvent.Outcomes.Stale;
            record.ProcessedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Ignoring stale webhook {EventId} for payment {Reference}: occurred {Occurred:o} is before the last applied {Last:o}",
                record.ExternalEventId, payment.Reference, occurred, last);

            return new WebhookAck(false, record.Outcome, payment.Id, payment.Status.ToString());
        }

        var target = payload.Type switch
        {
            "payment.succeeded" => PaymentStatus.Paid,
            "payment.failed" => PaymentStatus.Failed,
            "payment.cancelled" => PaymentStatus.Failed,
            _ => (PaymentStatus?)null,
        };

        if (target is null)
        {
            record.Outcome = PaymentWebhookEvent.Outcomes.Ignored;
            record.ProcessedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return new WebhookAck(false, record.Outcome, payment.Id, payment.Status.ToString());
        }

        var was = payment.Status;
        payment.Status = target.Value;
        payment.LastEventAtUtc = ToUtc(record.OccurredAtUtc) ?? record.ReceivedAtUtc;

        if (target == PaymentStatus.Paid)
        {
            payment.PaidAtUtc = ToUtc(record.OccurredAtUtc) ?? record.ReceivedAtUtc;
            payment.FailureReason = null;
        }
        else
        {
            payment.FailureReason = payload.FailureReason ?? "The payment gateway declined this charge.";
        }

        record.Outcome = PaymentWebhookEvent.Outcomes.Applied;
        record.ProcessedAtUtc = DateTime.UtcNow;

        await RecordLedgerAsync(payment, ct);

        var order = await RollUpOrderAsync(payment.OrderId, ct);

        await NotifyAsync(payment, order, was, ct);

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Webhook {EventId} applied to payment {Reference}: {Was} -> {Now}",
            record.ExternalEventId, payment.Reference, was, payment.Status);

        return new WebhookAck(false, record.Outcome, payment.Id, payment.Status.ToString());
    }

    /// <summary>
    /// Writes the settlement row to the ledger.
    /// </summary>
    /// <remarks>
    /// Normally this belongs to task 2.5, but a gateway payment that settled without a ledger
    /// entry would leave the order's paid figure derived from a Payment row with no financial
    /// record behind it, which is the gap 2.5 exists to close for the manual path. 2.5
    /// extends the ledger to off-platform transfers; it does not introduce it.
    /// </remarks>
    private async Task RecordLedgerAsync(Payment payment, CancellationToken ct)
    {
        if (payment.Status != PaymentStatus.Paid)
        {
            return;
        }

        var already = await db.Transactions
            .AnyAsync(t => t.PaymentId == payment.Id && t.Type == TransactionType.Payment, ct);

        if (already)
        {
            return;
        }

        var entry = new Transaction
        {
            OrderId = payment.OrderId,
            Type = TransactionType.Payment,
            Amount = payment.Amount,
            Currency = payment.Currency,
            PaymentId = payment.Id,
            OccurredAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow,
            Narrative = $"Payment {payment.Reference} settled via gateway",
            ExternalReference = payment.ProviderRef,
        };

        db.Transactions.Add(entry);

        // Same allocator as orders and RFQs, for the same reason: MAX+1 is racy.
        await SequenceAllocator.AllocateAsync(
            db,
            table: "Transactions",
            column: "Reference",
            prefix: TransactionPrefix(),
            assign: reference => entry.Reference = reference,
            save: ct => db.SaveChangesAsync(ct),
            ct);
    }

    /// <summary>
    /// Recomputes the order's payment status from its payments and returns the order.
    /// </summary>
    /// <remarks>
    /// Paid wins over everything: an order whose balance is fully covered is Paid even if an
    /// unrelated payment failed, because the failure did not stop it being covered. PartiallyPaid
    /// needs a positive settled amount, so a pending charge alone stays Pending.
    /// </remarks>
    private async Task<Order?> RollUpOrderAsync(Guid orderId, CancellationToken ct)
    {
        var order = await db.Orders
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order is null)
        {
            return null;
        }

        var paid = order.Payments.Where(p => p.Status == PaymentStatus.Paid).Sum(p => p.Amount);
        var pending = order.Payments.Any(p => p.Status == PaymentStatus.Pending);

        order.PaymentStatus =
            paid >= order.Total && paid > 0 ? PaymentStatus.Paid
            : paid > 0 ? PaymentStatus.PartiallyPaid
            : pending ? PaymentStatus.Pending
            : PaymentStatus.None;

        return order;
    }

    private async Task NotifyAsync(Payment payment, Order? order, PaymentStatus was, CancellationToken ct)
    {
        if (order is null)
        {
            return;
        }

        var sellerOwnerId = await db.Businesses
            .AsNoTracking()
            .Where(b => b.Id == order.SellerBusinessId)
            .Select(b => (Guid?)b.OwnerUserId)
            .FirstOrDefaultAsync(ct);

        var link = $"/dashboard/orders/{order.Id}";

        if (payment.Status == PaymentStatus.Paid && was != PaymentStatus.Paid)
        {
            // The seller hearing they have been paid is the commercially important half of
            // this event; the buyer already knows they paid.
            if (sellerOwnerId is not null)
            {
                db.Notifications.Add(new Domain.Communication.Notification
                {
                    UserId = sellerOwnerId.Value,
                    Type = NotificationType.Payment,
                    Title = $"Payment received on order {order.OrderNumber}",
                    Body = $"{payment.Reference} settled for {payment.Amount:N2} {payment.Currency}.",
                    Link = link,
                });
            }

            db.Notifications.Add(new Domain.Communication.Notification
            {
                UserId = order.BuyerUserId,
                Type = NotificationType.Payment,
                Title = $"Payment {payment.Reference} received",
                Body = order.PaymentStatus == PaymentStatus.Paid
                    ? $"{order.OrderNumber} is now paid in full."
                    : $"A partial payment of {payment.Amount:N2} {payment.Currency} was received.",
                Link = link,
            });
        }
        else if (payment.Status == PaymentStatus.Failed && was != PaymentStatus.Failed)
        {
            db.Notifications.Add(new Domain.Communication.Notification
            {
                UserId = order.BuyerUserId,
                Type = NotificationType.Payment,
                Title = $"Payment {payment.Reference} failed",
                Body = payment.FailureReason ?? "The gateway declined this charge.",
                Link = link,
            });

            if (sellerOwnerId is not null)
            {
                db.Notifications.Add(new Domain.Communication.Notification
                {
                    UserId = sellerOwnerId.Value,
                    Type = NotificationType.Payment,
                    Title = $"Payment on order {order.OrderNumber} failed",
                    Body = $"{payment.Reference} was declined. The order is still outstanding.",
                    Link = link,
                });
            }
        }
    }

    // ---- Helpers ----

    private async Task<OrderParty> PartyAsync(Order order, Guid userId, CancellationToken ct)
    {
        if (order.BuyerUserId == userId)
        {
            return OrderParty.Buyer;
        }

        return await db.Businesses
            .AsNoTracking()
            .AnyAsync(b => b.Id == order.SellerBusinessId && b.OwnerUserId == userId, ct)
            ? OrderParty.Seller
            : OrderParty.Outsider;
    }

    /// <summary>
    /// Drops a failed entity from the change tracker so a uniqueness collision does not poison
    /// the rest of the unit of work.
    /// </summary>
    private void Detach(PaymentWebhookEvent record)
    {
        foreach (var entry in db.ChangeTracker.Entries<PaymentWebhookEvent>().ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private async Task<PaymentDto> ToDtoAsync(Payment payment, string? nextActionUrl = null, CancellationToken ct = default)
    {
        var orderNumber = await db.Orders
            .AsNoTracking()
            .Where(o => o.Id == payment.OrderId)
            .Select(o => o.OrderNumber)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        return ToDto(payment, orderNumber, nextActionUrl);
    }

    private static PaymentDto ToDto(Payment payment, string orderNumber, string? nextActionUrl = null) => new()
    {
        Id = payment.Id,
        OrderId = payment.OrderId,
        OrderNumber = orderNumber,
        Reference = payment.Reference,
        Amount = payment.Amount,
        Currency = payment.Currency,
        Provider = payment.Provider,
        Method = payment.Method,
        Status = payment.Status,
        ProviderRef = payment.ProviderRef,
        RemittanceReference = payment.RemittanceReference,
        BankName = payment.BankName,
        ValueDateUtc = payment.ValueDateUtc,
        FailureReason = payment.FailureReason,
        Notes = payment.Notes,
        InitiatedAtUtc = payment.InitiatedAtUtc,
        PaidAtUtc = payment.PaidAtUtc,
        NextActionUrl = payment.Status == PaymentStatus.Pending ? nextActionUrl : null,
    };

    private static string ReferencePrefix() =>
        $"{ReferencePrefixStem}{DateTime.UtcNow.Year}-";

    private static string TransactionPrefix() =>
        $"{TransactionPrefixStem}{DateTime.UtcNow.Year}-";
}
