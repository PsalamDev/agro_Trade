using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

/// <summary>
/// Product categories (public tree + admin CRUD) and the commodity/HS-code starter
/// map (plan §03 — Categories & commodities).
/// </summary>
[ApiController]
[Route("api/v1")]
public class CategoriesController(
    ICatalogService catalogService,
    IValidator<CreateCategoryRequest> createValidator,
    IValidator<UpdateCategoryRequest> updateValidator) : ControllerBase
{
    [HttpGet("categories")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> GetTree(CancellationToken ct)
    {
        return Ok(await catalogService.GetCategoryTreeAsync(ct));
    }

    [HttpGet("commodities/hs-codes")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<HsCodeEntryDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<HsCodeEntryDto>> GetHsCodes()
    {
        return Ok(catalogService.GetHsCodeMap());
    }

    [HttpPost("admin/categories")]
    [Authorize(Policy = Policies.Administrator)]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<CategoryDto>> Create(
        CreateCategoryRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await createValidator.ValidateAndThrowAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, await catalogService.CreateCategoryAsync(request, ct));
    }

    [HttpPut("admin/categories/{id:guid}")]
    [Authorize(Policy = Policies.Administrator)]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CategoryDto>> Update(
        Guid id, UpdateCategoryRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await updateValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await catalogService.UpdateCategoryAsync(id, request, ct));
    }

    [HttpDelete("admin/categories/{id:guid}")]
    [Authorize(Policy = Policies.Administrator)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await catalogService.DeleteCategoryAsync(id, ct);
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