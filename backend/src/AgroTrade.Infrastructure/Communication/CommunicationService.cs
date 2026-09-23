using AgroTrade.Application.Common;
using AgroTrade.Application.Communication;
using AgroTrade.Domain.Communication;
using AgroTrade.Domain.Enums;
using AgroTrade.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Infrastructure.Communication;

/// <summary>
/// Direct + context-scoped messaging (RFQ negotiation threads) and in-app
/// notifications with unread state (plan §03 — Messages / Notifications).
/// SignalR hubs replace polling in Phase 2.
/// </summary>
public sealed class CommunicationService(AgroTradeDbContext db) : ICommunicationService
{
    public async Task<MessageDto> SendAsync(Guid senderUserId, SendMessageRequest request, CancellationToken ct = default)
    {
        var (recipientId, contextType, contextId, threadKey) = await ResolveThreadAsync(senderUserId, request, ct);

        var senderName = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == senderUserId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync(ct) ?? "User";

        var message = new Message
        {
            SenderUserId = senderUserId,
            RecipientUserId = recipientId,
            ContextType = contextType,
            ContextId = contextId,
            ThreadKey = threadKey,
            Body = request.Body.Trim(),
            SentAtUtc = DateTime.UtcNow,
        };
        db.Messages.Add(message);

        db.Notifications.Add(new Notification
        {
            UserId = recipientId,
            Type = NotificationType.Message,
            Title = "New message",
            Body = $"{senderName}: {message.Body[..Math.Min(120, message.Body.Length)]}",
            Link = $"/messages?thread={Uri.EscapeDataString(threadKey)}",
        });

        await db.SaveChangesAsync(ct);

        return new MessageDto
        {
            Id = message.Id,
            ThreadKey = message.ThreadKey,
            SenderUserId = message.SenderUserId,
            SenderDisplayName = senderName,
            RecipientUserId = message.RecipientUserId,
            ContextType = (int)message.ContextType,
            ContextId = message.ContextId,
            Body = message.Body,
            IsRead = message.IsRead,
            SentAtUtc = message.SentAtUtc,
        };
    }

