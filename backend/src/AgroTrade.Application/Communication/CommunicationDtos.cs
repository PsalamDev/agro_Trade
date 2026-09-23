using System.ComponentModel.DataAnnotations;
using AgroTrade.Application.Common;

namespace AgroTrade.Application.Communication;

public sealed class SendMessageRequest
{
    [Required, MaxLength(4000)]
    public string Body { get; init; } = string.Empty;

    /// <summary>Context (e.g. an RFQ negotiation thread). ContextType + ContextId pair.</summary>
    public int? ContextType { get; init; }
    public Guid? ContextId { get; init; }

    /// <summary>Required for direct (General) messages and ambiguous contexts.</summary>
    public Guid? RecipientUserId { get; init; }
}

public sealed class MessageDto
{
    public Guid Id { get; init; }
    public string ThreadKey { get; init; } = string.Empty;
    public Guid SenderUserId { get; init; }
    public string SenderDisplayName { get; init; } = string.Empty;
    public Guid RecipientUserId { get; init; }
    public int ContextType { get; init; }
    public Guid? ContextId { get; init; }
    public string Body { get; init; } = string.Empty;
    public bool IsRead { get; init; }
    public DateTime SentAtUtc { get; init; }
}

public sealed class ConversationDto
{
    public string ThreadKey { get; init; } = string.Empty;
    public Guid OtherUserId { get; init; }
    public string OtherDisplayName { get; init; } = string.Empty;
    public int ContextType { get; init; }
    public Guid? ContextId { get; init; }
    public string LastMessage { get; init; } = string.Empty;
    public DateTime LastMessageAtUtc { get; init; }
    public int UnreadCount { get; init; }
    public int TotalMessages { get; init; }
}

public sealed class MarkReadRequest
{
    [Required, MaxLength(64)]
    public string ThreadKey { get; init; } = string.Empty;
}

public sealed class NotificationDto
{
    public Guid Id { get; init; }
    public int Type { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Body { get; init; }
    public string? Link { get; init; }
    public bool IsRead { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}

/// <summary>
/// Direct + context-scoped messaging, read tracking, and in-app notifications.
/// SignalR hubs land with realtime in Phase 2; polling fallback is in the plan §03.
/// </summary>
public interface ICommunicationService
{
    Task<MessageDto> SendAsync(Guid senderUserId, SendMessageRequest request, CancellationToken ct = default);

    Task<PagedResult<ConversationDto>> GetConversationsAsync(Guid userId, CancellationToken ct = default);

    Task<PagedResult<MessageDto>> GetThreadAsync(Guid userId, string threadKey, int page, int pageSize, CancellationToken ct = default);

    Task MarkReadAsync(Guid userId, string threadKey, CancellationToken ct = default);

    Task<PagedResult<NotificationDto>> GetNotificationsAsync(Guid userId, bool? unreadOnly, int page, int pageSize, CancellationToken ct = default);

    Task<NotificationDto> MarkNotificationReadAsync(Guid userId, Guid id, CancellationToken ct = default);

    Task MarkAllNotificationsReadAsync(Guid userId, CancellationToken ct = default);

    Task<int> GetUnreadNotificationCountAsync(Guid userId, CancellationToken ct = default);
}