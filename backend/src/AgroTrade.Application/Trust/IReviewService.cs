using AgroTrade.Application.Common;

namespace AgroTrade.Application.Trust;

public interface IReviewService
{
    Task<PagedResult<ReviewResponse>> ListAsync(ReviewListRequest request, CancellationToken ct);
    Task<ReviewResponse> CreateAsync(Guid userId, ReviewCreateRequest request, CancellationToken ct);
    Task PublishAsync(Guid id, CancellationToken ct);
}