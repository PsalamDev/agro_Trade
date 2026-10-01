using AgroTrade.Domain.Common;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Domain.Trade;

/// <summary>
/// Every gateway webhook the system has been sent, kept as an audit trail and used as the
/// idempotency guard for delivery (plan §02 — Payments).
///
/// <para>
/// Gateways deliver webhooks at-least-once and will happily repeat an event for days. A
/// unique index on (Provider, ExternalEventId) turns a redelivery into a failed insert, which
/// is how the handler recognises it and answers 200 without touching the payment. Answering
/// 200 is deliberate: a non-2xx is an instruction to the gateway to retry, and retrying an
/// event we have already applied is precisely the thing we are preventing.
/// </para>
///
/// <para>
/// Deliberately has no foreign key to <see cref="Payment"/>. A webhook event is evidence of
/// what the gateway told us and should outlive the payment — and, through it, the order.
/// <see cref="PaymentId"/> is a plain column for correlation only.
/// </para>
/// </summary>
public sealed class PaymentWebhookEvent : IAuditableEntity
{
    /// <summary>Outcomes recorded against <see cref="Outcome"/>.</summary>
    public static class Outcomes
    {
        /// <summary>Changed the payment.</summary>
        public const string Applied = "Applied";

        /// <summary>Already seen — the unique index refused the second insert.</summary>
        public const string Duplicate = "Duplicate";

        /// <summary>Older than the payment's last applied event, so superseded.</summary>
        public const string Stale = "Stale";

        /// <summary>Names a charge we hold no payment for.</summary>
        public const string UnmatchedPayment = "UnmatchedPayment";

        /// <summary>A well-formed event we do not act on, e.g. an unmodelled type.</summary>
        public const string Ignored = "Ignored";

        /// <summary>Signature did not verify. Never reaches the ledger in normal operation.</summary>
        public const string BadSignature = "BadSignature";
    }

    public Guid Id { get; set; } = Guid.NewGuid();

    public PaymentProvider Provider { get; set; }

    /// <summary>
    /// The gateway's own event id. Unique with <see cref="Provider"/> — this pair is the
    /// idempotency guarantee, and the index on it is the mechanism.
    /// </summary>
    public string ExternalEventId { get; set; } = string.Empty;

    /// <summary>
    /// Gateway event type, e.g. <c>payment.succeeded</c>. Left as the provider's own string
    /// rather than an enum: event vocabularies are gateway-specific and adding a real
    /// provider should not mean renumbering ours.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Charge this event refers to, as the gateway named it.</summary>
    public string ProviderRef { get; set; } = string.Empty;

    /// <summary>Correlation only — see the class remarks on why there is no foreign key.</summary>
    public Guid? PaymentId { get; set; }

    /// <summary>
    /// The raw body exactly as received. Kept so a decision can be re-read later, and because
    /// the signature is over these bytes: re-serialising a parsed object would not verify.
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Signature header as received, for the audit trail.</summary>
    public string? Signature { get; set; }

    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>Null while the event is still being worked through.</summary>
    public DateTime? ProcessedAtUtc { get; set; }

    /// <summary>One of the <see cref="Outcomes"/> constants.</summary>
    public string? Outcome { get; set; }

    /// <summary>
    /// The gateway's timestamp for the event, used for the ordering decision. Distinct from
    /// <see cref="ReceivedAtUtc"/> because delivery can be late but the event itself is stamped
    /// when it happened.
    /// </summary>
    public DateTime? OccurredAtUtc { get; set; }

    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
