namespace AgroTrade.Application.Trade;

public sealed class CategoryDto
{
    public Guid Id { get; init; }
    public Guid? ParentId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string? Icon { get; init; }
    public int SortOrder { get; init; }
    public List<CategoryDto> Children { get; init; } = [];
}

public sealed class CreateCategoryRequest
{
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public Guid? ParentId { get; init; }
    public string? Icon { get; init; }
    public int SortOrder { get; init; }
}

public sealed class UpdateCategoryRequest
{
    public string? Name { get; init; }
    public string? Slug { get; init; }
    public string? Icon { get; init; }
    public bool? IsActive { get; init; }
    public int? SortOrder { get; init; }
}

/// <summary>Starter map of common commodities → HS codes (plan §02 seeding).</summary>
public sealed record HsCodeEntryDto(string Commodity, string HsCode);