using AgroTrade.Domain.Common;

namespace AgroTrade.Domain.Trust;

public sealed class Review : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReviewerUserId { get; set; }
    public Guid TargetBusinessId { get; set; }
    public Guid OrderId { get; set; }
    public byte Rating { get; set; } // 1-5
    public string? Comment { get; set; }
    public bool IsPublished { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}