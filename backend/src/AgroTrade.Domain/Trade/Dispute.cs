using AgroTrade.Domain.Common;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Domain.Trade;

public sealed class Dispute : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid RaisedByUserId { get; set; }
    public Guid AgainstBusinessId { get; set; }
    public int Reason { get; set; }
    public string Details { get; set; } = string.Empty;
    public DisputeStatus Status { get; set; } = DisputeStatus.Open;
    public Guid? ResolvedByUserId { get; set; }
    public string? Resolution { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}