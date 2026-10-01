using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

/// <summary>
/// Per-listing stock (plan §02 — Inventory). Bookable stock is OnHand minus Reserved; orders
/// hold a reservation while they are being prepared, so this is the figure a buyer sees and
/// the one an order is checked against.
/// </summary>
[ApiController]
[Route("api/v1/products")]
public class InventoryController(
    IInventoryService inventoryService,
    IValidator<SetStockRequest> stockValidator) : ControllerBase
{
    /// <summary>Current stock for a listing. Public — buyers need it before ordering.</summary>
    [HttpGet("{id:guid}/stock")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(InventoryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<InventoryDto>> Get(Guid id, CancellationToken ct)
    {
        return Ok(await inventoryService.GetAsync(id, ct));
    }

    /// <summary>Declares the seller's stock level. Listing owner only.</summary>
    [HttpPut("{id:guid}/stock")]
    [Authorize]
    [ProducesResponseType(typeof(InventoryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<InventoryDto>> SetStock(
        Guid id, SetStockRequest request, CancellationToken ct)
    {
        if (request is null)
        {
            throw ApiException.BadRequest("INVALID_BODY", "A JSON request body is required.");
        }

        await stockValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await inventoryService.SetStockAsync(id, User.GetUserId(), request, ct));
    }
}
