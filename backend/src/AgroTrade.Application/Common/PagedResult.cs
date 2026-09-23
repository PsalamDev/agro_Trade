namespace AgroTrade.Application.Common;

/// <summary>Standard pagination envelope (plan §03).</summary>
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public int Total { get; init; }
    public int TotalPages { get; init; }
}

public static class PageHelper
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    public static (int Page, int PageSize) Normalize(int page, int pageSize)
    {
        var p = Math.Max(1, page);
        var ps = Math.Clamp(pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);
        return (p, ps);
    }
}