using AgroTrade.Application.Common;
using AgroTrade.Application.Communication;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

/// <summary>
/// Direct and RFQ-context messaging: send, conversations, thread view, mark-read
/// (plan §03 — Messages). Notifications live on /me/notifications.
/// </summary>
[ApiController]
[Route("api/v1/messages")]
[Authorize]
public class MessagesController(
    ICommunicationService communicationService,
    IValidator<SendMessageRequest> sendValidator) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(MessageDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<MessageDto>> Send(SendMessageRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await sendValidator.ValidateAndThrowAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created,
            await communicationService.SendAsync(User.GetUserId(), request, ct));
    }

    [HttpGet("conversations")]
    [ProducesResponseType(typeof(PagedResult<ConversationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ConversationDto>>> Conversations(CancellationToken ct)
    {
        return Ok(await communicationService.GetConversationsAsync(User.GetUserId(), ct));
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<MessageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<MessageDto>>> Thread(
        [FromQuery] string threadKey, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(threadKey))
        {
            throw ApiException.BadRequest("THREAD_REQUIRED", "The threadKey query parameter is required.");
        }
        return Ok(await communicationService.GetThreadAsync(User.GetUserId(), threadKey, page, pageSize, ct));
    }

    [HttpPut("read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkRead(MarkReadRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await communicationService.MarkReadAsync(User.GetUserId(), request.ThreadKey, ct);
        return NoContent();
    }

    private static void EnsureBody(object? request)
    {
        if (request is null)
        {
            throw ApiException.BadRequest("INVALID_BODY", "A JSON request body is required.");
        }
    }
}