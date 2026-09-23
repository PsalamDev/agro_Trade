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

    Task<ProductDetailDto> UpdateAsync(Guid id, Guid ownerUserId, UpdateProductRequest request, CancellationToken ct = default);

    Task DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct = default);

    Task<ProductImageDto> UploadImageAsync(Guid id, Guid ownerUserId, string fileName, string contentType, Stream content, bool isPrimary, CancellationToken ct = default);

    Task DeleteImageAsync(Guid id, Guid imageId, Guid ownerUserId, CancellationToken ct = default);
}