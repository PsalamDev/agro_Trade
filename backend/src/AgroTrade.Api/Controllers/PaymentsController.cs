using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using AgroTrade.Domain.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

/// <summary>
/// Payments against an order, and the gateway webhook endpoint (plan §02 — Payments).
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize]
public class PaymentsController(
    IPaymentService payments,
    ISandboxPaymentSimulator sandbox,
    IValidator<InitiatePaymentRequest> initiateValidator,
    IValidator<SimulatePaymentRequest> simulateValidator) : ControllerBase
{
    /// <summary>
    /// Raises a gateway charge against an order. Buyer only.
    /// </summary>
    /// <remarks>
    /// Retrying with the same <c>idempotencyKey</c> returns the original payment rather than
    /// raising a second charge, so a client that times out mid-request can simply try again.
    /// </remarks>
    [HttpPost("orders/{orderId:guid}/payments")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<PaymentDto>> Initiate(
        Guid orderId, [FromBody] InitiatePaymentRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await initiateValidator.ValidateAndThrowAsync(request, ct);

        var payment = await payments.InitiateAsync(orderId, User.GetUserId(), request, ct);

        // Created even on an idempotent replay: the payment exists at this address either way,
        // and the address is what a client needs in order to poll it for settlement.
        return Created($"/api/v1/payments/{payment.Id}", payment);
    }

    /// <summary>Payments on an order. Visible to both parties.</summary>
    [HttpGet("orders/{orderId:guid}/payments")]
    [ProducesResponseType(typeof(IReadOnlyList<PaymentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PaymentDto>>> ListForOrder(Guid orderId, CancellationToken ct)
    {
        return Ok(await payments.ListForOrderAsync(orderId, User.GetUserId(), ct));
    }

    [HttpGet("payments/{paymentId:guid}")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PaymentDto>> Get(Guid paymentId, CancellationToken ct)
    {
        return Ok(await payments.GetAsync(paymentId, User.GetUserId(), ct));
    }

    /// <summary>
    /// Sandbox checkout: settle a charge as succeeded or failed. Dispatches a real signed
    /// webhook through the production handler.
    /// </summary>
    /// <remarks>
    /// Scoped under the provider name so it is unmistakably a sandbox affordance rather than
    /// something to carry into a real provider, where settlement is the gateway's job.
    /// </remarks>
    [HttpPost("sandbox/payments/{paymentId:guid}/settle")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PaymentDto>> Settle(
        Guid paymentId, [FromBody] SimulatePaymentRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await simulateValidator.ValidateAndThrowAsync(request, ct);

        return Ok(await sandbox.SettleAsync(paymentId, User.GetUserId(), request, ct));
    }

    private static void EnsureBody(object? request)
    {
        if (request is null)
        {
            throw ApiException.BadRequest("INVALID_BODY", "A JSON request body is required.");
        }
    }
}

/// <summary>
/// Inbound gateway webhooks. Unauthenticated by necessity and authenticated by signature
/// instead: the caller holds no account with us and cannot send a bearer token.
/// </summary>
/// <remarks>
/// Kept in its own controller so the <c>[Authorize]</c> above does not have to be undone per
/// action, and so the exemption is visible in one place.
/// </remarks>
[ApiController]
[Route("api/v1")]
[AllowAnonymous]
public class PaymentWebhooksController(IPaymentService payments) : ControllerBase
{
    /// <summary>Header the gateway signs the body with.</summary>
    private const string SignatureHeader = "X-Sandbox-Signature";

    /// <summary>
    /// Receives one gateway event.
    /// </summary>
    /// <remarks>
    /// Takes the body as a raw string rather than a bound model, because the signature covers
    /// the exact bytes received. Binding to a DTO and re-serialising would produce different
    /// bytes and fail verification.
    /// </remarks>
    [HttpPost("payments/webhooks/{provider}")]
    [ProducesResponseType(typeof(WebhookAck), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WebhookAck>> Receive(string provider, CancellationToken ct)
    {
        if (!Enum.TryParse<PaymentProvider>(provider, ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            throw ApiException.NotFound("WEBHOOK_UNKNOWN_PROVIDER", "Unknown payment provider.");
        }

        using var reader = new StreamReader(Request.Body, System.Text.Encoding.UTF8);
        var rawBody = await reader.ReadToEndAsync(ct);

        if (string.IsNullOrWhiteSpace(rawBody))
        {
            throw ApiException.BadRequest("WEBHOOK_EMPTY", "The webhook body was empty.");
        }

        var signature = Request.Headers[SignatureHeader].FirstOrDefault();

        // Any 2xx here means "stop sending". A duplicate is reported as a duplicate in the
        // body, not as an error status, because a non-2xx would instruct the gateway to keep
        // redelivering the event we have already applied.
        return Ok(await payments.HandleWebhookAsync(parsed, signature, rawBody, ct));
    }
}
