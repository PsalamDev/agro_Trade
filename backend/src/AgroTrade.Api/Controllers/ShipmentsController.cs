using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using AgroTrade.Domain.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

/// <summary>
/// Consignments against an order and their tracking milestones (plan §02 — Shipments).
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize]
public class ShipmentsController(
    IShipmentService shipments,
    IValidator<CreateShipmentRequest> createValidator,
    IValidator<AppendTrackingEventRequest> eventValidator) : ControllerBase
{
    /// <summary>
    /// Records the consignment for an order and writes its first milestone. Seller only.
    /// </summary>
    [HttpPost("orders/{orderId:guid}/shipments")]
    [ProducesResponseType(typeof(ShipmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ShipmentDto>> Create(
        Guid orderId, [FromBody] CreateShipmentRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await createValidator.ValidateAndThrowAsync(request, ct);

        var shipment = await shipments.CreateAsync(orderId, User.GetUserId(), request, ct);

        return Created($"/api/v1/shipments/{shipment.Id}", shipment);
    }

    /// <summary>
    /// The order's consignments. Visible to both parties.
    /// </summary>
    /// <remarks>
    /// A list although there is at most one today: splitting a consignment across order items is a
    /// real pattern this model does not yet express, and returning an array now means adding it
    /// later is not a breaking change for every client holding a parsed object.
    /// </remarks>
    [HttpGet("orders/{orderId:guid}/shipments")]
    [ProducesResponseType(typeof(IReadOnlyList<ShipmentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ShipmentDto>>> ListForOrder(Guid orderId, CancellationToken ct)
    {
        return Ok(await shipments.ListForOrderAsync(
            orderId, User.GetUserId(), IsAdmin, ct));
    }

    /// <summary>
    /// Every consignment the caller is party to, most recently moved first.
    /// </summary>
    /// <remarks>
    /// <c>as</c> rather than <c>role</c> because it reads as the caller's position in the trade,
    /// and because there is no carrier account to offer as a third value yet.
    /// </remarks>
    [HttpGet("shipments")]
    [ProducesResponseType(typeof(IReadOnlyList<ShipmentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ShipmentDto>>> List(
        [FromQuery] string? @as,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        var query = new ShipmentQuery
        {
            Role = @as?.Trim().ToLowerInvariant() switch
            {
                null or "" => null,
                "buyer" => ShipmentRole.Buyer,
                "seller" => ShipmentRole.Seller,
                _ => throw ApiException.BadRequest(
                    "INVALID_ROLE", "The 'as' filter must be 'buyer' or 'seller'."),
            },
            Status = ParseStatus(status),
        };

        return Ok(await shipments.ListAsync(User.GetUserId(), IsAdmin, query, ct));
    }

    /// <summary>A consignment with its full timeline. Visible to both parties.</summary>
    [HttpGet("shipments/{shipmentId:guid}")]
    [ProducesResponseType(typeof(ShipmentDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ShipmentDto>> Get(Guid shipmentId, CancellationToken ct)
    {
        return Ok(await shipments.GetAsync(shipmentId, User.GetUserId(), IsAdmin, ct));
    }

    /// <summary>
    /// Appends a tracking milestone. Seller only, and permanent: there is no way to edit or delete
    /// one, because a shipment timeline is the evidence a buyer relies on if goods arrive late.
    /// </summary>
    [HttpPost("shipments/{shipmentId:guid}/tracking-events")]
    [ProducesResponseType(typeof(ShipmentTrackingEventDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ShipmentTrackingEventDto>> AppendEvent(
        Guid shipmentId, [FromBody] AppendTrackingEventRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await eventValidator.ValidateAndThrowAsync(request, ct);

        var milestone = await shipments.AppendEventAsync(shipmentId, User.GetUserId(), request, ct);

        return Created($"/api/v1/shipments/{shipmentId}/tracking-events/{milestone.Id}", milestone);
    }

    /// <summary>
    /// Issues a fresh guest tracking token, invalidating the previous link. Seller only.
    /// </summary>
    /// <remarks>
    /// The raw token is in the response and nowhere else, ever — not in a read endpoint, not in
    /// the order detail payload, not in the database, which stores only its hash. If it is lost
    /// here it must be rotated; there is no endpoint that can recover it.
    /// </remarks>
    [HttpPost("shipments/{shipmentId:guid}/tracking-token")]
    [ProducesResponseType(typeof(TrackingTokenDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TrackingTokenDto>> RotateTrackingToken(Guid shipmentId, CancellationToken ct)
    {
        return Ok(await shipments.RotateTrackingTokenAsync(shipmentId, User.GetUserId(), ct));
    }

    private bool IsAdmin => User.IsInRole(RoleNames.Administrator);

    /// <summary>
    /// Accepts a status as a name or a number. Named over <c>Enum.TryParse</c> alone because the
    /// filter lives in a query string, where a typo should be a clear 400 rather than an empty list
    /// that reads as "no shipments match".
    /// </summary>
    private static ShipmentStatus? ParseStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (Enum.TryParse<ShipmentStatus>(raw.Trim(), ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw ApiException.BadRequest(
            "INVALID_SHIPMENT_STATUS",
            $"'{raw}' is not a shipment status. Use one of: {string.Join(", ", Enum.GetNames<ShipmentStatus>())}.");
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
/// Public consignment tracking, for a buyer forwarding a link to a freight forwarder, customs
/// agent or insurer who has no account here.
/// </summary>
/// <remarks>
/// In its own controller so the <c>[Authorize]</c> on the authenticated one does not have to be
/// undone per action, and so the single exemption is visible in one place.
/// </remarks>
[ApiController]
[Route("api/v1")]
[AllowAnonymous]
public class ShipmentTrackingController(IShipmentService shipments) : ControllerBase
{
    /// <summary>
    /// Consignment status and timeline for a guest token.
    /// </summary>
    /// <remarks>
    /// Returns a deliberately reduced payload — route, carrier, reference, ETA, milestones — and
    /// nothing about the order, the parties, the goods or the money. Someone holding a forwarded
    /// link needs to know where the container is, and has no business learning who traded what for
    /// how much. See <see cref="ShipmentPublicTrackingDto"/>.
    /// </remarks>
    [HttpGet("shipments/track/{token}")]
    [ProducesResponseType(typeof(ShipmentPublicTrackingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ShipmentPublicTrackingDto>> Track(string token, CancellationToken ct)
    {
        return Ok(await shipments.TrackAsync(token, ct));
    }
}