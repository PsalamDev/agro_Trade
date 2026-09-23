using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using AgroTrade.Domain.Trade;
using AgroTrade.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Infrastructure.Trade;

/// <summary>
/// Category tree + HS-code starter map + admin category CRUD (plan §03).
/// </summary>
public sealed class CatalogService(AgroTradeDbContext db) : ICatalogService
{
    public async Task<IReadOnlyList<CategoryDto>> GetCategoryTreeAsync(CancellationToken ct = default)
    {
        var categories = await db.Categories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(ct);

        return BuildTree(categories, null);
    }

    public IReadOnlyList<HsCodeEntryDto> GetHsCodeMap() => HsCodeMap.Entries;

    public async Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct = default)
    {
        if (await db.Categories.AnyAsync(c => c.Slug == request.Slug, ct))
        {
            throw ApiException.Conflict("SLUG_EXISTS", $"A category with slug '{request.Slug}' already exists.");
        }

        if (request.ParentId is not null && !await db.Categories.AnyAsync(c => c.Id == request.ParentId, ct))
        {
            throw ApiException.BadRequest("PARENT_NOT_FOUND", "Parent category does not exist.");
        }

        var category = new Category
        {
            Name = request.Name.Trim(),
            Slug = request.Slug.Trim().ToLowerInvariant(),
            ParentId = request.ParentId,
            Icon = request.Icon,
            SortOrder = request.SortOrder,
        };
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        return ToDto(category);
    }

    public async Task<CategoryDto> UpdateCategoryAsync(Guid id, UpdateCategoryRequest request, CancellationToken ct = default)
    {
        var category = await db.Categories.FindAsync([id], ct)
            ?? throw ApiException.NotFound("CATEGORY_NOT_FOUND", "Category not found.");

        if (request.Name is not null) category.Name = request.Name.Trim();
        if (request.Icon is not null) category.Icon = request.Icon;
        if (request.SortOrder is not null) category.SortOrder = request.SortOrder.Value;
        if (request.IsActive is not null) category.IsActive = request.IsActive.Value;

        if (request.Slug is not null && !string.Equals(category.Slug, request.Slug, StringComparison.OrdinalIgnoreCase))
        {
            var slug = request.Slug.Trim().ToLowerInvariant();
            if (await db.Categories.AnyAsync(c => c.Slug == slug && c.Id != id, ct))
            {
                throw ApiException.Conflict("SLUG_EXISTS", $"A category with slug '{slug}' already exists.");
            }
            category.Slug = slug;
        }

        await db.SaveChangesAsync(ct);
        return ToDto(category);
    }

    public async Task DeleteCategoryAsync(Guid id, CancellationToken ct = default)
    {
        var category = await db.Categories.FindAsync([id], ct)
            ?? throw ApiException.NotFound("CATEGORY_NOT_FOUND", "Category not found.");

        if (await db.Categories.AnyAsync(c => c.ParentId == id, ct))
        {
            throw ApiException.Conflict("HAS_CHILDREN", "Move or delete child categories first.");
        }
        if (await db.Products.AnyAsync(p => p.CategoryId == id && !p.IsDeleted, ct))
        {
            throw ApiException.Conflict("HAS_PRODUCTS", "Reassign or delete products in this category first.");
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
    }

    private static List<CategoryDto> BuildTree(List<Category> all, Guid? parentId) =>
        all
            .Where(c => c.ParentId == parentId)
            .Select(c => new CategoryDto
            {
                Id = c.Id,
                ParentId = c.ParentId,
                Name = c.Name,
                Slug = c.Slug,
                Icon = c.Icon,
                SortOrder = c.SortOrder,
                Children = BuildTree(all, c.Id),
            })
            .ToList();

    private static CategoryDto ToDto(Category c) => new()
    {
        Id = c.Id,
        ParentId = c.ParentId,
        Name = c.Name,
        Slug = c.Slug,
        Icon = c.Icon,
        SortOrder = c.SortOrder,
    };
}

/// <summary>Commodity → HS chapter starter map (plan §02 seeding; assignment-mapping wise).</summary>
internal static class HsCodeMap
{
    public static readonly IReadOnlyList<HsCodeEntryDto> Entries =
    [
        new("Cashew nuts", "0801"),
        new("Maize (corn)", "1005"),
        new("Soybeans", "1201"),
        new("Cocoa beans", "1801"),
        new("Coffee (green)", "0901"),
        new("Rice (milled)", "1006"),
        new("Millet", "1008"),
        new("Sorghum", "1007"),
        new("Groundnuts (peanuts)", "1202"),
        new("Sesame seeds", "1207"),
        new("Palm oil (crude)", "1511"),
        new("Shea butter", "1515"),
        new("Cassava (dried)", "0714"),
        new("Ginger", "0910"),
        new("Tea", "0902"),
        new("Cotton (raw)", "5201"),
        new("Live cattle", "0102"),
        new("Frozen fish", "0303"),
    ];
}