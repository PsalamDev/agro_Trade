using AgroTrade.Application.Common;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Application.Trade;

/// <summary>
/// Payment initiation and gateway event handling (plan §02 — Payments; roadmap §5.5).
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Raises a gateway charge against an order and records the payment as pending.
    /// Buyer only, and not on a closed order. Safe to retry: repeating with the same
    /// <see cref="InitiatePaymentRequest.IdempotencyKey"/> returns the original payment.
    /// </summary>
    Task<PaymentDto> InitiateAsync(
        Guid orderId, Guid userId, InitiatePaymentRequest request, CancellationToken ct = default);

    /// <summary>Payments on an order. Both parties may read them.</summary>
    Task<IReadOnlyList<PaymentDto>> ListForOrderAsync(
        Guid orderId, Guid userId, CancellationToken ct = default);

    /// <summary>A single payment. Both parties may read it.</summary>
    Task<PaymentDto> GetAsync(Guid paymentId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Records an off-platform payment — a bank transfer or letter of credit — against an order,
    /// already settled, with the bank's remittance reference.
    ///
    /// <para>
    /// <b>Seller only.</b> This is the deliberate inverse of
    /// <see cref="InitiateAsync"/>: the buyer is the party that sends the money, but only the
    /// beneficiary of the account can observe that it arrived. Letting the buyer attest to their
    /// own transfer would let anyone mark an order paid and demand dispatch against funds the
    /// platform cannot verify exist.
    /// </para>
    ///
    /// <para>
    /// Safe to retry via <see cref="RecordManualPaymentRequest.IdempotencyKey"/>, and separately
    /// guarded by the remittance reference, which is unique per order.
    /// </para>
    /// </summary>
    Task<PaymentDto> RecordManualAsync(
        Guid orderId, Guid userId, RecordManualPaymentRequest request, CancellationToken ct = default);

    /// <summary>
    /// The order's ledger: one immutable row per money movement, oldest first. Both parties may
    /// read it. This is the financial record, not the list of attempts.
    /// </summary>
    Task<IReadOnlyList<TransactionDto>> ListTransactionsAsync(
        Guid orderId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Gives money back on a settled payment. <b>Administrator only</b> — the controller carrying
    /// this call enforces that policy, and <paramref name="adminUserId"/> is passed in so the act
    /// is attributed to a person in the audit trail.
    ///
    /// <para>
    /// Books a compensating <see cref="TransactionType.Refund"/> ledger entry (negative amount)
    /// rather than flipping the payment, because the ledger is the record an auditor reconciles
    /// against and the entry must survive the payment row being edited. A full refund of the
    /// payment's remaining refundable amount also moves the payment to
    /// <see cref="PaymentStatus.Refunded"/> and sets
    /// <see cref="PaymentDto.RefundedAtUtc"/>; a partial refund leaves it
    /// <see cref="PaymentStatus.Paid"/> with the ledger saying what went back. Either way the
    /// order's status is re-rolled up, a Paid invoice reopens if the order drops below fully
    /// paid, and both parties are notified.
    /// </para>
    /// </summary>
    Task<PaymentDto> RefundAsync(
        Guid paymentId, Guid adminUserId, RefundPaymentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Verifies, deduplicates and applies one gateway event. <paramref name="rawBody"/> must be
    /// the exact bytes received: the signature covers them, and a re-serialised payload will
    /// not verify.
    /// </summary>
    Task<WebhookAck> HandleWebhookAsync(
        PaymentProvider provider, string? signature, string rawBody, CancellationToken ct = default);
}

/// <summary>
/// Sandbox-only affordance: drive a charge to succeeded or failed, which dispatches a real
/// signed webhook through the same verification and idempotency path a live gateway would use.
///
/// <para>
/// Deliberately not on <see cref="IPaymentService"/>. A service that offers "settle this for
/// me" has to know which adapter it is settling, and that is a sandbox concern — a live gateway
/// settles its own charges without being asked.
/// </para>
/// </summary>
public interface ISandboxPaymentSimulator
{
    Task<PaymentDto> SettleAsync(
        Guid paymentId, Guid userId, SimulatePaymentRequest request, CancellationToken ct = default);
}

// ---- Requests & results ----

public sealed class InitiatePaymentRequest
{
    /// <summary>
    /// Defaults to the order's outstanding balance. A partial payment must be positive and
    /// cannot exceed it.
    /// </summary>
    public decimal? Amount { get; init; }

    /// <summary>
    /// Optional caller-supplied retry guard. Retrying with the same key returns the original
    /// payment instead of raising a second charge.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    public string? Notes { get; init; }
}

/// <summary>Outcome the sandbox charge should be driven to.</summary>
public sealed class SimulatePaymentRequest
{
    public string Outcome { get; init; } = string.Empty;
}

/// <summary>
/// An off-platform payment the seller confirms has landed: a bank transfer, or a letter of credit
/// presented and honoured.
/// </summary>
/// <remarks>
/// Recorded as already settled rather than pending. There is no provider to ask, so a "pending"
/// state here would mean "waiting for a human to type something", and the record would exist only
/// to be updated. The value date carries the timing instead.
/// </remarks>
public sealed class RecordManualPaymentRequest
{
    /// <summary>
    /// Settled amount. Positive and no more than the order's outstanding balance. Partial payments
    /// are the norm — a deposit against the balance is the common trade term.
    /// </summary>
    public decimal Amount { get; init; }

    /// <summary>
    /// Bank transfer or letter of credit. <see cref="PaymentMethod.Gateway"/> is refused: a
    /// gateway payment is raised through <see cref="InitiatePaymentRequest"/>, not typed in.
    /// </summary>
    public PaymentMethod Method { get; init; } = PaymentMethod.BankTransfer;

    /// <summary>
    /// The bank's own reference for the transfer — the UTR/narrative on the remittance advice.
    /// Required, and unique per order: it is the field an auditor reconciles against, so the same
    /// trace id recorded twice is a double-count, not a second transfer.
    /// </summary>
    public string RemittanceReference { get; init; } = string.Empty;

    /// <summary>Sending or receiving bank, so the buyer can match it against their statement.</summary>
    public string? BankName { get; init; }

    /// <summary>
    /// When the funds actually landed. Defaults to now. Must not be in the future: a value date
    /// ahead of today describes money that has not arrived yet. Send UTC; a value with no offset
    /// is read as UTC.
    /// </summary>
    public DateTime? ValueDateUtc { get; init; }

    public string? Notes { get; init; }

    /// <summary>
    /// Optional caller-supplied retry guard, as on <see cref="InitiatePaymentRequest"/>. Guards a
    /// retried request; the remittance reference guards a genuine double-entry, which is the more
    /// likely mistake and is caught regardless of keys.
    /// </summary>
    public string? IdempotencyKey { get; init; }
}

/// <summary>
/// An administrator refunding money back on a settled payment.
/// </summary>
/// <remarks>
/// The refund is booked as a compensating ledger entry and can never exceed what the payment
/// still has refundable (its settled amount less anything already refunded), so a retried or
/// doubled call is refused rather than silently over-refunded.
/// </remarks>
public sealed class RefundPaymentRequest
{
    /// <summary>
    /// Amount to refund. Defaults to everything the payment still has refundable.
    /// </summary>
    public decimal? Amount { get; init; }

    /// <summary>
    /// Why. Optional, and recorded on the ledger entry and in the audit trail — the money
    /// movement is immutable, and so should its justification be.
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>Simulated outcomes, kept in step with <see cref="GatewayStatuses"/>.</summary>
public static class SimulateOutcomes
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
}

/// <summary>
/// What the webhook endpoint returns. Always a success code when we understood the delivery —
/// the ack is how the caller learns whether work was done, not whether we want more.
/// </summary>
public sealed record WebhookAck(
    bool Duplicate,
    string Outcome,
    Guid? PaymentId,
    string? Status);

public sealed record PaymentDto
{
    public Guid Id { get; init; }
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;

    public string Reference { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public PaymentProvider Provider { get; init; }
    public PaymentMethod Method { get; init; }
    public PaymentStatus Status { get; init; }

    /// <summary>The gateway's charge id. Null for a manual transfer, which has no gateway.</summary>
    public string? ProviderRef { get; init; }

    public string? RemittanceReference { get; init; }
    public string? BankName { get; init; }
    public DateTime? ValueDateUtc { get; init; }
    public string? FailureReason { get; init; }
    public string? Notes { get; init; }
    public DateTime InitiatedAtUtc { get; init; }
    public DateTime? PaidAtUtc { get; init; }

    /// <summary>When the payment was refunded in full. Null until nothing is left to refund.</summary>
    public DateTime? RefundedAtUtc { get; init; }

    /// <summary>
    /// How much of this payment has been refunded so far, as a positive number. Null when none
    /// has — history ledger rows tell the full story; this is the same money for the list view.
    /// </summary>
    public decimal? RefundedAmount { get; init; }

    /// <summary>Where the payer completes the charge. Sandbox only; null once settled.</summary>
    public string? NextActionUrl { get; init; }

    /// <summary>Set when this payment is outstanding against the order, for the list view.</summary>
    public decimal? OrderBalanceDue { get; init; }
}

/// <summary>
/// One immutable ledger entry. Unlike <see cref="PaymentDto"/>, which is a mutable attempt, this
/// is the financial history: it is written once when money moves and never edited.
/// </summary>
public sealed record TransactionDto
{
    public Guid Id { get; init; }
    public Guid OrderId { get; init; }

    /// <summary>Ledger reference, e.g. "TXN-2026-000001".</summary>
    public string Reference { get; init; } = string.Empty;

    public TransactionType Type { get; init; }

    /// <summary>Positive for money in, negative for money out.</summary>
    public decimal Amount { get; init; }

    public string Currency { get; init; } = string.Empty;

    /// <summary>The payment that produced this entry, if there was one.</summary>
    public Guid? PaymentId { get; init; }

    /// <summary>That payment's reference, resolved so the ledger reads without a second lookup.</summary>
    public string? PaymentReference { get; init; }

    /// <summary>The counterparty's own reference — the gateway charge id, or the bank's trace id.</summary>
    public string? ExternalReference { get; init; }

    public DateTime OccurredAtUtc { get; init; }
    public string? Narrative { get; init; }
}
