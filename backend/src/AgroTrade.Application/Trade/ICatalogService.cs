namespace AgroTrade.Application.Trade;

/// <summary>
/// Categories tree, HS-code starter map, and admin category CRUD (plan §03 — 
/// Categories & commodities).
/// </summary>
public interface ICatalogService
{
    Task<IReadOnlyList<CategoryDto>> GetCategoryTreeAsync(CancellationToken ct = default);

    /// <summary>Starter HS-code map — static helper data, no DB.</summary>
    IReadOnlyList<HsCodeEntryDto> GetHsCodeMap();

    /// <summary>Full ISO 3166-1 country list (name + alpha-2/alpha-3/numeric) — static helper data.</summary>
    IReadOnlyList<CountryDto> GetCountryList();

    Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct = default);

    Task<CategoryDto> UpdateCategoryAsync(Guid id, UpdateCategoryRequest request, CancellationToken ct = default);

    Task DeleteCategoryAsync(Guid id, CancellationToken ct = default);
}