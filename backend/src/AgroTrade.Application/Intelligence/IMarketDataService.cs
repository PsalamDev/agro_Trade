using AgroTrade.Application.Common;

namespace AgroTrade.Application.Intelligence;

public interface IMarketDataService
{
    Task<PagedResult<MarketDataResponse>> ListAsync(MarketDataListRequest request, CancellationToken ct);
    Task<MarketDataResponse> CreateAsync(MarketDataCreateRequest request, CancellationToken ct);
}