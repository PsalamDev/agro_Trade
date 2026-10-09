using AgroTrade.Application.Common;
using AgroTrade.Application.Trust;
using AgroTrade.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

[ApiController]
[Route("api/v1")]
public class ReviewsController(IReviewService reviews) : ControllerBase
{
    [HttpGet("businesses/{businessId:guid}/reviews")]
    [ProducesResponseType(typeof(PagedResult<ReviewResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ReviewResponse>>> List(Guid businessId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        return Ok(await reviews.ListAsync(new ReviewListRequest(page, pageSize, businessId, true), ct));
    }

    [Authorize]
    [HttpPost("reviews")]
    [ProducesResponseType(typeof(ReviewResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<ReviewResponse>> Create([FromBody] ReviewCreateRequest request, CancellationToken ct = default)
    {
        var r = await reviews.CreateAsync(User.GetUserId(), request, ct);
        return Created($"/api/v1/reviews/{r.Id}", r);
    }

    [Authorize(Roles = RoleNames.Administrator)]
    [HttpPost("admin/reviews/{id:guid}/publish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct = default)
    {
        await reviews.PublishAsync(id, ct);
        return NoContent();
    }
}