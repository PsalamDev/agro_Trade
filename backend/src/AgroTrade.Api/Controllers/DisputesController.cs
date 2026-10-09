using AgroTrade.Application.Trade;
using AgroTrade.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

[ApiController]
[Route("api/v1")]
public class DisputesController(IDisputeService disputes) : ControllerBase
{
    [Authorize]
    [HttpPost("disputes")]
    [ProducesResponseType(typeof(DisputeResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<DisputeResponse>> Create([FromBody] DisputeCreateRequest request, CancellationToken ct = default)
    {
        var d = await disputes.CreateAsync(User.GetUserId(), request, ct);
        return Created($"/api/v1/disputes/{d.Id}", d);
    }

    [Authorize(Roles = RoleNames.Administrator)]
    [HttpGet("admin/disputes/open")]
    [ProducesResponseType(typeof(IReadOnlyList<DisputeResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DisputeResponse>>> ListOpen(CancellationToken ct = default)
    {
        return Ok(await disputes.ListOpenAsync(ct));
    }

    [Authorize(Roles = RoleNames.Administrator)]
    [HttpPost("admin/disputes/{id:guid}/resolve")]
    [ProducesResponseType(typeof(DisputeResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DisputeResponse>> Resolve(Guid id, [FromBody] DisputeResolveRequest request, CancellationToken ct = default)
    {
        var d = await disputes.ResolveAsync(id, User.GetUserId(), request, ct);
        return Ok(d);
    }
}