using AgroTrade.Domain.Common;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Domain.Trade;

/// <summary>
/// A payment against an order. Off-platform transfers (bank T/T, L/C) are recorded here
/// with <see cref="PaymentProvider.ManualTransfer"/> and a remittance reference — no
/// gateway, no webhook (plan §02 — Payments).
/// </summary>
public sealed class Payment : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }

    /// <summary>Human-facing reference, unique. e.g. "PAY-2026-000001".</summary>
    public string Reference { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public PaymentProvider Provider { get; set; } = PaymentProvider.ManualTransfer;
    public PaymentMethod Method { get; set; } = PaymentMethod.BankTransfer;

    /// <summary>Bank reference / remittance advice for manual transfers.</summary>
    public string? RemittanceReference { get; set; }
    /// <summary>Bank name for manual transfers, so the buyer can reconcile.</summary>
    public string? BankName { get; set; }
    public DateTime? ValueDateUtc { get; set; }

    /// <summary>
    /// The gateway's own charge id, e.g. <c>sbx_9f2c…</c>. This is what a webhook names, and
    /// the only way to match an inbound event back to a payment — a gateway has no idea about
    /// our references. Null for a manual transfer, which has no gateway.
    /// </summary>
    public string? ProviderRef { get; set; }

    /// <summary>
    /// Makes initiation idempotent: repeating the request with the same key returns the
    /// original payment rather than raising a second charge at the gateway. Generated when the
    /// caller does not supply one, which covers retries of *our* API but not a user who
    /// genuinely wants to pay twice — that case gets a new key and a new payment.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// When the most recently applied gateway event arrived. Webhooks are not delivered in
    /// order, so an event older than this has been superseded by one already applied and is
    /// recorded but not acted on — otherwise a late <c>payment.failed</c> could undo a
    /// <c>payment.succeeded</c>.
    /// </summary>
    public DateTime? LastEventAtUtc { get; set; }

    /// <summary>
    /// Where the payment has got to.
    /// </summary>
    /// <remarks>
    /// <see cref="PaymentStatus.Paid"/> does not un-settle on its own. Settled money on a card
    /// rail is not reversed by a late <c>payment.failed</c> — that would leave the ledger claiming
    /// a settlement the order no longer counts, with nothing recording that they disagree. Money
    /// is returned only by an explicit refund, which writes a compensating
    /// <see cref="TransactionType.Refund"/> ledger entry and, once nothing is left to refund,
    /// moves this payment to <see cref="PaymentStatus.Refunded"/>.
    /// </remarks>
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    public string? FailureReason { get; set; }

    public DateTime InitiatedAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }

    /// <summary>
    /// When the payment was refunded in full. A partial refund leaves this null — the payment
    /// stays <see cref="PaymentStatus.Paid"/> and the ledger records what went back; only when
    /// nothing is left to refund does the payment itself change state.
    /// </summary>
    public DateTime? RefundedAtUtc { get; set; }

    public string? Notes { get; set; }

    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public Order? Order { get; set; }
}