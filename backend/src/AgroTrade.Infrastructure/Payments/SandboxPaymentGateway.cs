using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgroTrade.Application.Trade;
using AgroTrade.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgroTrade.Infrastructure.Payments;

/// <summary>
/// The task 2.4 adapter: a sandbox gateway for one corridor (roadmap §5.5).
///
/// <para>
/// It exists to prove the payment <em>pipeline</em> — charge, webhook, signature, idempotency,
/// ordering, ledger, rollup — before any real money is involved, and to do so against the same
/// interface a live provider will implement. It settles nothing and moves no funds.
/// </para>
///
/// <para>
/// A sandbox that resolved payments by fiat would prove nothing about the part that actually
/// breaks in production, which is the asynchronous, at-least-once, unordered webhook. So this
/// adapter does <em>not</em> settle a charge inline. It raises a pending charge with a
/// checkout URL, and the charge is settled later by
/// <c>IPaymentService.SimulateSettlementAsync</c>, which uses <see cref="BuildEvent"/> to
/// construct a genuine signed envelope and pushes it through
/// <c>IPaymentService.HandleWebhookAsync</c> — the same verification and deduplication code the
/// HTTP endpoint runs. The only thing the sandbox short-circuits is the network hop, and the
/// endpoint's transport contract is covered separately by posting to it directly.
/// </para>
/// </summary>
public sealed class SandboxPaymentGateway(
    IOptions<PaymentOptions> options,
    ILogger<SandboxPaymentGateway> logger) : IPaymentGateway
{
    private readonly PaymentOptions options = options.Value;

    public PaymentProvider Provider => PaymentProvider.SandboxGateway;

    public IReadOnlyCollection<string> SupportedCurrencies =>
        options.SupportedCurrencies.Select(c => c.ToUpperInvariant()).ToArray();

    public Task<GatewayCharge> CreateChargeAsync(GatewayChargeRequest request, CancellationToken ct = default)
    {
        if (request.Amount <= 0)
        {
            return Task.FromResult(Failed("AMOUNT_INVALID", "The charge amount must be greater than zero."));
        }

        if (!SupportedCurrencies.Contains(request.Currency.ToUpperInvariant()))
        {
            // The corridor limit. Refusing here means no orphan charge exists at a provider that
            // was never going to settle it.
            return Task.FromResult(Failed(
                "CURRENCY_UNSUPPORTED",
                $"This gateway settles {string.Join(", ", SupportedCurrencies)} only."));
        }

        var providerRef = $"sbx_{Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant()}";

        logger.LogInformation(
            "Sandbox charge {ProviderRef} raised for {Amount} {Currency} (payment {PaymentId}, ref {Reference})",
            providerRef, request.Amount, request.Currency, request.PaymentId, request.Reference);

        return Task.FromResult(new GatewayCharge(
            providerRef,
            GatewayStatuses.Pending,
            NextActionUrl: CheckoutUrl(providerRef, request.Reference),
            FailureReason: null));
    }

    /// <summary>
    /// Builds a signed event envelope for a settled charge, exactly as the gateway would have
    /// delivered it.
    /// </summary>
    /// <remarks>
    /// The timestamp is a parameter rather than read from the clock so a test can construct an
    /// event that is genuinely older than one already applied, which is the only honest way to
    /// exercise the out-of-order path.
    /// </remarks>
    public SandboxWebhookEvent BuildEvent(
        string providerRef,
        string outcome,
        DateTime occurredAtUtc,
        string? failureReason = null)
    {
        var eventId = $"evt_{Convert.ToHexString(RandomNumberGenerator.GetBytes(10)).ToLowerInvariant()}";

        // camelCase, like every real gateway's payload. Using the plain serializer defaults here
        // would emit PascalCase and quietly exempt the sandbox from the casing a real provider
        // sends, which is exactly the mismatch the handler would then be blamed for.
        var payload = JsonSerializer.Serialize(new SandboxWebhookPayload(
            Id: eventId,
            Type: $"payment.{outcome}",
            ChargeRef: providerRef,
            OccurredAtUtc: occurredAtUtc,
            FailureReason: failureReason), PaymentService.WebJson);

        return new SandboxWebhookEvent(eventId, Sign(payload), payload);
    }

    /// <summary>
    /// HMAC-SHA256 over the exact body bytes, hex encoded. The signature covers the bytes as
    /// received, which is why the payload string is signed rather than re-serialised later.
    /// </summary>
    public string Sign(string rawBody) =>
        Convert.ToHexString(
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(options.SigningSecret),
                Encoding.UTF8.GetBytes(rawBody)))
            .ToLowerInvariant();

    /// <inheritdoc />
    public bool VerifyWebhook(string rawBody, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        // Accept the common "sha256=" prefix without requiring it, since a real provider's
        // header format is its own business.
        var provided = signature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
            ? signature[7..]
            : signature;

        byte[] expected;
        byte[] actual;
        try
        {
            expected = Convert.FromHexString(Sign(rawBody));
            actual = Convert.FromHexString(provided);
        }
        catch (FormatException)
        {
            // Not hex at all. Nothing to compare; treat as a bad signature rather than a 400.
            return false;
        }

        // FixedTimeEquals on purpose: an ordinary string comparison returns at the first
        // differing byte, leaking how much of a forged signature was correct. That is a slow
        // oracle to exploit against HMAC, but it costs nothing to not have one.
        return expected.Length == actual.Length
            && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private string CheckoutUrl(string providerRef, string reference)
    {
        var path = $"/dashboard/orders/pay/{providerRef}";
        return string.IsNullOrWhiteSpace(options.PublicBaseUrl)
            ? path
            : options.PublicBaseUrl.TrimEnd('/') + path;
    }

    private static GatewayCharge Failed(string code, string reason) =>
        new(string.Empty, GatewayStatuses.Failed, NextActionUrl: null, FailureReason: $"{code}: {reason}");
}

/// <summary>The sandbox gateway's wire shape. Named, not anonymous, so it can be documented.</summary>
/// <param name="Id">Event id; the idempotency key together with the provider.</param>
/// <param name="Type">Event type, e.g. "payment.succeeded".</param>
/// <param name="ChargeRef">The gateway's charge id, matching <c>Payment.ProviderRef</c>.</param>
/// <param name="OccurredAtUtc">When the event happened, used for the ordering decision.</param>
/// <param name="FailureReason">Present only on a failure.</param>
public sealed record SandboxWebhookPayload(
    string Id,
    string Type,
    string ChargeRef,
    DateTime OccurredAtUtc,
    string? FailureReason);

/// <summary>A signed sandbox event, ready to be fed to the webhook handler.</summary>
public sealed record SandboxWebhookEvent(string EventId, string Signature, string Payload);
