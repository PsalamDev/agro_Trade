using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

/// <summary>
/// Importer RFQs: publish, open directory, quotations with trade terms, buyer
/// comparison, accept/withdraw (plan §03 — RFQs / Trade Requests). Negotiation
/// threads live under /messages with RFQ context.
/// </summary>
[ApiController]
[Route("api/v1")]
public class RfqsController(
    IRfqService rfqService,
    IValidator<CreateTradeRequestRequest> createValidator,
    IValidator<CreateQuotationRequest> quotationValidator) : ControllerBase
{
    [HttpPost("rfqs")]
    [Authorize(Policy = Policies.Buyer)]
    [ProducesResponseType(typeof(TradeRequestDetailDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<TradeRequestDetailDto>> Create(
        CreateTradeRequestRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await createValidator.ValidateAndThrowAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created,
            await rfqService.CreateAsync(User.GetUserId(), request, ct));
    }

    [HttpGet("rfqs/open")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResult<TradeRequestCardDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TradeRequestCardDto>>> GetOpen(
        [FromQuery] string? destinationCountry, Guid? categoryId, string? q,
        int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        return Ok(await rfqService.GetOpenDirectoryAsync(new OpenRfqFilters
        {
            DestinationCountry = destinationCountry,
            CategoryId = categoryId,
            Q = q,
            Page = page,
            PageSize = pageSize,
        }, ct));
    }

    [HttpGet("rfqs")]
    [Authorize]
    [ProducesResponseType(typeof(PagedResult<TradeRequestCardDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TradeRequestCardDto>>> Mine(
        [FromQuery] string? as_, int? status, int page = 1, int pageSize = 20,
        CancellationToken ct = default)
    {
        return Ok(await rfqService.MineAsync(User.GetUserId(), new RfqListQuery
        {
            As = as_ ?? "buyer",
            Status = status,
            Page = page,
            PageSize = pageSize,
        }, ct));
    }

    [HttpGet("rfqs/{id:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(TradeRequestDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<TradeRequestDetailDto>> Get(Guid id, CancellationToken ct)
    {
        return Ok(await rfqService.GetAsync(id, User.GetUserId(), ct));
    }

    [HttpPost("rfqs/{id:guid}/quotations")]
    [Authorize(Policy = Policies.Seller)]
    [ProducesResponseType(typeof(QuotationDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<QuotationDto>> CreateQuotation(
        Guid id, CreateQuotationRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await quotationValidator.ValidateAndThrowAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created,
            await rfqService.CreateQuotationAsync(id, User.GetUserId(), request, ct));
    }

    [HttpGet("rfqs/{id:guid}/quotations")]
    [Authorize]
    [ProducesResponseType(typeof(PagedResult<QuotationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<QuotationDto>>> GetQuotations(Guid id, CancellationToken ct)
    {
        return Ok(await rfqService.GetQuotationsAsync(id, User.GetUserId(), ct));
    }

    [HttpGet("quotations/{id:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(QuotationDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<QuotationDto>> GetQuotation(Guid id, CancellationToken ct)
    {
        return Ok(await rfqService.GetQuotationAsync(id, User.GetUserId(), ct));
    }

    [HttpPost("quotations/{id:guid}/accept")]
    [Authorize(Policy = Policies.Buyer)]
    [ProducesResponseType(typeof(QuotationDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<QuotationDto>> Accept(Guid id, CancellationToken ct)
    {
        return Ok(await rfqService.AcceptQuotationAsync(id, User.GetUserId(), ct));
    }

    [HttpPost("quotations/{id:guid}/withdraw")]
    [Authorize(Policy = Policies.Seller)]
    [ProducesResponseType(typeof(QuotationDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<QuotationDto>> Withdraw(Guid id, CancellationToken ct)
    {
        return Ok(await rfqService.WithdrawQuotationAsync(id, User.GetUserId(), ct));
    }

    private static void EnsureBody(object? request)
    {
        if (request is null)
        {
            throw ApiException.BadRequest("INVALID_BODY", "A JSON request body is required.");
        }
    }
}