using AgroTrade.Application.Admin;
using AgroTrade.Application.Admin.Validation;
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
    IAdminService admin,
    IBusinessService businessService,
    ITradeDocumentService documentService,
    IValidator<ReviewVerificationRequest> reviewValidator,
    IValidator<ReviewDocumentRequest> documentReviewValidator,
    IValidator<ModerateProductRequest> moderationValidator) : ControllerBase
{
    /// <summary>
    /// Everything the dashboard shows, in one read: platform counts, the four funnels, corridor
    /// value, category supply against demand, and RFQ demand.
    /// </summary>
    /// <remarks>
    /// One endpoint rather than eight, because the dashboard is one screen. Splitting it would mean
    /// the page fires a fan of requests that each re-read the same tables, and an admin refreshing
    /// the page would see counts from different moments if they landed at different times.
    /// </remarks>
    [HttpGet("overview")]
    [ProducesResponseType(typeof(AdminOverviewDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminOverviewDto>> Overview(CancellationToken ct)
    {
        return Ok(await admin.GetOverviewAsync(ct));
    }

    /// <summary>
    /// The audit trail of administrative actions, newest first.
    /// </summary>
    [HttpGet("audit-log")]
    [ProducesResponseType(typeof(PagedResult<AdminAuditLogDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AdminAuditLogDto>>> AuditLog(
        [FromQuery] string? action, [FromQuery] Guid? userId, [FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        return Ok(await admin.SearchAuditLogAsync(new AdminAuditLogQuery
        {
            Action = action,
            UserId = userId,
            Q = q,
            Page = page,
            PageSize = pageSize,
        }, ct));
    }

    [HttpGet("users")]
    [ProducesResponseType(typeof(PagedResult<AdminUserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AdminUserDto>>> Users(
        [FromQuery] string? q, [FromQuery] string? role, [FromQuery] bool? emailConfirmed,
        [FromQuery] bool? lockedOut, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        return Ok(await admin.SearchUsersAsync(new AdminUserSearchQuery
        {
            Q = q,
            Role = role,
            EmailConfirmed = emailConfirmed,
            LockedOut = lockedOut,
            Page = page,
            PageSize = pageSize,
        }, ct));
    }

    /// <summary>Every consignment across every order, rather than one party's.</summary>
    [HttpGet("shipments")]
    [ProducesResponseType(typeof(PagedResult<AdminShipmentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AdminShipmentDto>>> Shipments(
        [FromQuery] int? status, [FromQuery] string? q, [FromQuery] bool? hasGuestTracking,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        return Ok(await admin.SearchShipmentsAsync(new AdminShipmentSearchQuery
        {
            Status = status,
            Q = q,
            HasGuestTracking = hasGuestTracking,
            Page = page,
            PageSize = pageSize,
        }, ct));
    }

    [HttpGet("orders")]
    [ProducesResponseType(typeof(PagedResult<AdminOrderRowDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AdminOrderRowDto>>> Orders(
        [FromQuery] int? status, [FromQuery] int? paymentStatus, [FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        return Ok(await admin.SearchOrdersAsync(new AdminOrderSearchQuery
        {
            Status = status,
            PaymentStatus = paymentStatus,
            Q = q,
            Page = page,
            PageSize = pageSize,
        }, ct));
    }

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

    [HttpGet("products")]
    [ProducesResponseType(typeof(PagedResult<AdminProductRowDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AdminProductRowDto>>> Products(
        [FromQuery] int? listingStatus, [FromQuery] int? moderationStatus, [FromQuery] string? q,
        [FromQuery] Guid? categoryId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        return Ok(await admin.SearchProductsAsync(new AdminProductSearchQuery
        {
            ListingStatus = listingStatus,
            ModerationStatus = moderationStatus,
            Q = q,
            CategoryId = categoryId,
            Page = page,
            PageSize = pageSize,
        }, ct));
    }

    [HttpPatch("products/{id:guid}/moderation")]
    [ProducesResponseType(typeof(AdminProductRowDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminProductRowDto>> ModerateProduct(
        Guid id, [FromBody] ModerateProductRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await moderationValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await admin.ModerateProductAsync(id, User.GetUserId(), new ModerateProductCommand
        {
            Action = request.Action,
            Reason = request.Reason,
        }, ct));
    }

    [HttpGet("rfqs")]
    [ProducesResponseType(typeof(PagedResult<AdminRfqRowDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AdminRfqRowDto>>> Rfqs(
        [FromQuery] int? status, [FromQuery] string? q, [FromQuery] bool? unanswered,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        return Ok(await admin.SearchRfqsAsync(new AdminRfqSearchQuery
        {
            Status = status,
            Q = q,
            Unanswered = unanswered,
            Page = page,
            PageSize = pageSize,
        }, ct));
    }

    private static void EnsureBody(object? request)
    {
        if (request is null)
        {
            throw ApiException.BadRequest("INVALID_BODY", "A JSON request body is required.");
        }
    }
}