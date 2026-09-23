namespace AgroTrade.Domain.Trade;

/// <summary>
/// Product category tree (12 top-level categories per spec §4 plus commodity children).
/// </summary>
public sealed class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}