    public async Task<PagedResult<ConversationDto>> GetConversationsAsync(Guid userId, CancellationToken ct = default)
    {
        var messages = await db.Messages
            .AsNoTracking()
            .Where(m => m.SenderUserId == userId || m.RecipientUserId == userId)
            .OrderByDescending(m => m.SentAtUtc)
            .ToListAsync(ct);

        var conversations = messages
            .GroupBy(m => m.ThreadKey)
            .Select(group =>
            {
                var last = group.First();
                var otherId = last.SenderUserId == userId ? last.RecipientUserId : last.SenderUserId;
                return new
                {
                    ThreadKey = group.Key,
                    OtherId = otherId,
                    ContextType = last.ContextType,
                    ContextId = last.ContextId,
                    LastMessage = last.Body,
                    LastMessageAtUtc = last.SentAtUtc,
                    Unread = group.Count(m => m.RecipientUserId == userId && !m.IsRead),
                    Total = group.Count(),
                };
            })
            .OrderByDescending(c => c.LastMessageAtUtc)
            .ToList();

        var otherIds = conversations.Select(c => c.OtherId).Distinct().ToList();
        var names = await db.Users
            .AsNoTracking()
            .Where(u => otherIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var items = conversations.Select(c => new ConversationDto
        {
            ThreadKey = c.ThreadKey,
            OtherUserId = c.OtherId,
            OtherDisplayName = names.GetValueOrDefault(c.OtherId, "User"),
            ContextType = (int)c.ContextType,
            ContextId = c.ContextId,
            LastMessage = c.LastMessage,
            LastMessageAtUtc = c.LastMessageAtUtc,
            UnreadCount = c.Unread,
            TotalMessages = c.Total,
        }).ToList();

        return new PagedResult<ConversationDto>
        {
            Items = items,
            Page = 1,
            PageSize = Math.Max(1, items.Count),
            Total = items.Count,
            TotalPages = 1,
        };
    }

    public async Task<PagedResult<MessageDto>> GetThreadAsync(Guid userId, string threadKey, int page, int pageSize, CancellationToken ct = default)
    {
        var (p, ps) = PageHelper.Normalize(page, pageSize);

        var query = db.Messages
            .AsNoTracking()
            .Where(m => m.ThreadKey == threadKey && (m.SenderUserId == userId || m.RecipientUserId == userId));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(m => m.SentAtUtc)
            .Skip((p - 1) * ps)
            .Take(ps)
            .ToListAsync(ct);

        var senderIds = items.Select(m => m.SenderUserId).Distinct().ToList();
        var names = await db.Users
            .AsNoTracking()
            .Where(u => senderIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        return new PagedResult<MessageDto>
        {
            Items = items.Select(m => new MessageDto
            {
                Id = m.Id,
                ThreadKey = m.ThreadKey,
                SenderUserId = m.SenderUserId,
                SenderDisplayName = names.GetValueOrDefault(m.SenderUserId, "User"),
                RecipientUserId = m.RecipientUserId,
                ContextType = (int)m.ContextType,
                ContextId = m.ContextId,
                Body = m.Body,
                IsRead = m.IsRead,
                SentAtUtc = m.SentAtUtc,
            }).ToList(),
            Page = p,
            PageSize = ps,
            Total = total,
            TotalPages = (int)Math.Ceiling(total / (double)ps),
        };
    }

    public async Task MarkReadAsync(Guid userId, string threadKey, CancellationToken ct = default)
    {
        var unread = await db.Messages
            .Where(m => m.ThreadKey == threadKey && m.RecipientUserId == userId && !m.IsRead)
            .ToListAsync(ct);
        foreach (var message in unread)
        {
            message.IsRead = true;
        }
        if (unread.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<PagedResult<NotificationDto>> GetNotificationsAsync(Guid userId, bool? unreadOnly, int page, int pageSize, CancellationToken ct = default)
    {
        var (p, ps) = PageHelper.Normalize(page, pageSize);
        var query = db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId);
        if (unreadOnly == true)
        {
            query = query.Where(n => !n.IsRead);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(n => n.CreatedAtUtc)
            .Skip((p - 1) * ps)
            .Take(ps)
            .ToListAsync(ct);

        return new PagedResult<NotificationDto>
        {
            Items = items.Select(n => new NotificationDto
            {
                Id = n.Id,
                Type = (int)n.Type,
                Title = n.Title,
                Body = n.Body,
                Link = n.Link,
                IsRead = n.IsRead,
                CreatedAtUtc = n.CreatedAtUtc ?? DateTime.MinValue,
            }).ToList(),
            Page = p,
            PageSize = ps,
            Total = total,
            TotalPages = (int)Math.Ceiling(total / (double)ps),
        };
    }

    public async Task<NotificationDto> MarkNotificationReadAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct)
            ?? throw ApiException.NotFound("NOTIFICATION_NOT_FOUND", "Notification not found.");
        notification.IsRead = true;
        await db.SaveChangesAsync(ct);
        return new NotificationDto
        {
            Id = notification.Id,
            Type = (int)notification.Type,
            Title = notification.Title,
            Body = notification.Body,
            Link = notification.Link,
            IsRead = notification.IsRead,
            CreatedAtUtc = notification.CreatedAtUtc ?? DateTime.MinValue,
        };
    }

    public async Task MarkAllNotificationsReadAsync(Guid userId, CancellationToken ct = default)
    {
        var unread = await db.Notifications.Where(n => n.UserId == userId && !n.IsRead).ToListAsync(ct);
        foreach (var notification in unread)
        {
            notification.IsRead = true;
        }
        if (unread.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<int> GetUnreadNotificationCountAsync(Guid userId, CancellationToken ct = default) =>
        await db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);

    // ---- internals ----

    private async Task<(Guid RecipientId, MessageContextType ContextType, Guid? ContextId, string ThreadKey)> ResolveThreadAsync(
        Guid senderUserId, SendMessageRequest request, CancellationToken ct)
    {
        if (request.ContextId is not null && request.ContextType is not null)
        {
            var contextType = (MessageContextType)request.ContextType.Value;
            if (contextType == MessageContextType.Rfq)
            {
                var rfq = await db.TradeRequests
                    .Include(r => r.Quotations)
                    .ThenInclude(q => q.SupplierBusiness)
                    .FirstOrDefaultAsync(r => r.Id == request.ContextId.Value, ct)
                    ?? throw ApiException.NotFound("RFQ_NOT_FOUND", "RFQ not found.");

                Guid recipientId;
                if (rfq.BuyerUserId == senderUserId)
                {
                    // Buyer messaging a specific quoting supplier.
                    if (request.RecipientUserId is null)
                    {
                        throw ApiException.BadRequest("RECIPIENT_REQUIRED", "Select the supplier to message.");
                    }
                    var supplierQuoted = rfq.Quotations.Any(q =>
                        q.Status != QuotationStatus.Withdrawn &&
                        q.SupplierBusiness!.OwnerUserId == request.RecipientUserId.Value);
                    if (!supplierQuoted)
                    {
                        throw ApiException.Forbidden("NOT_PARTICIPANT", "That supplier has not quoted on this RFQ.");
                    }
                    recipientId = request.RecipientUserId.Value;
                }
                else
                {
                    // A quoting supplier messaging the buyer.
                    var isSupplier = rfq.Quotations.Any(q =>
                        q.Status != QuotationStatus.Withdrawn &&
                        q.SupplierBusiness!.OwnerUserId == senderUserId);
                    if (!isSupplier)
                    {
                        throw ApiException.Forbidden("NOT_PARTICIPANT", "You must quote on this RFQ to message the buyer.");
                    }
                    recipientId = rfq.BuyerUserId;
                }

                var key = $"rfq:{rfq.Id:N}:{OrderedPair(senderUserId, recipientId)}";
                return (recipientId, MessageContextType.Rfq, rfq.Id, key);
            }

            throw ApiException.BadRequest("CONTEXT_UNSUPPORTED", "Only RFQ contexts are supported for scoped threads.");
        }

        // Direct (general) message.
        if (request.RecipientUserId is null)
        {
            throw ApiException.BadRequest("RECIPIENT_REQUIRED", "A recipient is required for a direct message.");
        }
        if (request.RecipientUserId.Value == senderUserId)
        {
            throw ApiException.BadRequest("SELF_MESSAGE", "You cannot message yourself.");
        }
        var recipientExists = await db.Users.AnyAsync(u => u.Id == request.RecipientUserId.Value, ct);
        if (!recipientExists)
        {
            throw ApiException.BadRequest("RECIPIENT_NOT_FOUND", "Recipient does not exist.");
        }

        var generalKey = $"user:{OrderedPair(senderUserId, request.RecipientUserId.Value)}";
        return (request.RecipientUserId.Value, MessageContextType.General, null, generalKey);
    }

    private static string OrderedPair(Guid a, Guid b)
    {
        var (lo, hi) = a.CompareTo(b) <= 0 ? (a, b) : (b, a);
        return $"{lo:N}{hi:N}";
    }
}