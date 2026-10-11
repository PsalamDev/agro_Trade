using AgroTrade.Application.Common;
using AgroTrade.Application.Intelligence;
using AgroTrade.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

[ApiController]
[Route("api/v1")]
public class MarketDataController(IMarketDataService marketData, IDestinationRequirementsService destinationRequirements) : ControllerBase
{
    [HttpGet("marketdata/prices")]
    [ProducesResponseType(typeof(PagedResult<MarketDataResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<MarketDataResponse>>> GetPrices(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? commodity = null,
        [FromQuery] string? country = null,
        [FromQuery] string? currency = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? sort = "reportedAtDesc",
        CancellationToken ct = default)
    {
        var req = new MarketDataListRequest(page, pageSize, commodity, country, currency, from, to, sort);
        return Ok(await marketData.ListAsync(req, ct));
    }

    [Authorize(Roles = RoleNames.Administrator)]
    [HttpGet("admin/marketdata")]
    [ProducesResponseType(typeof(PagedResult<MarketDataResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<MarketDataResponse>>> GetAdmin(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? commodity = null,
        [FromQuery] string? country = null,
        [FromQuery] string? currency = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? sort = "reportedAtDesc",
        CancellationToken ct = default)
    {
        var req = new MarketDataListRequest(page, pageSize, commodity, country, currency, from, to, sort);
        return Ok(await marketData.ListAsync(req, ct));
    }

    [Authorize(Roles = RoleNames.Administrator)]
    [HttpPost("admin/marketdata")]
    [ProducesResponseType(typeof(MarketDataResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<MarketDataResponse>> Create([FromBody] MarketDataCreateRequest request, CancellationToken ct = default)
    {
        var res = await marketData.CreateAsync(request, ct);
        return Created($"/api/v1/admin/marketdata/{res.Id}", res);
    }

    /// <summary>
    /// The documents a route is likely to need, from an origin/destination pair. Anonymous, like
    /// the price series it sits beside: it is reference information, useful before anyone signs in.
    /// </summary>
    [HttpGet("marketdata/destination-requirements")]
    [ProducesResponseType(typeof(DestinationRequirementsResponse), StatusCodes.Status200OK)]
    public ActionResult<DestinationRequirementsResponse> GetDestinationRequirements(
        [FromQuery] string? origin,
        [FromQuery] string? destination)
        => Ok(destinationRequirements.Get(origin ?? string.Empty, destination ?? string.Empty));
}