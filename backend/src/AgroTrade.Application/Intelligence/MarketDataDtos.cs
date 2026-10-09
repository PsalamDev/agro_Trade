namespace AgroTrade.Application.Intelligence;

public record MarketDataResponse(
    Guid Id,
    string Commodity,
    string Country,
    string? Region,
    string Unit,
    decimal Price,
    string Currency,
    string Source,
    DateTime ReportedAtUtc,
    string? Notes);

public record MarketDataListRequest(
    int Page = 1,
    int PageSize = 20,
    string? Commodity = null,
    string? Country = null,
    string? Currency = null,
    DateTime? From = null,
    DateTime? To = null,
    string? Sort = "reportedAtDesc");

public record MarketDataCreateRequest(
    string Commodity,
    string Country,
    string? Region,
    string Unit,
    decimal Price,
    string Currency,
    string Source,
    DateTime ReportedAtUtc,
    string? Notes);