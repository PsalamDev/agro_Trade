using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

/// <summary>
/// Trade execution (plan §03 — Orders). Task 2.1: create an order from an accepted
/// quotation or by manual entry, and read buyer/seller order lists and detail.
/// Lifecycle transitions are task 2.2.
/// </summary>
[ApiController]
[Route("api/v1")]
public class OrdersController(
    IOrderService orderService,
    IValidator<CreateOrderFromQuotationRequest> fromQuotationValidator,
    IValidator<CreateManualOrderRequest> manualValidator,
    IValidator<OrderTransitionRequest> transitionValidator) : ControllerBase
{
    /// <summary>Converts an accepted quotation into an order, preserving all trade terms.</summary>
    [HttpPost("orders/from-quotation")]
    [Authorize(Policy = Policies.Buyer)]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<OrderDetailDto>> CreateFromQuotation(
        CreateOrderFromQuotationRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await fromQuotationValidator.ValidateAndThrowAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created,
            await orderService.CreateFromQuotationAsync(User.GetUserId(), request, ct));
    }

    /// <summary>Creates an order by hand, for trades that did not begin as an RFQ.</summary>
    [HttpPost("orders")]
    [Authorize(Policy = Policies.Buyer)]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<OrderDetailDto>> CreateManual(
        CreateManualOrderRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await manualValidator.ValidateAndThrowAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created,
            await orderService.CreateManualAsync(User.GetUserId(), request, ct));
    }

    /// <summary>Orders the caller placed as buyer.</summary>
    [HttpGet("orders")]
    [Authorize(Policy = Policies.Buyer)]
    [ProducesResponseType(typeof(PagedResult<OrderCardDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<OrderCardDto>>> Mine(
        [FromQuery] string? status, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        return Ok(await orderService.GetMineAsync(User.GetUserId(), new OrderListQuery
        {
            Status = status,
            Page = page,
            PageSize = pageSize,
        }, ct));
    }

    /// <summary>Orders placed against the caller's businesses, as seller.</summary>
    [HttpGet("orders/selling")]
    [Authorize(Policy = Policies.Seller)]
    [ProducesResponseType(typeof(PagedResult<OrderCardDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<OrderCardDto>>> Selling(
        [FromQuery] string? status, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        return Ok(await orderService.GetSellingAsync(User.GetUserId(), new OrderListQuery
        {
            Status = status,
            Page = page,
            PageSize = pageSize,
        }, ct));
    }

    /// <summary>Order detail. Visible only to the buyer and the selling business.</summary>
    [HttpGet("orders/{id:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderDetailDto>> Get(Guid id, CancellationToken ct)
    {
        return Ok(await orderService.GetAsync(id, User.GetUserId(), ct));
    }

    /// <summary>
    /// Moves an order to the next state. Which transitions exist, and which side of the trade
    /// may perform them, is decided by OrderLifecycle — the endpoint just forwards.
    /// </summary>
    [HttpPost("orders/{id:guid}/transitions")]
    [Authorize]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderDetailDto>> Transition(
        Guid id, OrderTransitionRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await transitionValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await orderService.TransitionAsync(id, User.GetUserId(), request, ct));
    }

    private static void EnsureBody(object? request)
    {
        if (request is null)
        {
            throw ApiException.BadRequest("INVALID_BODY", "A JSON request body is required.");
        }
    }
}