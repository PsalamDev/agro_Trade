using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using AgroTrade.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace AgroTrade.Infrastructure.Payments;

/// <summary>
/// Drives a sandbox charge to an outcome by dispatching a genuine signed event through the
/// real webhook handler.
///
/// <para>
/// Separate from <see cref="PaymentService"/> so the core payment service depends only on
/// <see cref="IPaymentGateway"/> and stays ignorant of which adapters exist. That matters once
/// a second rail is registered: a service that knows about the sandbox by name has to be
/// edited to add a real gateway, and would keep being edited for every provider after that.
/// </para>
///
/// <para>
/// It holds no state. The charge's own row is the state, and settlement happens by replaying an
/// event through exactly the path a live gateway would use, which is the only arrangement in
/// which the sandbox proves anything about signature, idempotency or ordering.
/// </para>
/// </summary>
public sealed class SandboxPaymentSimulator(
    IPaymentService payments,
    SandboxPaymentGateway gateway,
    ILogger<SandboxPaymentSimulator> logger) : ISandboxPaymentSimulator
{
    public async Task<PaymentDto> SettleAsync(
        Guid paymentId, Guid userId, SimulatePaymentRequest request, CancellationToken ct = default)
    {
        // Authorisation and the state checks live in the service, so the sandbox path enforces
        // exactly what a real settlement would.
        var payment = await payments.GetAsync(paymentId, userId, ct);

        if (payment.Provider != PaymentProvider.SandboxGateway)
        {
            throw ApiException.BadRequest(
                "NOT_SANDBOX",
                "Only sandbox charges can be settled from here.");
        }

        if (payment.Status != PaymentStatus.Pending)
        {
            throw ApiException.Conflict(
                "PAYMENT_ALREADY_SETTLED",
                $"This payment is already {payment.Status}.");
        }

        if (string.IsNullOrWhiteSpace(payment.ProviderRef))
        {
            throw ApiException.Conflict(
                "CHARGE_NOT_FOUND",
                "This payment has no gateway charge to settle.");
        }

        var succeeded = request.Outcome == SimulateOutcomes.Succeeded;
        var outcome = succeeded ? GatewayStatuses.Succeeded : GatewayStatuses.Failed;

        var envelope = gateway.BuildEvent(
            payment.ProviderRef,
            outcome,
            DateTime.UtcNow,
            succeeded ? null : "The issuer declined the charge.");

        logger.LogInformation(
            "Sandbox settling payment {Reference} as {Outcome} (event {EventId})",
            payment.Reference, request.Outcome, envelope.EventId);

        // In-process rather than over HTTP. Signature, deduplication, ordering, the ledger and
        // the order rollup all run exactly as they would for a delivered webhook; only the
        // network hop is skipped. The endpoint's transport contract — raw body, signature
        // header, status codes — is exercised separately by posting to it directly.
        await payments.HandleWebhookAsync(
            PaymentProvider.SandboxGateway, envelope.Signature, envelope.Payload, ct);

        return await payments.GetAsync(paymentId, userId, ct);
    }
}
