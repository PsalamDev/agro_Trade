using AgroTrade.Application.Trade;
using AgroTrade.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

[ApiController]
[Route("api/v1")]
public class InvoicesController(IInvoiceService invoices) : ControllerBase
{
    [Authorize(Roles = $"{RoleNames.Company},{RoleNames.Administrator}")]
    [HttpPost("orders/{orderId:guid}/invoice")]
    [ProducesResponseType(typeof(InvoiceResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<InvoiceResponse>> Generate(
        Guid orderId, [FromBody] GenerateInvoiceRequest request, CancellationToken ct)
    {
        var req = request with { OrderId = orderId };
        var isAdmin = User.IsInRole(RoleNames.Administrator);
        var invoice = await invoices.GenerateInvoiceAsync(User.GetUserId(), isAdmin, req, ct);
        return Created($"/api/v1/orders/{orderId}/invoice", invoice);
    }

    [Authorize]
    [HttpGet("orders/{orderId:guid}/invoice")]
    [ProducesResponseType(typeof(InvoiceResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<InvoiceResponse>> Get(Guid orderId, CancellationToken ct)
    {
        var isAdmin = User.IsInRole(RoleNames.Administrator);
        return Ok(await invoices.GetInvoiceAsync(orderId, User.GetUserId(), isAdmin, ct));
    }
}
