using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Api.Controllers;

/// <summary>
/// Trade documents attached to an order: upload by either party, read by both, and an admin
/// queue that verifies or rejects (plan §02 — Documents).
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize]
public class TradeDocumentsController(ITradeDocumentService documentService) : ControllerBase
{
    [HttpPost("orders/{orderId:guid}/documents")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    [ProducesResponseType(typeof(TradeDocumentDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<TradeDocumentDto>> Upload(
        Guid orderId,
        [FromForm] int? docType,
        [FromForm] string? documentReference,
        [FromForm] DateTime? issuedOnUtc,
        [FromForm] DateTime? expiresOnUtc,
        IFormFile file,
        CancellationToken ct)
    {
        if (docType is null)
        {
            throw ApiException.BadRequest("DOC_TYPE_REQUIRED", "The docType field is required.");
        }

        var (stream, fileName, contentType) = await UploadGuard.ReadAsync(file, ct);
        try
        {
            var dto = await documentService.UploadAsync(
                orderId,
                User.GetUserId(),
                new UploadTradeDocumentRequest
                {
                    DocType = (DocumentType)docType.Value,
                    DocumentReference = documentReference,
                    IssuedOnUtc = issuedOnUtc,
                    ExpiresOnUtc = expiresOnUtc,
                    FileName = fileName,
                    ContentType = contentType,
                },
                stream,
                ct);

            return StatusCode(StatusCodes.Status201Created, dto);
        }
        finally
        {
            await stream.DisposeAsync();
        }
    }

    [HttpGet("orders/{orderId:guid}/documents")]
    [ProducesResponseType(typeof(IReadOnlyList<TradeDocumentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TradeDocumentDto>>> GetForOrder(Guid orderId, CancellationToken ct)
    {
        var isAdmin = User.IsInRole(RoleNames.Administrator);
        return Ok(await documentService.GetForOrderAsync(orderId, User.GetUserId(), isAdmin, ct));
    }

    [HttpGet("documents/{documentId:guid}")]
    [ProducesResponseType(typeof(TradeDocumentDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<TradeDocumentDto>> Get(Guid documentId, CancellationToken ct)
    {
        var isAdmin = User.IsInRole(RoleNames.Administrator);
        return Ok(await documentService.GetAsync(documentId, User.GetUserId(), isAdmin, ct));
    }

    [HttpDelete("documents/{documentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid documentId, CancellationToken ct)
    {
        var isAdmin = User.IsInRole(RoleNames.Administrator);
        await documentService.DeleteAsync(documentId, User.GetUserId(), isAdmin, ct);
        return NoContent();
    }
}