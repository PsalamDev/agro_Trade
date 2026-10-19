using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using AgroTrade.Application.Admin;
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
    ILogger<PaymentService> logger,
    IAuditLogWriter audit,
    ITransactionalEmailService email) : IPaymentService
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
        await SaveAsync(ct);

        // The charge is recorded; tell the payer the gateway is working on it. A failure here must
        // not make the API look as if the charge was not raised.
        await email.PaymentInitiatedAsync(payment, order, ct);

        logger.LogInformation(
            "Payment {Reference} raised against order {OrderId} for {Amount} {Currency} (providerRef {ProviderRef})",
            payment.Reference, orderId, payment.Amount, payment.Currency, payment.ProviderRef);

        return await ToDtoAsync(payment, charge.NextActionUrl, ct);
    }

    // ---- Off-platform payments (task 2.5) ----

    /// <remarks>
    /// Writes across three commits — the payment row, the ledger row, then the order rollup and
    /// notifications — because <c>PAY-</c> and <c>TXN-</c> references come from two different
    /// max-plus-one sequences and each allocator owns its own transaction. A request that fails
    /// between them can therefore leave a settled payment with no ledger entry. The retry path
    /// repairs that; see the idempotency branch below. Collapsing this to one commit needs an
    /// allocator that can number two sequences inside a single transaction, which is a change to
    /// infrastructure shared with RFQ and order numbering and is not this task's to make.
    /// </remarks>
    public async Task<PaymentDto> RecordManualAsync(
        Guid orderId, Guid userId, RecordManualPaymentRequest request, CancellationToken ct = default)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw ApiException.NotFound("ORDER_NOT_FOUND", "Order not found.");

        // The seller records it, not the buyer. The buyer sends the money; only the beneficiary
        // of the account can see it land. A buyer attesting to their own transfer would let
        // anyone mark an order paid and demand dispatch against funds we cannot verify — the
        // exact claim this record exists to make trustworthy.
        if (await PartyAsync(order, userId, ct) != OrderParty.Seller)
        {
            throw ApiException.NotFound("ORDER_NOT_FOUND", "Order not found.");
        }

        // Money already in hand for a cancelled or completed order is a recovery, not a payment.
        // Recording it as one would let the ledger imply the platform handled a live transaction
        // it never could have.
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Completed)
        {
            throw ApiException.Conflict(
                "ORDER_CLOSED",
                order.Status == OrderStatus.Cancelled
                    ? "This order was cancelled, so nothing can be recorded against it."
                    : "This order is already complete.");
        }

        var idempotencyKey = request.IdempotencyKey?.Trim();
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            var replay = await db.Payments
                .FirstOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey, ct);

            if (replay is not null)
            {
                // Same rule as the gateway path: a retry guard that only matched on the key would
                // hand back the wrong payment and hide the client bug behind a success.
                if (replay.OrderId != orderId || replay.Amount != request.Amount)
                {
                    throw ApiException.Conflict(
                        "IDEMPOTENCY_KEY_REUSED",
                        "That idempotency key was already used for a different payment.");
                }

                // Recording spans three commits — the payment row, then the ledger row, then the
                // order rollup — because each reference comes from a different max-plus-one
                // sequence and each allocator owns its own transaction. So a request that dies
                // between them can leave a settled payment with no ledger entry behind it. It
                // cannot be prevented here without a single allocator spanning two sequences,
                // which is a bigger change than this task should make to shared infrastructure.
                //
                // It can be *repaired*, though, and a retry is exactly when that should happen.
                // RecordLedgerAsync is a no-op when the row is already there, so this is free on
                // the ordinary replay path and only does work after a torn commit.
                await RecordLedgerAsync(replay, ct);
                await db.SaveChangesAsync(ct);

                return await ToDtoAsync(replay, ct: ct);
            }
        }

        var remittanceReference = request.RemittanceReference.Trim();

        // The bank's trace id is unique per order. Recording the same transfer twice is the
        // likeliest mistake here — a double-click, or a second pass over a statement — and it
        // silently doubles the order's paid figure if it is not caught.
        var duplicateRef = await db.Payments
            .FirstOrDefaultAsync(
                p => p.OrderId == orderId && p.RemittanceReference == remittanceReference, ct);

        if (duplicateRef is not null)
        {
            throw ApiException.Conflict(
                "REMITTANCE_REFERENCE_REUSED",
                $"Remittance reference {remittanceReference} is already recorded on this order.");
        }

        var paid = order.Payments.Where(p => p.Status == PaymentStatus.Paid).Sum(p => p.Amount);
        var balanceDue = order.Total - paid;

        if (balanceDue <= 0)
        {
            throw ApiException.Conflict(
                "NOTHING_OUTSTANDING",
                "This order is already paid in full.");
        }

        if (request.Amount > balanceDue)
        {
            throw ApiException.BadRequest(
                "AMOUNT_EXCEEDS_BALANCE",
                $"Only {balanceDue:N2} {order.Currency} is outstanding on this order.");
        }

        var valueDate = ToUtc(request.ValueDateUtc);

        // A value date ahead of now describes money that has not arrived. Accepting it would put a
        // ledger entry dated later than today's settlements into a chronological sequence, and
        // the paid figure would then be true only as of some future moment.
        if (valueDate is { } stated && stated > DateTime.UtcNow)
        {
            throw ApiException.BadRequest(
                "VALUE_DATE_IN_FUTURE",
                "The value date cannot be in the future — the funds have to have arrived.");
        }

        var now = DateTime.UtcNow;

        var payment = new Payment
        {
            OrderId = order.Id,
            Amount = request.Amount,
            // The order's currency, never converted. A multi-currency order would need two
            // entries in two currencies; there is no rate table here to pretend otherwise.
            Currency = order.Currency,
            Provider = PaymentProvider.ManualTransfer,
            Method = request.Method,
            // Already settled. There is no provider to ask, so "pending" would only mean
            // "waiting for a human to come back and type something".
            Status = PaymentStatus.Paid,
            RemittanceReference = remittanceReference,
            BankName = request.BankName?.Trim(),
            ValueDateUtc = valueDate,
            InitiatedAtUtc = now,
            PaidAtUtc = valueDate ?? now,
            Notes = request.Notes?.Trim(),
            IdempotencyKey = string.IsNullOrEmpty(idempotencyKey)
                ? Guid.NewGuid().ToString("N")
                : idempotencyKey,
        };

        db.Payments.Add(payment);

        await SequenceAllocator.AllocateAsync(
            db,
            table: "Payments",
            column: "Reference",
            prefix: ReferencePrefix(),
            assign: reference => payment.Reference = reference,
            save: ct => db.SaveChangesAsync(ct),
            ct);

        await RecordLedgerAsync(payment, ct);

        var rolled = await RollUpOrderAsync(order.Id, ct);

        // The seller is the actor, so only the buyer is told. Both are notified on a gateway
        // settlement because there the buyer initiated and the seller learned; here that is
        // reversed, and a notification telling someone what they just did is noise.
        await NotifyAsync(payment, rolled, was: PaymentStatus.None, skipUserId: userId, ct);

        await SaveAsync(ct);

        // The seller recorded the money, so the buyer is the party who needs telling. `rolled` is
        // the order after the payment rollup, so the email and the order page agree on the balance.
        if (rolled is not null)
        {
            await email.PaymentReceivedAsync(payment, rolled, actorUserId: userId, ct);
        }

        logger.LogInformation(
            "Manual payment {Reference} recorded against order {OrderId} for {Amount} {Currency} (remittance {RemittanceReference})",
            payment.Reference, orderId, payment.Amount, payment.Currency, payment.RemittanceReference);

        return await ToDtoAsync(payment, ct: ct);
    }

    // ---- Refunds (roadmap §5.5) ----

    public async Task<PaymentDto> RefundAsync(
        Guid paymentId, Guid adminUserId, RefundPaymentRequest request, CancellationToken ct = default)
    {
        var payment = await db.Payments
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct)
            ?? throw ApiException.NotFound("PAYMENT_NOT_FOUND", "Payment not found.");

        // Only money actually received can be given back. A pending charge is still in flight, a
        // failed one never landed, and a refunded one has nothing left — its ledger is settled.
        if (payment.Status != PaymentStatus.Paid)
        {
            throw ApiException.Conflict(
                "PAYMENT_NOT_REFUNDABLE",
                payment.Status switch
                {
                    PaymentStatus.Pending => "This payment has not settled yet, so there is nothing to refund.",
                    PaymentStatus.Refunded => "This payment has already been refunded in full.",
                    _ => "Only a settled payment can be refunded.",
                });
        }

        var refundable = await RefundableAmountAsync(payment.Id, payment.Amount, ct);

        if (refundable <= 0)
        {
            throw ApiException.Conflict(
                "ALREADY_REFUNDED",
                $"Payment {payment.Reference} has already been refunded in full.");
        }

        var amount = request.Amount ?? refundable;

        if (amount <= 0)
        {
            throw ApiException.BadRequest(
                "INVALID_REFUND_AMOUNT",
                "The refund amount must be greater than zero.");
        }

        if (amount > refundable)
        {
            throw ApiException.BadRequest(
                "REFUND_EXCEEDS_PAYMENT",
                $"Only {refundable:N2} {payment.Currency} is refundable on payment {payment.Reference}.");
        }

        var full = amount == refundable;
        var reason = request.Reason?.Trim();
        var occurredAtUtc = DateTime.UtcNow;

        // The compensating entry: negative, so the order's net settled figure — and the corridor
        // stats that sum the ledger — fall by exactly this much, and immutable like every row.
        var entry = new Transaction
        {
            OrderId = payment.OrderId,
            Type = TransactionType.Refund,
            Amount = -amount,
            Currency = payment.Currency,
            PaymentId = payment.Id,
            OccurredAtUtc = occurredAtUtc,
            Narrative = $"Refund of {payment.Reference}"
                + (full ? string.Empty : " (partial)")
                + (reason is null ? string.Empty : $" — {reason}"),
            ExternalReference = null,
        };

        db.Transactions.Add(entry);

        // Bring the payment's rowversion into this write before the allocator commits the entry.
        // The refundable balance was read from the ledger, and a second simultaneous refund reads
        // the same balance; making the payment row part of the same transaction turns that race
        // into a lost update — a clean 409 — instead of a double refund. It also keeps the ledger
        // entry and the payment state in one commit, so a lost race cannot leave a refund entry
        // behind with no matching payment state.
        if (full)
        {
            // A full refund is money the payment began and ended with; the row changes state so
            // the payment list reads true.
            payment.Status = PaymentStatus.Refunded;
            payment.RefundedAtUtc = occurredAtUtc;
        }
        else
        {
            // A partial refund leaves the payment Paid — most of it is still money the seller is
            // holding — and only the ledger says what went back. It still has to serialise against
            // another refund, so touch the audited timestamp to bring the rowversion into the write.
            db.Entry(payment).Property(p => p.UpdatedAtUtc).IsModified = true;
        }

        // Same allocator as settlements, for the same reason: MAX+1 is racy. It commits the entry
        // and the payment state together.
        await SequenceAllocator.AllocateAsync(
            db,
            table: "Transactions",
            column: "Reference",
            prefix: TransactionPrefix(),
            assign: reference => entry.Reference = reference,
            save: ct => SaveAsync(ct),
            ct);

        var order = await RollUpOrderAsync(payment.OrderId, ct);

        await NotifyRefundAsync(payment, order, amount, reason, ct);

        audit.Record(
            adminUserId,
            "PaymentRefund",
            "Payment",
            payment.Id,
            $"Refunded {amount:N2} {payment.Currency} on payment {payment.Reference} "
                + $"(order {(order is null ? payment.OrderId.ToString() : order.OrderNumber)})",
            reason);

        await SaveAsync(ct);

        // Both sides need to see money leave the order; the administrator who issued the refund is
        // not a party and the notification does not tell them. Best-effort, after the commit.
        if (order is not null)
        {
            await email.PaymentRefundedAsync(payment, order, amount, reason, ct);
        }

        logger.LogInformation(
            "Refunded {Amount} {Currency} on payment {Reference} (order {OrderId})",
            amount, payment.Currency, payment.Reference, payment.OrderId);

        return await ToDtoAsync(payment, ct: ct);
    }

    /// <summary>
    /// Saves, translating a lost rowversion race into a clean 409.
    /// </summary>
    /// <remarks>
    /// Two admins refunding the same payment, or a webhook settling while a refund is in flight,
    /// must surface as a conflict the caller can retry — not a 500. The webhook path has its own
    /// retry loop; this is for the request-driven paths that have nothing to re-evaluate against.
    /// </remarks>
    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ApiException.Conflict(
                "PAYMENT_CHANGED",
                "This payment or its order was updated by someone else at the same time. Reload it and try again.");
        }
    }

    /// <summary>
    /// How much of a settled payment is still refundable: what arrived, less what has gone back.
    /// Refund ledger entries are negative, so the sum is subtracted from the settled amount.
    /// </summary>
    private async Task<decimal> RefundableAmountAsync(Guid paymentId, decimal settledAmount, CancellationToken ct)
    {
        var refunded = await db.Transactions
            .Where(t => t.PaymentId == paymentId && t.Type == TransactionType.Refund)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;
        return settledAmount + refunded;
    }

    /// <summary>
    /// Positive refund totals per payment on an order, in one query — a list view never pays for
    /// a per-payment round trip.
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> RefundedTotalsByPaymentAsync(Guid orderId, CancellationToken ct)
    {
        var rows = await db.Transactions
            .AsNoTracking()
            .Where(t => t.OrderId == orderId && t.Type == TransactionType.Refund && t.PaymentId != null)
            .GroupBy(t => t.PaymentId!.Value)
            .Select(g => new { PaymentId = g.Key, RefundedSum = g.Sum(t => t.Amount) })
            .ToListAsync(ct);

        return rows.ToDictionary(x => x.PaymentId, x => -x.RefundedSum);
    }

    public async Task<IReadOnlyList<TransactionDto>> ListTransactionsAsync(
        Guid orderId, Guid userId, CancellationToken ct = default)
    {
        // A missing order and someone with no stake in it are the same answer, deliberately: a
        // 403 would confirm an order exists, which is itself private.
        if (await PartyForOrderIdAsync(orderId, userId, ct) == OrderParty.Outsider)
        {
            throw ApiException.NotFound("ORDER_NOT_FOUND", "Order not found.");
        }

        // Oldest first, with the reference as a tie-break so the order is stable for entries that
        // share a timestamp — which a same-day set of transfers will.
        return await db.Transactions
            .AsNoTracking()
            .Where(t => t.OrderId == orderId)
            .OrderBy(t => t.OccurredAtUtc)
            .ThenBy(t => t.Reference)
            .Select(t => new TransactionDto
            {
                Id = t.Id,
                OrderId = t.OrderId,
                Reference = t.Reference,
                Type = t.Type,
                Amount = t.Amount,
                Currency = t.Currency,
                PaymentId = t.PaymentId,
                PaymentReference = t.Payment == null ? null : t.Payment.Reference,
                ExternalReference = t.ExternalReference,
                OccurredAtUtc = t.OccurredAtUtc,
                Narrative = t.Narrative,
            })
            .ToListAsync(ct);
    }

    /// <summary>Party lookup for a bare order id, for reads that have no order loaded.</summary>
    private async Task<OrderParty> PartyForOrderIdAsync(Guid orderId, Guid userId, CancellationToken ct)
    {
        var buyerId = await db.Orders
            .AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => (Guid?)o.BuyerUserId)
            .FirstOrDefaultAsync(ct);

        if (buyerId is null)
        {
            return OrderParty.Outsider;
        }

        if (buyerId == userId)
        {
            return OrderParty.Buyer;
        }

        return await db.Orders
            .AsNoTracking()
            .AnyAsync(o => o.Id == orderId && db.Businesses.Any(b => b.Id == o.SellerBusinessId && b.OwnerUserId == userId), ct)
            ? OrderParty.Seller
            : OrderParty.Outsider;
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

        var payments = await db.Payments
            .AsNoTracking()
            .Where(p => p.OrderId == orderId)
            .OrderByDescending(p => p.CreatedAtUtc ?? p.InitiatedAtUtc)
            .ToListAsync(ct);

        var refundedByPayment = await RefundedTotalsByPaymentAsync(orderId, ct);

        return payments
            .Select(p => ToDto(
                p,
                order.OrderNumber,
                refundedAmount: refundedByPayment.TryGetValue(p.Id, out var refunded) ? refunded : null))
            .ToList();
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

    /// <summary>
    /// Applies a webhook event, retrying when a concurrent event changes the payment or its order
    /// between the read and the write.
    /// </summary>
    /// <remarks>
    /// The payment carries a rowversion, so when two events for one charge are applied at once the
    /// second save loses instead of overwriting the first — without this the later of the two could
    /// flip a settled payment to Failed while the ledger still held its settlement. Losing the save
    /// is not an error here: this event's own row is already durable, and everything the apply
    /// touches — the ledger entry, the receipt, the order rollup — is idempotent, so re-evaluating
    /// from committed state is safe. Re-reads the event too, because the failed save rolled back the
    /// outcome written onto it.
    /// </remarks>
    private async Task<WebhookAck> ApplyAsync(
        PaymentWebhookEvent record, SandboxWebhookPayload payload, CancellationToken ct)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ApplyOnceAsync(record, payload, ct);
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxAttempts)
            {
                db.ChangeTracker.Clear();
                record = await db.PaymentWebhookEvents.FirstAsync(w => w.Id == record.Id, ct);
            }
        }
    }

    private async Task<WebhookAck> ApplyOnceAsync(
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

        // A settlement is terminal. Money that has settled on a card rail does not un-settle: if
        // the gateway genuinely takes it back, that is a refund, which is a separate entry with a
        // separate reference — not a failure arriving late.
        //
        // The ordering check above does not cover this. It compares timestamps, and a
        // payment.failed stamped genuinely *after* the success sails through, because by
        // timestamp it is the newest word. Applying it would be the worst outcome available: the
        // payment flips to Failed while the ledger keeps its "settled" row, so the order's paid
        // figure and the financial record disagree, permanently and invisibly. Money is returned
        // only by an explicit refund booked as a compensating entry (see
        // <see cref="RefundAsync"/>), so the honest thing here is to record the event and decline
        // to act on it.
        if (payment.Status == PaymentStatus.Paid && target != PaymentStatus.Paid)
        {
            record.Outcome = PaymentWebhookEvent.Outcomes.SupersedesSettlement;
            record.ProcessedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            logger.LogWarning(
                "Refusing webhook {EventId} for payment {Reference}: {EventType} would undo a settled payment",
                record.ExternalEventId, payment.Reference, record.EventType);

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

        await NotifyAsync(payment, order, was, ct: ct);

        await db.SaveChangesAsync(ct);

        // Only now, with the settlement durable, is the email safe to describe as a fact. The
        // concurrency retry above re-enters before this point, so a lost save cannot send twice.
        if (order is not null)
        {
            if (payment.Status == PaymentStatus.Paid)
            {
                await email.PaymentReceivedAsync(payment, order, ct: ct);
            }
            else if (payment.Status == PaymentStatus.Failed)
            {
                await email.PaymentFailedAsync(payment, order, ct);
            }
        }

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
            Narrative = LedgerNarrative(payment),
            // Whichever identity the counterparty gave the money: the gateway's charge id, or the
            // bank's trace id. This is the column an auditor reconciles against, so leaving it
            // null for a manual transfer would drop the only externally-verifiable detail on the
            // entry.
            ExternalReference = payment.ProviderRef ?? payment.RemittanceReference,
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

        // A settled payment is receiptable the moment it is booked. Generating it here rather
        // than at a separate call site means both settlement routes - a gateway webhook and a
        // seller-recorded manual transfer - produce a receipt, and produce exactly one.
        await EnsureReceiptAsync(payment, ct);
    }

    /// <summary>
    /// Writes the receipt for a settled payment if one does not already exist.
    /// </summary>
    /// <remarks>
    /// Guarded on <see cref="PaymentId"/> rather than only on order, because a partial-payment
    /// order legitimately has several receipts. Idempotent so the webhook retry path, which may
    /// re-enter <see cref="RecordLedgerAsync"/>, cannot mint a second receipt for one settlement.
    /// </remarks>
    private async Task EnsureReceiptAsync(Payment payment, CancellationToken ct)
    {
        if (payment.Status != PaymentStatus.Paid)
        {
            return;
        }

        var exists = await db.Receipts.AnyAsync(r => r.PaymentId == payment.Id, ct);
        if (exists)
        {
            return;
        }

        db.Receipts.Add(new Domain.Trade.Receipt
        {
            OrderId = payment.OrderId,
            PaymentId = payment.Id,
            InvoiceId = await db.Invoices
                .Where(i => i.OrderId == payment.OrderId)
                .Select(i => (Guid?)i.Id)
                .FirstOrDefaultAsync(ct),
            ReceiptNumber = $"RCPT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            Amount = payment.Amount,
            Currency = payment.Currency,
            IssuedAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow,
            Notes = payment.RemittanceReference is { Length: > 0 } reference
                ? $"Received against {reference}."
                : null,
        });
    }

    /// <summary>
    /// Recomputes the order's payment status from its payments and refunds, and returns the order.
    /// </summary>
    /// <remarks>
    /// Paid wins over everything: an order whose balance is fully covered is Paid even if an
    /// unrelated payment failed, because the failure did not stop it being covered. PartiallyPaid
    /// needs a positive settled amount, so a pending charge alone stays Pending. Refunds are
    /// signed ledger entries (negative), so netting them in means money that went back out stops
    /// counting as held.
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

        // Only this order's refunds count — a refund on a different order must not move this one.
        var refunded = await db.Transactions
            .Where(t => t.OrderId == orderId && t.Type == TransactionType.Refund)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

        var net = paid + refunded;

        order.PaymentStatus =
            net >= order.Total && net > 0 ? PaymentStatus.Paid
            : net > 0 ? PaymentStatus.PartiallyPaid
            : pending ? PaymentStatus.Pending
            : PaymentStatus.None;

        // Keep the order's invoice in step with the money. Only a fully-paid order closes its
        // invoice; a partial settlement leaves it Issued so the outstanding balance is visible.
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.OrderId == orderId, ct);
        if (invoice is not null)
        {
            if (order.PaymentStatus == PaymentStatus.Paid && invoice.Status != InvoiceStatus.Paid)
            {
                invoice.Status = InvoiceStatus.Paid;
                invoice.PaidAtUtc = DateTime.UtcNow;
            }
            else if (order.PaymentStatus != PaymentStatus.Paid && invoice.Status == InvoiceStatus.Paid)
            {
                // Money went back out (a refund), dropping the order below fully paid. The
                // invoice reopens Issued, or the outstanding balance is silently hidden by a
                // status that claims the debt is closed.
                invoice.Status = InvoiceStatus.Issued;
                invoice.PaidAtUtc = null;
            }
        }

        return order;
    }

    /// <summary>
    /// Describes a settlement in words, distinguishing the two very different ways money arrives.
    /// "via gateway" on a bank transfer would be a lie recorded permanently in the ledger.
    /// </summary>
    private static string LedgerNarrative(Payment payment) => payment.Method switch
    {
        PaymentMethod.BankTransfer =>
            $"Bank transfer {payment.RemittanceReference} received"
                + (payment.BankName is null ? "" : $" via {payment.BankName}"),
        PaymentMethod.LetterOfCredit =>
            $"Letter of credit presented and honoured ({payment.RemittanceReference})",
        _ => $"Payment {payment.Reference} settled",
    };

    private async Task NotifyAsync(
        Payment payment,
        Order? order,
        PaymentStatus was,
        Guid? skipUserId = null,
        CancellationToken ct = default)
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

        // The party that performed the action does not need telling about it.
        var tellSeller = sellerOwnerId is not null && sellerOwnerId != skipUserId;
        var tellBuyer = order.BuyerUserId != skipUserId;

        var link = $"/dashboard/orders/{order.Id}";

        if (payment.Status == PaymentStatus.Paid && was != PaymentStatus.Paid)
        {
            // The seller hearing they have been paid is the commercially important half of
            // this event; the buyer already knows they paid.
            if (tellSeller)
            {
                db.Notifications.Add(new Domain.Communication.Notification
                {
                    UserId = sellerOwnerId!.Value,
                    Type = NotificationType.Payment,
                    Title = $"Payment received on order {order.OrderNumber}",
                    Body = $"{payment.Reference} settled for {payment.Amount:N2} {payment.Currency}.",
                    Link = link,
                });
            }

            if (tellBuyer)
            {
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
        }
        else if (payment.Status == PaymentStatus.Failed && was != PaymentStatus.Failed)
        {
            if (tellBuyer)
            {
                db.Notifications.Add(new Domain.Communication.Notification
                {
                    UserId = order.BuyerUserId,
                    Type = NotificationType.Payment,
                    Title = $"Payment {payment.Reference} failed",
                    Body = payment.FailureReason ?? "The gateway declined this charge.",
                    Link = link,
                });
            }

            if (tellSeller)
            {
                db.Notifications.Add(new Domain.Communication.Notification
                {
                    UserId = sellerOwnerId!.Value,
                    Type = NotificationType.Payment,
                    Title = $"Payment on order {order.OrderNumber} failed",
                    Body = $"{payment.Reference} was declined. The order is still outstanding.",
                    Link = link,
                });
            }
        }
    }

    /// <summary>
    /// Tells both parties that money came back off their order. The administrator is the actor,
    /// so unlike <see cref="NotifyAsync"/> nobody is skipped for having done the deed.
    /// </summary>
    private async Task NotifyRefundAsync(
        Payment payment, Order? order, decimal amount, string? reason, CancellationToken ct)
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

        var title = $"Refund issued on order {order.OrderNumber}";
        var body = $"Payment {payment.Reference} refunded {amount:N2} {payment.Currency}."
            + (reason is null ? string.Empty : $" Reason: {reason}");
        var link = $"/dashboard/orders/{order.Id}";

        if (sellerOwnerId is { } sellerId)
        {
            db.Notifications.Add(new Domain.Communication.Notification
            {
                UserId = sellerId,
                Type = NotificationType.Refund,
                Title = title,
                Body = body,
                Link = link,
            });
        }

        db.Notifications.Add(new Domain.Communication.Notification
        {
            UserId = order.BuyerUserId,
            Type = NotificationType.Refund,
            Title = title,
            Body = body,
            Link = link,
        });
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

        var refunded = await db.Transactions
            .Where(t => t.PaymentId == payment.Id && t.Type == TransactionType.Refund)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

        return ToDto(payment, orderNumber, nextActionUrl, refundedAmount: refunded == 0 ? null : -refunded);
    }

    internal static PaymentDto ToDto(
        Payment payment, string orderNumber, string? nextActionUrl = null, decimal? refundedAmount = null) => new()
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
        RefundedAtUtc = payment.RefundedAtUtc,
        RefundedAmount = refundedAmount,
        NextActionUrl = payment.Status == PaymentStatus.Pending ? nextActionUrl : null,
    };

    private static string ReferencePrefix() =>
        $"{ReferencePrefixStem}{DateTime.UtcNow.Year}-";

    private static string TransactionPrefix() =>
        $"{TransactionPrefixStem}{DateTime.UtcNow.Year}-";
}
