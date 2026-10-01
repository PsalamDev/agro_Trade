using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Api.Controllers;

/// <summary>
/// Admin operations: business verification queue, review approve/reject with notes,
/// category management, and the trade document review queue (plan §03 — Admin).
/// </summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize(Policy = Policies.Administrator)]
public class AdminController(
    IBusinessService businessService,
    ITradeDocumentService documentService,
    IValidator<ReviewVerificationRequest> reviewValidator,
    IValidator<ReviewDocumentRequest> documentReviewValidator) : ControllerBase
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

    [HttpGet("documents")]
    [ProducesResponseType(typeof(PagedResult<TradeDocumentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TradeDocumentDto>>> SearchDocuments(
        [FromQuery] int? status, [FromQuery] int? docType, [FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        return Ok(await documentService.SearchQueueAsync(new TradeDocumentSearchQuery
        {
            Status = status is null ? null : (DocumentStatus)status.Value,
            DocType = docType is null ? null : (DocumentType)docType.Value,
            Q = q,
            Page = page,
            PageSize = pageSize,
        }, ct));
    }

    [HttpPost("documents/{id:guid}/review")]
    [ProducesResponseType(typeof(TradeDocumentDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<TradeDocumentDto>> ReviewDocument(
        Guid id, ReviewDocumentRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await documentReviewValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await documentService.ReviewAsync(id, User.GetUserId(), request, ct));
    }

    private static void EnsureBody(object? request)
    {
        if (request is null)
        {
            throw ApiException.BadRequest("INVALID_BODY", "A JSON request body is required.");
        }
    }
}