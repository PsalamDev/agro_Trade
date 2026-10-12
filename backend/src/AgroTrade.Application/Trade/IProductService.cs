using AgroTrade.Application.Common;

namespace AgroTrade.Application.Trade;

/// <summary>
/// Export listings: seller CRUD (draft/active lifecycle), images, marketplace search
/// with destination-reachability and verified-only filters (plan §03 — Products).
/// </summary>
public interface IProductService
{
    Task<PagedResult<ProductCardDto>> SearchAsync(ProductSearchQuery query, CancellationToken ct = default);

    Task<ProductDetailDto> GetAsync(string idOrSlug, Guid? viewerUserId, CancellationToken ct = default);

    Task<ProductDetailDto> CreateAsync(Guid ownerUserId, string? businessIdOrName, CreateProductRequest request, CancellationToken ct = default);

    /// <summary>
    /// Creates a listing on behalf of a business the caller does not own.
    /// </summary>
    /// <remarks>
    /// The administrative path. The seller route resolves the business from the caller's own
    /// account; here the business is named explicitly, because the acting administrator owns no
    /// business of their own. The same creation rules apply (an Active listing needs a verified
    /// business, the stock row is seeded), and the action is audited against
    /// <paramref name="actorUserId"/>.
    /// </remarks>
    Task<ProductDetailDto> CreateForBusinessAsync(
        Guid businessId, Guid actorUserId, CreateProductRequest request, CancellationToken ct = default);

    Task<ProductDetailDto> UpdateAsync(Guid id, Guid ownerUserId, UpdateProductRequest request, CancellationToken ct = default);

    Task DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct = default);

    Task<ProductImageDto> UploadImageAsync(Guid id, Guid ownerUserId, string fileName, string contentType, Stream content, bool isPrimary, CancellationToken ct = default);

    Task DeleteImageAsync(Guid id, Guid imageId, Guid ownerUserId, CancellationToken ct = default);
}