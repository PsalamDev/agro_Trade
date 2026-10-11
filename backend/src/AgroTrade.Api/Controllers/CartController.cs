using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

/// <summary>
/// Domestic buy-now / cart (roadmap Phase 4). Buyer-side endpoints only: a cart belongs to
/// one buyer, and checkout fans it out into one order per seller with a delivery address,
/// a summed delivery fee and stock reserved across the whole cart at once.
/// </summary>
[ApiController]
[Route("api/v1")]
public class CartController(
    ICartService cartService,
    IValidator<AddToCartRequest> addValidator,
    IValidator<SetCartItemQuantityRequest> setQuantityValidator,
    IValidator<CheckoutRequest> checkoutValidator) : ControllerBase
{
    /// <summary>The caller's cart, with live listing prices and delivery fees.</summary>
    [HttpGet("carts/mine")]
    [Authorize(Policy = Policies.Buyer)]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CartDto>> Mine(CancellationToken ct)
    {
        return Ok(await cartService.GetMineAsync(User.GetUserId(), ct));
    }

    /// <summary>Adds a listing to the cart. Increments the line when it is already there.</summary>
    [HttpPost("carts/items")]
    [Authorize(Policy = Policies.Buyer)]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CartDto>> Add(AddToCartRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await addValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await cartService.AddAsync(User.GetUserId(), request, ct));
    }

    /// <summary>Sets a line's quantity to an absolute value. Use DELETE to remove the line.</summary>
    [HttpPut("carts/items/{productId:guid}")]
    [Authorize(Policy = Policies.Buyer)]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CartDto>> SetQuantity(
        Guid productId, SetCartItemQuantityRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await setQuantityValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await cartService.SetQuantityAsync(User.GetUserId(), productId, request, ct));
    }

    /// <summary>Removes a line from the cart. Idempotent — a missing line is not an error.</summary>
    [HttpDelete("carts/items/{productId:guid}")]
    [Authorize(Policy = Policies.Buyer)]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CartDto>> Remove(Guid productId, CancellationToken ct)
    {
        return Ok(await cartService.RemoveAsync(User.GetUserId(), productId, ct));
    }

    /// <summary>
    /// Converts the cart into orders — one per seller — with a delivery address and delivery
    /// fees, and holds stock for the whole cart atomically. The cart is emptied by a
    /// successful checkout, so it can never be placed twice.
    /// </summary>
    [HttpPost("carts/checkout")]
    [Authorize(Policy = Policies.Buyer)]
    [ProducesResponseType(typeof(CheckoutResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CheckoutResultDto>> Checkout(CheckoutRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await checkoutValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await cartService.CheckoutAsync(User.GetUserId(), request, ct));
    }

    private static void EnsureBody(object request)
    {
        if (request is null)
        {
            throw ApiException.BadRequest("EMPTY_BODY", "Request body is required.");
        }
    }
}