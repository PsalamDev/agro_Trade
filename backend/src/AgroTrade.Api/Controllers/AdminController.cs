using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

/// <summary>
/// Admin operations: business verification queue, review approve/reject with notes,
/// and category management (plan §03 — Admin).
/// </summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize(Policy = Policies.Administrator)]
public class AdminController(
    IBusinessService businessService,
    IValidator<ReviewVerificationRequest> reviewValidator) : ControllerBase
{
    [HttpGet("businesses")]
    [ProducesResponseType(typeof(PagedResult<BusinessSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<BusinessSummaryDto>>> SearchBusinesses(
        [FromQuery] int? status, string? q, int page = 1, int pageSize = 20,
        CancellationToken ct = default)
    {
        return Ok(await businessService.AdminSearchAsync(new AdminBusinessSearchQuery
        {
            Status = status,
            Q = q,
            Page = page,
            PageSize = pageSize,
        }, ct));
    }

    [HttpPost("businesses/{id:guid}/verification/review")]
    [ProducesResponseType(typeof(BusinessDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<BusinessDetailDto>> ReviewVerification(
        Guid id, ReviewVerificationRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await reviewValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await businessService.ReviewVerificationAsync(id, User.GetUserId(), request, ct));
    }

    private static void EnsureBody(object? request)
    {
        if (request is null)
        {
            throw ApiException.BadRequest("INVALID_BODY", "A JSON request body is required.");
        }
    }
}