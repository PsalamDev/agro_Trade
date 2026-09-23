using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

/// <summary>
/// Business profiles: register, public search, trade-country reachability, KYC
/// document upload, and submit-for-verification (plan §03 — Businesses).
/// </summary>
[ApiController]
[Route("api/v1/businesses")]
public class BusinessesController(
    IBusinessService businessService,
    IValidator<RegisterBusinessRequest> registerValidator,
    IValidator<TradeCountriesRequest> tradeCountriesValidator,
    IValidator<UpdateBusinessRequest> updateValidator) : ControllerBase
{
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(BusinessDetailDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<BusinessDetailDto>> Create(
        RegisterBusinessRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await registerValidator.ValidateAndThrowAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created,
            await businessService.CreateAsync(User.GetUserId(), request, ct));
    }

    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResult<BusinessSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<BusinessSummaryDto>>> Search(
        [FromQuery] string? q, string? country, int? type, bool verifiedOnly,
        string? exportsTo, string? importsFrom, int page = 1, int pageSize = 20,
        CancellationToken ct = default)
    {
        return Ok(await businessService.SearchAsync(new BusinessSearchQuery
        {
            Q = q,
            Country = country,
            Type = type,
            VerifiedOnly = verifiedOnly,
            ExportsTo = exportsTo,
            ImportsFrom = importsFrom,
            Page = page,
            PageSize = pageSize,
        }, ct));
    }

    [HttpGet("mine")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<BusinessSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BusinessSummaryDto>>> Mine(CancellationToken ct)
    {
        return Ok(await businessService.MineAsync(User.GetUserId(), ct));
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(BusinessDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<BusinessDetailDto>> Get(Guid id, CancellationToken ct)
    {
        Guid? viewer = User.Identity?.IsAuthenticated == true ? User.GetUserId() : null;
        var isAdmin = User.IsInRole(AgroTrade.Domain.Enums.RoleNames.Administrator);
        return Ok(await businessService.GetAsync(id, viewer, isAdmin, ct));
    }

    [HttpPut("{id:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(BusinessDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<BusinessDetailDto>> Update(
        Guid id, UpdateBusinessRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await updateValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await businessService.UpdateAsync(id, User.GetUserId(), request, ct));
    }

    [HttpPut("{id:guid}/trade-countries")]
    [Authorize]
    [ProducesResponseType(typeof(BusinessDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<BusinessDetailDto>> SetTradeCountries(
        Guid id, TradeCountriesRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await tradeCountriesValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await businessService.SetTradeCountriesAsync(id, User.GetUserId(), request, ct));
    }

    [HttpPost("{id:guid}/documents")]
    [Authorize]
    [ProducesResponseType(typeof(BusinessDocumentDto), StatusCodes.Status201Created)]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<ActionResult<BusinessDocumentDto>> UploadDocument(
        Guid id, [FromForm] int? docType, IFormFile file, CancellationToken ct)
    {
        if (docType is null)
        {
            throw ApiException.BadRequest("DOC_TYPE_REQUIRED", "The docType field is required.");
        }
        var (stream, fileName, contentType) = await UploadGuard.ReadAsync(file, ct);
        try
        {
            return StatusCode(StatusCodes.Status201Created,
                await businessService.UploadDocumentAsync(id, User.GetUserId(), docType.Value, fileName, contentType, stream, ct));
        }
        finally
        {
            await stream.DisposeAsync();
        }
    }

    [HttpGet("{id:guid}/documents")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<BusinessDocumentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BusinessDocumentDto>>> GetDocuments(Guid id, CancellationToken ct)
    {
        var isAdmin = User.IsInRole(AgroTrade.Domain.Enums.RoleNames.Administrator);
        return Ok(await businessService.GetDocumentsAsync(id, User.GetUserId(), isAdmin, ct));
    }

    [HttpPost("{id:guid}/verification-submissions")]
    [Authorize]
    [ProducesResponseType(typeof(VerificationStateDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<VerificationStateDto>> SubmitForVerification(Guid id, CancellationToken ct)
    {
        return Ok(await businessService.SubmitForVerificationAsync(id, User.GetUserId(), ct));
    }

    [HttpGet("{id:guid}/verification")]
    [Authorize]
    [ProducesResponseType(typeof(VerificationStateDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<VerificationStateDto>> GetVerification(Guid id, CancellationToken ct)
    {
        var isAdmin = User.IsInRole(AgroTrade.Domain.Enums.RoleNames.Administrator);
        return Ok(await businessService.GetVerificationAsync(id, User.GetUserId(), isAdmin, ct));
    }

    private static void EnsureBody(object? request)
    {
        if (request is null)
        {
            throw ApiException.BadRequest("INVALID_BODY", "A JSON request body is required.");
        }
    }
}