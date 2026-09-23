using AgroTrade.Domain.Common;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Domain.Communication;

/// <summary>
/// A direct or context-scoped message (e.g. RFQ negotiation thread). ThreadKey
/// canonicalizes participants + context for ordered threads (plan §02).
/// </summary>
public sealed class Message : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ThreadKey { get; set; } = string.Empty;
    public Guid SenderUserId { get; set; }
    public Guid RecipientUserId { get; set; }
    public MessageContextType ContextType { get; set; }
    public Guid? ContextId { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? AttachmentUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTime SentAtUtc { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}