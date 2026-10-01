using AgroTrade.Domain.Enums;

namespace AgroTrade.Application.Trade;

/// <summary>
/// What the marketplace asks a gateway to do. Deliberately tiny: an adapter's whole job is to
/// turn a charge request into a provider reference and a status, and to be replaceable when a
/// second corridor opens (plan §04 — Payments; roadmap §5.5).
///
/// <para>
/// This is the seam Phase 3 widens into the multi-currency gateway matrix. Adding a real
/// provider means implementing this interface and registering it, not touching
/// <see cref="IPaymentService"/> — which is the reason nothing here mentions HTTP, and the
/// reason the sandbox can dispatch an event in-process without lying about the shape.
/// </para>
/// </summary>
public interface IPaymentGateway
{
    PaymentProvider Provider { get; }

    /// <summary>
    /// Currencies this gateway can settle. The service refuses anything else with a clear
    /// error rather than creating a charge that will never arrive — this is how "one corridor"
    /// is an enforced constraint instead of a comment.
    /// </summary>
    IReadOnlyCollection<string> SupportedCurrencies { get; }

    /// <summary>Raises a charge. Must be safe to retry under the same idempotency key.</summary>
    Task<GatewayCharge> CreateChargeAsync(GatewayChargeRequest request, CancellationToken ct = default);

    /// <summary>
    /// Checks a webhook body against its signature.
    /// </summary>
    /// <remarks>
    /// On the interface, not in the service, because every provider signs differently and a
    /// real one will not use HMAC over the raw body at all. Verification is the adapter's own
    /// business; what the service needs is a yes or no it can trust.
    /// </remarks>
    bool VerifyWebhook(string rawBody, string? signature);
}

/// <summary>
/// Picks the gateway that can settle a given currency, or by provider for an inbound event.
///
/// <para>
/// This exists so that "which rail handles this corridor" is a registration concern rather than
/// a switch. <see cref="PaymentProvider"/> identifies <em>who</em> the counterparty is and is
/// append-only, so a second card rail must not arrive as a new enum value per currency — that
/// would make <c>SandboxUsd</c>/<c>SandboxEur</c>/<c>StripeEur</c> permanent and force a rewrite
/// of every caller that switches on it. Capability lives on the adapter; this finds the one
/// that has it.
/// </para>
/// </summary>
public interface IPaymentGatewayResolver
{
    /// <summary>First registered gateway that settles <paramref name="currency"/>.</summary>
    IPaymentGateway? Resolve(string currency);

    /// <summary>The gateway for a provider, for verifying and applying an inbound event.</summary>
    IPaymentGateway? ResolveProvider(PaymentProvider provider);
}

/// <summary>One charge to raise against a gateway.</summary>
public sealed record GatewayChargeRequest
{
    /// <summary>Our payment id, passed through so an adapter can log against it.</summary>
    public required Guid PaymentId { get; init; }

    /// <summary>Our human-readable reference, e.g. "PAY-2026-000001".</summary>
    public required string Reference { get; init; }

    public required decimal Amount { get; init; }
    public required string Currency { get; init; }

    /// <summary>
    /// Key that makes a retry return the original charge instead of raising a second one.
    /// Distinct from our own initiation idempotency key: this one stops us double-charging the
    /// customer, while ours stops us creating two payment rows.
    /// </summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>Payer's email. Card rails need it; a bank rail does not.</summary>
    public required string PayerEmail { get; init; }

    /// <summary>Where to send the payer to complete the charge, if the rail needs a redirect.</summary>
    public string? ReturnUrl { get; init; }
}

/// <summary>
/// What the gateway tells us about a charge it has just raised.
/// </summary>
/// <param name="ProviderRef">The gateway's own charge id. The only handle a webhook will give us.</param>
/// <param name="Status">One of <see cref="GatewayStatuses"/>.</param>
/// <param name="NextActionUrl">
/// Where the payer must go to complete the charge, or null for rails that settle in one call.
/// </param>
/// <param name="FailureReason">Populated when the charge could not be raised at all.</param>
public sealed record GatewayCharge(
    string ProviderRef,
    string Status,
    string? NextActionUrl = null,
    string? FailureReason = null);

/// <summary>
/// Status vocabulary an adapter reports back. Deliberately the smallest set that can describe a
/// charge's life, so every gateway maps onto it and the service has no per-provider branches.
/// </summary>
public static class GatewayStatuses
{
    /// <summary>Raised, awaiting the payer or an asynchronous confirmation.</summary>
    public const string Pending = "pending";

    /// <summary>Settled. Money is in.</summary>
    public const string Succeeded = "succeeded";

    /// <summary>Declined or errored. Will not settle on its own.</summary>
    public const string Failed = "failed";

    /// <summary>Started and then abandoned by the payer.</summary>
    public const string Cancelled = "cancelled";
}
