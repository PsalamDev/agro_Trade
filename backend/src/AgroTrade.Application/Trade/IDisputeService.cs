namespace AgroTrade.Application.Trade;

public interface IDisputeService
{
    Task<DisputeResponse> CreateAsync(Guid userId, DisputeCreateRequest request, CancellationToken ct);
    Task<DisputeResponse> ResolveAsync(Guid id, Guid adminId, DisputeResolveRequest request, CancellationToken ct);
    Task<IReadOnlyList<DisputeResponse>> ListOpenAsync(CancellationToken ct);
}