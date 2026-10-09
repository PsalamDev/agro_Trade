using AgroTrade.Application.Trade;
using AgroTrade.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

[ApiController]
[Route("api/v1")]
public class ReceiptsController(IReceiptService receipts) : ControllerBase
{
    [Authorize]
    [HttpPost("orders/{orderId:guid}/payments/{paymentId:guid}/receipt")]
    [ProducesResponseType(typeof(ReceiptResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<ReceiptResponse>> Generate(
        Guid orderId, Guid paymentId, CancellationToken ct)
    {
        var isAdmin = User.IsInRole(RoleNames.Administrator);
        var receipt = await receipts.GenerateReceiptAsync(User.GetUserId(), isAdmin, orderId, paymentId, ct);
        return Created($"/api/v1/orders/{orderId}/receipts/{receipt.Id}", receipt);
    }

    [Authorize]
    [HttpGet("orders/{orderId:guid}/receipts")]
    [ProducesResponseType(typeof(IReadOnlyList<ReceiptResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ReceiptResponse>>> List(Guid orderId, CancellationToken ct)
    {
        var isAdmin = User.IsInRole(RoleNames.Administrator);
        return Ok(await receipts.ListForOrderAsync(orderId, User.GetUserId(), isAdmin, ct));
    }
}
