namespace AgroTrade.Domain.Common;

/// <summary>
/// Entities that carry audit timestamps (UTC by convention).
/// </summary>
public interface IAuditableEntity
{
    DateTime? CreatedAtUtc { get; set; }
    DateTime? UpdatedAtUtc { get; set; }
}