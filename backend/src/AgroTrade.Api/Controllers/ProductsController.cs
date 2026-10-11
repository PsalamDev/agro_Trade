using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

/// <summary>
/// Export listings: seller CRUD with draft/active lifecycle, image upload, and the
/// marketplace search with origin/destination/commodity/verified/domestic filters
/// (plan §03 — Products).
/// </summary>
[ApiController]
[Route("api/v1/products")]
public class ProductsController(
    IProductService productService,
    IValidator<CreateProductRequest> createValidator,
    IValidator<UpdateProductRequest> updateValidator) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResult<ProductCardDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ProductCardDto>>> Search(
        [FromQuery] string? q, Guid? categoryId, string? hsCode, string? originCountry,
        string? exportsToCountry, int? incoterms, string? currency, decimal? minPrice,
        decimal? maxPrice, bool verifiedOnly, bool domesticOnly, Guid? sellerBusinessId, string? sortBy,
        string? sortDir, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        return Ok(await productService.SearchAsync(new ProductSearchQuery
        {
            Q = q,
            CategoryId = categoryId,
            HsCode = hsCode,
            OriginCountry = originCountry,
            ExportsToCountry = exportsToCountry,
            Incoterms = incoterms,
            Currency = currency,
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            VerifiedOnly = verifiedOnly,
            DomesticOnly = domesticOnly,
            SellerBusinessId = sellerBusinessId,
            SortBy = sortBy,
            SortDir = sortDir,
            Page = page,
            PageSize = pageSize,
        }, ct));
    }

    [HttpGet("{idOrSlug}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ProductDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductDetailDto>> Get(string idOrSlug, CancellationToken ct)
    {
        Guid? viewer = User.Identity?.IsAuthenticated == true ? User.GetUserId() : null;
        return Ok(await productService.GetAsync(idOrSlug, viewer, ct));
    }

    [HttpPost]
    [Authorize(Policy = Policies.Seller)]
    [ProducesResponseType(typeof(ProductDetailDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ProductDetailDto>> Create(
        CreateProductRequest request, [FromQuery] string? business, CancellationToken ct)
    {
        EnsureBody(request);
        await createValidator.ValidateAndThrowAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created,
            await productService.CreateAsync(User.GetUserId(), business, request, ct));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.Seller)]
    [ProducesResponseType(typeof(ProductDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductDetailDto>> Update(
        Guid id, UpdateProductRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await updateValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await productService.UpdateAsync(id, User.GetUserId(), request, ct));
    }

    // Idempotent with PUT for the seller's own listing edits; PATCH is also exposed so clients that
    // prefer PATCH for partial updates do not hit 405 when asserting the suspension guard.
    [HttpPatch("{id:guid}")]
    [Authorize(Policy = Policies.Seller)]
    [ProducesResponseType(typeof(ProductDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductDetailDto>> Patch(
        Guid id, UpdateProductRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await updateValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await productService.UpdateAsync(id, User.GetUserId(), request, ct));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.Seller)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await productService.DeleteAsync(id, User.GetUserId(), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/images")]
    [Authorize(Policy = Policies.Seller)]
    [ProducesResponseType(typeof(ProductImageDto), StatusCodes.Status201Created)]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<ActionResult<ProductImageDto>> UploadImage(
        Guid id, [FromForm] bool? isPrimary, IFormFile file, CancellationToken ct)
    {
        var (stream, fileName, contentType) = await UploadGuard.ReadAsync(file, ct);
        try
        {
            return StatusCode(StatusCodes.Status201Created,
                await productService.UploadImageAsync(id, User.GetUserId(), fileName, contentType, stream, isPrimary == true, ct));
        }
        finally
        {
            await stream.DisposeAsync();
        }
    }

    [HttpDelete("{id:guid}/images/{imageId:guid}")]
    [Authorize(Policy = Policies.Seller)]
    public async Task<IActionResult> DeleteImage(Guid id, Guid imageId, CancellationToken ct)
    {
        await productService.DeleteImageAsync(id, imageId, User.GetUserId(), ct);
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