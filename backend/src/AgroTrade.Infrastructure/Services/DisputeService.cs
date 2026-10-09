using AgroTrade.Application.Trade;
using AgroTrade.Domain.Enums;
using AgroTrade.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Infrastructure.Services;

public sealed class DisputeService(AgroTradeDbContext db) : IDisputeService
{
    public async Task<DisputeResponse> CreateAsync(Guid userId, DisputeCreateRequest request, CancellationToken ct)
    {
        var d = new Domain.Trade.Dispute
        {
            OrderId = request.OrderId,
            RaisedByUserId = userId,
            AgainstBusinessId = request.AgainstBusinessId,
            Reason = request.Reason,
            Details = request.Details.Trim(),
            Status = DisputeStatus.Open,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.Disputes.Add(d);
        await db.SaveChangesAsync(ct);
        return new DisputeResponse(d.Id, d.OrderId, d.RaisedByUserId, d.AgainstBusinessId, d.Reason, d.Details, d.Status, d.ResolvedByUserId, d.Resolution, d.CreatedAtUtc!.Value, d.ResolvedAtUtc);
    }

    public async Task<DisputeResponse> ResolveAsync(Guid id, Guid adminId, DisputeResolveRequest request, CancellationToken ct)
    {
        var d = await db.Disputes.FindAsync(new object[] { id }, ct);
        if (d == null) throw new InvalidOperationException("Dispute not found");
        d.Status = request.Status;
        d.ResolvedByUserId = adminId;
        d.Resolution = request.Resolution?.Trim();
        d.ResolvedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return new DisputeResponse(d.Id, d.OrderId, d.RaisedByUserId, d.AgainstBusinessId, d.Reason, d.Details, d.Status, d.ResolvedByUserId, d.Resolution, d.CreatedAtUtc!.Value, d.ResolvedAtUtc);
    }

    public async Task<IReadOnlyList<DisputeResponse>> ListOpenAsync(CancellationToken ct)
    {
        return await db.Disputes.Where(x => x.Status == DisputeStatus.Open || x.Status == DisputeStatus.UnderReview)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(d => new DisputeResponse(d.Id, d.OrderId, d.RaisedByUserId, d.AgainstBusinessId, d.Reason, d.Details, d.Status, d.ResolvedByUserId, d.Resolution, d.CreatedAtUtc!.Value, d.ResolvedAtUtc))
            .ToListAsync(ct);
    }
}