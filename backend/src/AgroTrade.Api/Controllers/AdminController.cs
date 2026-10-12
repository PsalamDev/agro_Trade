using AgroTrade.Application.Admin;
using AgroTrade.Application.Admin.Validation;
using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AgroTrade.Domain.Enums;
using System.Text;

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
    IPaymentService payments,
    IValidator<ReviewVerificationRequest> reviewValidator,
    IValidator<ReviewDocumentRequest> documentReviewValidator,
    IValidator<ModerateProductRequest> moderationValidator,
    IValidator<AdminUserStatusRequest> userStatusValidator) : ControllerBase
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

    /// <summary>
    /// The audit trail as a CSV file, under the same filters as the list.
    /// </summary>
    /// <remarks>
    /// A route of its own rather than a <c>format</c> parameter on the list, because the two are read
    /// by different things: the list is paged for a screen, the file is taken away whole. The response
    /// streams, so a large trail is not held in server memory before it is sent.
    /// </remarks>
    [HttpGet("audit-log/export")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAuditLog(
        [FromQuery] string? action, [FromQuery] Guid? userId, [FromQuery] string? q,
        CancellationToken ct = default)
    {
        return await CsvDownloadAsync(
            "audit-log",
            (writer, token) => admin.ExportAuditLogCsvAsync(
                new AdminAuditLogQuery { Action = action, UserId = userId, Q = q }, writer, token),
            ct);
    }

    [HttpGet("users")]
    [ProducesResponseType(typeof(PagedResult<AdminUserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AdminUserDto>>> Users(
        [FromQuery] string? q, [FromQuery] string? role, [FromQuery] bool? emailConfirmed,
        [FromQuery] bool? lockedOut, [FromQuery] bool? hasBuyerId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        return Ok(await admin.SearchUsersAsync(new AdminUserSearchQuery
        {
            Q = q,
            Role = role,
            EmailConfirmed = emailConfirmed,
            LockedOut = lockedOut,
            HasBuyerId = hasBuyerId,
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

    /// <summary>
    /// The payments on one order, for the refund screen — the parties' own payment list lives
    /// under <c>GET /orders/{id}/payments</c> and this is the administration-only counterpart.
    /// Each payment carries its refunded total, so an administrator can see what is refundable.
    /// </summary>
    [HttpGet("orders/{orderId:guid}/payments")]
    [ProducesResponseType(typeof(IReadOnlyList<PaymentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PaymentDto>>> OrderPayments(
        Guid orderId, CancellationToken ct)
    {
        return Ok(await admin.ListOrderPaymentsAsync(orderId, ct));
    }

    /// <summary>
    /// Gives money back on a settled payment. Books a compensating ledger entry (negative, so the
    /// order's net settled figure and the corridor statistics fall with it); a full refund of the
    /// payment's remaining refundable amount moves the payment to <c>Refunded</c>. The order is
    /// re-rolled up, a Paid invoice reopens if the order drops below fully paid, and both parties
    /// are notified. Every refund is written to the audit trail.
    /// </summary>
    [HttpPost("payments/{paymentId:guid}/refund")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentDto>> RefundPayment(
        Guid paymentId, RefundPaymentRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        return Ok(await payments.RefundAsync(paymentId, User.GetUserId(), request, ct));
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

    /// <summary>
    /// The transaction ledger: every movement of money, newest first, across every order.
    /// </summary>
    /// <remarks>
    /// The administrative counterpart to the parties' order-scoped payment list. This is the record
    /// that reconciles: entries are read from the ledger rather than rolled up from payments, so a
    /// corrected payment does not rewrite history.
    /// </remarks>
    [HttpGet("transactions")]
    [ProducesResponseType(typeof(PagedResult<AdminTransactionRowDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AdminTransactionRowDto>>> Transactions(
        [FromQuery] string? q, [FromQuery] int? type, [FromQuery] string? currency,
        [FromQuery] Guid? orderId, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        return Ok(await admin.SearchTransactionsAsync(new AdminTransactionSearchQuery
        {
            Q = q,
            Type = type,
            Currency = currency,
            OrderId = orderId,
            From = from,
            To = to,
            Page = page,
            PageSize = pageSize,
        }, ct));
    }

    /// <summary>
    /// The transaction ledger as a CSV file, under the same filters as the list.
    /// </summary>
    /// <remarks>
    /// The ledger is what an operator reconciles against a bank statement, so being able to take the
    /// filtered rows away is the point, not a convenience. Ordering matches the list exactly, so a
    /// spreadsheet sorted the way it was exported is the same order the screen showed.
    /// </remarks>
    [HttpGet("transactions/export")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportTransactions(
        [FromQuery] string? q, [FromQuery] int? type, [FromQuery] string? currency,
        [FromQuery] Guid? orderId, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        CancellationToken ct = default)
    {
        return await CsvDownloadAsync(
            "transactions",
            (writer, token) => admin.ExportTransactionsCsvAsync(
                new AdminTransactionSearchQuery
                {
                    Q = q,
                    Type = type,
                    Currency = currency,
                    OrderId = orderId,
                    From = from,
                    To = to,
                },
                writer,
                token),
            ct);
    }

    /// <summary>
    /// Starts a CSV download and lets a service stream rows straight into it.
    /// </summary>
    /// <remarks>
    /// Written to the response rather than materialised into a string first, so the size of an export
    /// is bounded by the client's patience rather than by the server's memory. The UTF-8 BOM is
    /// deliberate: without it Excel reads a UTF-8 CSV as the local legacy code page and mangles any
    /// non-ASCII text — a trading name, a rejection reason — the moment the file is double-clicked.
    /// </remarks>
    private async Task<IActionResult> CsvDownloadAsync(
        string baseName,
        Func<TextWriter, CancellationToken, Task<int>> write,
        CancellationToken ct)
    {
        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers["Content-Disposition"] =
            $"attachment; filename=\"{baseName}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv\"";

        await using var writer = new StreamWriter(
            Response.Body,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            bufferSize: 16 * 1024,
            leaveOpen: true);

        await write(writer, ct);
        await writer.FlushAsync(ct);

        return new EmptyResult();
    }

    /// <summary>
    /// One analytics report over an optional date window.
    /// </summary>
    /// <remarks>
    /// A path segment rather than a query parameter, because the four reports do not share a payload
    /// shape: <c>/reports/corridors</c> reads as the resource it is, and an unknown kind is a 400
    /// rather than an empty 200.
    /// </remarks>
    [HttpGet("reports/{kind}")]
    [ProducesResponseType(typeof(AdminReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AdminReportDto>> Report(
        string kind, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        return Ok(await admin.GetReportAsync(ParseReportKind(kind), from, to, ct));
    }

    /// <summary>The operator-editable settings, catalogue defaults included.</summary>
    [HttpGet("settings")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminSettingDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminSettingDto>>> Settings(CancellationToken ct)
    {
        return Ok(await admin.GetSettingsAsync(ct));
    }

    /// <summary>
    /// Writes the supplied settings. Partial: keys not sent keep their current value or default.
    /// </summary>
    [HttpPut("settings")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminSettingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<AdminSettingDto>>> UpdateSettings(
        AdminSettingsUpdateRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        return Ok(await admin.UpdateSettingsAsync(User.GetUserId(), request, ct));
    }

    /// <summary>
    /// Suspends or reactivates an account.
    /// </summary>
    /// <remarks>
    /// Suspension sets the platform's own <c>IsActive</c> flag, which is checked on sign-in and on
    /// token validation: it blocks a suspended user immediately rather than only at their next
    /// login. The account is never deleted — the audit trail is <c>ON DELETE RESTRICT</c> precisely
    /// so the record of what an account approved cannot be removed with it.
    /// </remarks>
    [HttpPatch("users/{id:guid}/status")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminUserDto>> SetUserStatus(
        Guid id, AdminUserStatusRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await userStatusValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await admin.SetUserStatusAsync(id, User.GetUserId(), new SetUserStatusCommand
        {
            Action = request.Action,
            Reason = request.Reason,
        }, ct));
    }

    private static AdminReportKind ParseReportKind(string kind) => kind.Trim().ToLowerInvariant() switch
    {
        "corridors" => AdminReportKind.Corridors,
        "rfq" or "rfqs" => AdminReportKind.Rfq,
        "verification-funnel" => AdminReportKind.VerificationFunnel,
        "commodities" => AdminReportKind.Commodities,
        _ => throw ApiException.BadRequest(
            "UNKNOWN_REPORT",
            "Unknown report kind. Expected corridors, rfq, verification-funnel or commodities."),
    };

    private static void EnsureBody(object? request)
    {
        if (request is null)
        {
            throw ApiException.BadRequest("INVALID_BODY", "A JSON request body is required.");
        }
    }
}