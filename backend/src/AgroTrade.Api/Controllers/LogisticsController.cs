using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using AgroTrade.Domain.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

/// <summary>
/// The logistics-provider directory and bookings against an order (Phase 4 — LSP directory +
/// booking). The directory list is public like the open RFQ directory; everything that names a
/// party or an order requires an account.
/// </summary>
[ApiController]
[Route("api/v1")]
public class LogisticsController(
    ILogisticsDirectoryService logistics,
    IValidator<UpsertLogisticsProviderRequest> upsertProviderValidator,
    IValidator<CreateLogisticsBookingRequest> bookingValidator,
    IValidator<RespondToLogisticsBookingRequest> respondValidator) : ControllerBase
{
    // ---- Directory ----

    /// <summary>
    /// Search the provider directory. Public: a provider directory is a front door, and a buyer
    /// deciding whether to register should be able to see who is listed.
    /// </summary>
    [HttpGet("logistics/providers")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResult<LogisticsProviderCardDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<LogisticsProviderCardDto>>> SearchProviders(
        [FromQuery] string? country, [FromQuery] int? mode, [FromQuery] int? service,
        [FromQuery] string? q, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        return Ok(await logistics.SearchProvidersAsync(new LogisticsProviderQuery
        {
            Country = country,
            Mode = ToEnum<ShipmentMode>(mode, "mode"),
            Service = ToEnum<LogisticsService>(service, "service"),
            Q = q,
            Page = page,
            PageSize = pageSize,
        }, ct));
    }

    /// <summary>The caller's own provider profile, or 404 if they have not created one yet.</summary>
    [HttpGet("logistics/providers/mine")]
    [Authorize(Policy = Policies.Lsp)]
    [ProducesResponseType(typeof(LogisticsProviderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LogisticsProviderDetailDto>> MyProvider(CancellationToken ct)
    {
        var provider = await logistics.GetMyProviderAsync(User.GetUserId(), ct);
        return provider is null
            ? NotFound(Problem("PROVIDER_NOT_FOUND", "You have not created a provider profile yet."))
            : Ok(provider);
    }

    /// <summary>Creates or replaces the caller's provider profile.</summary>
    [HttpPut("logistics/providers/mine")]
    [Authorize(Policy = Policies.Lsp)]
    [ProducesResponseType(typeof(LogisticsProviderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<LogisticsProviderDetailDto>> UpsertMyProvider(
        [FromBody] UpsertLogisticsProviderRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await upsertProviderValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await logistics.UpsertMyProviderAsync(User.GetUserId(), request, ct));
    }

    /// <summary>A provider's full profile, including contact details. Authenticated.</summary>
    [HttpGet("logistics/providers/{id:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(LogisticsProviderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LogisticsProviderDetailDto>> GetProvider(Guid id, CancellationToken ct)
    {
        return Ok(await logistics.GetProviderAsync(id, User.GetUserId(), IsAdmin(), ct));
    }

    // ---- Bookings ----

    /// <summary>
    /// Requests a booking with a provider against an order. Either party to the order may ask.
    /// </summary>
    [HttpPost("orders/{orderId:guid}/logistics-bookings")]
    [Authorize]
    [ProducesResponseType(typeof(LogisticsBookingDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LogisticsBookingDto>> RequestBooking(
        Guid orderId, [FromBody] CreateLogisticsBookingRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await bookingValidator.ValidateAndThrowAsync(request, ct);

        var booking = await logistics.RequestBookingAsync(orderId, User.GetUserId(), request, ct);

        return Created($"/api/v1/logistics/bookings/{booking.Id}", booking);
    }

    /// <summary>The bookings on an order. Visible to both parties.</summary>
    [HttpGet("orders/{orderId:guid}/logistics-bookings")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<LogisticsBookingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<LogisticsBookingDto>>> BookingsForOrder(
        Guid orderId, CancellationToken ct)
    {
        return Ok(await logistics.ListBookingsForOrderAsync(orderId, User.GetUserId(), IsAdmin(), ct));
    }

    /// <summary>Every booking the caller raised or was named in.</summary>
    [HttpGet("logistics/bookings")]
    [Authorize]
    [ProducesResponseType(typeof(PagedResult<LogisticsBookingDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<LogisticsBookingDto>>> MyBookings(
        [FromQuery] int? status, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        return Ok(await logistics.ListMyBookingsAsync(User.GetUserId(), new LogisticsBookingQuery
        {
            Status = ToEnum<LogisticsBookingStatus>(status, "status"),
            Page = page,
            PageSize = pageSize,
        }, ct));
    }

    /// <summary>A single booking, for a party to it.</summary>
    [HttpGet("logistics/bookings/{id:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(LogisticsBookingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LogisticsBookingDto>> GetBooking(Guid id, CancellationToken ct)
    {
        return Ok(await logistics.GetBookingAsync(id, User.GetUserId(), IsAdmin(), ct));
    }

    /// <summary>Accepts or declines an open request. The named provider only.</summary>
    [HttpPost("logistics/bookings/{id:guid}/respond")]
    [Authorize(Policy = Policies.Lsp)]
    [ProducesResponseType(typeof(LogisticsBookingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LogisticsBookingDto>> Respond(
        Guid id, [FromBody] RespondToLogisticsBookingRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await respondValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await logistics.RespondAsync(id, User.GetUserId(), request, ct));
    }

    /// <summary>Withdraws an un-answered request. The requester only.</summary>
    [HttpPost("logistics/bookings/{id:guid}/cancel")]
    [Authorize]
    [ProducesResponseType(typeof(LogisticsBookingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LogisticsBookingDto>> Cancel(Guid id, CancellationToken ct)
    {
        return Ok(await logistics.CancelAsync(id, User.GetUserId(), ct));
    }

    /// <summary>Marks an accepted booking as fulfilled. The requester only.</summary>
    [HttpPost("logistics/bookings/{id:guid}/complete")]
    [Authorize]
    [ProducesResponseType(typeof(LogisticsBookingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LogisticsBookingDto>> Complete(Guid id, CancellationToken ct)
    {
        return Ok(await logistics.CompleteAsync(id, User.GetUserId(), ct));
    }

    private bool IsAdmin() =>
        User.IsInRole(AgroTrade.Domain.Enums.RoleNames.Administrator);

    /// <summary>
    /// Converts an optional int query value to an enum, refusing an unrecognised one with a clear
    /// 400 rather than silently treating it as "any" — the same rule the shipment status filter
    /// follows, so a typo never reads as an empty result set.
    /// </summary>
    private static TEnum? ToEnum<TEnum>(int? value, string name) where TEnum : struct, Enum
    {
        if (value is not int v)
        {
            return null;
        }

        if (!Enum.IsDefined(typeof(TEnum), v))
        {
            throw ApiException.BadRequest(
                "INVALID_FILTER",
                $"'{value}' is not a valid {name}. Use one of: {string.Join(", ", Enum.GetValues<TEnum>().Select(e => (int)(object)e))}.");
        }

        return (TEnum)(object)v;
    }

    private static void EnsureBody(object? request)
    {
        if (request is null)
        {
            throw ApiException.BadRequest("INVALID_BODY", "A JSON request body is required.");
        }
    }
}
