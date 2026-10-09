using AgroTrade.Application.Common;
using AgroTrade.Application.Trust;
using AgroTrade.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Infrastructure.Services;

public sealed class ReviewService(AgroTradeDbContext db) : IReviewService
{
    public async Task<PagedResult<ReviewResponse>> ListAsync(ReviewListRequest request, CancellationToken ct)
    {
        var query = db.Reviews.AsQueryable();
        if (request.TargetBusinessId.HasValue)
            query = query.Where(r => r.TargetBusinessId == request.TargetBusinessId.Value);
        if (request.IsPublished.HasValue)
            query = query.Where(r => r.IsPublished == request.IsPublished.Value);

        query = query.OrderByDescending(r => r.CreatedAtUtc);

        var total = await query.CountAsync(ct);
        var items = await query.Skip((request.Page-1)*request.PageSize).Take(request.PageSize)
            .Select(r => new ReviewResponse(r.Id, r.ReviewerUserId, r.TargetBusinessId, r.OrderId, r.Rating, r.Comment, r.IsPublished, r.CreatedAtUtc!.Value))
            .ToListAsync(ct);

        return new PagedResult<ReviewResponse> { Items = items, Total = total, Page = request.Page, PageSize = request.PageSize };
    }

    public async Task<ReviewResponse> CreateAsync(Guid userId, ReviewCreateRequest request, CancellationToken ct)
    {
        var review = new Domain.Trust.Review
        {
            ReviewerUserId = userId,
            TargetBusinessId = request.TargetBusinessId,
            OrderId = request.OrderId,
            Rating = request.Rating,
            Comment = request.Comment?.Trim(),
            IsPublished = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.Reviews.Add(review);
        await db.SaveChangesAsync(ct);
        return new ReviewResponse(review.Id, review.ReviewerUserId, review.TargetBusinessId, review.OrderId, review.Rating, review.Comment, review.IsPublished, review.CreatedAtUtc!.Value);
    }

    public async Task PublishAsync(Guid id, CancellationToken ct)
    {
        var r = await db.Reviews.FindAsync(new object[] { id }, ct);
        if (r == null) return;
        r.IsPublished = true;
        await db.SaveChangesAsync(ct);
    }
}