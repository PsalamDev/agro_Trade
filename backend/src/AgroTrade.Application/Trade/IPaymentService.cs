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

    /// <summary>Where the payer completes the charge. Sandbox only; null once settled.</summary>
    public string? NextActionUrl { get; init; }

    /// <summary>Set when this payment is outstanding against the order, for the list view.</summary>
    public decimal? OrderBalanceDue { get; init; }
}
