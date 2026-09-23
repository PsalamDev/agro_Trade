using AgroTrade.Domain.Common;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Domain.Trade;

/// <summary>
/// Append-only history of verification transitions for a business (submit / review).
/// The current state lives on Business.VerificationStatus (plan §02).
/// </summary>
public sealed class BusinessVerification : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }
    public VerificationStatus Status { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public string? Notes { get; set; }
    /// <summary>Basic | Enhanced (enhanced = additional due diligence).</summary>
    public string VerifiedLevel { get; set; } = "Basic";
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}