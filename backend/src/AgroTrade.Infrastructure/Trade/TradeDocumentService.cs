using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using AgroTrade.Domain.Communication;
using AgroTrade.Domain.Enums;
using AgroTrade.Domain.Trade;
using AgroTrade.Infrastructure.Data;
using AgroTrade.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Infrastructure.Trade;

/// <summary>
/// Trade documents against an order, task 2.3: upload by either party, then an admin queue
/// moves each one from Uploaded to Verified or Rejected (plan §02 — Documents).
/// </summary>
/// <remarks>
/// Documents start life unverified and stay that way until a human admin clears them. Nothing
/// here treats an uploaded file as trustworthy on arrival — the status is the whole point.
/// </remarks>
public sealed class TradeDocumentService(
    AgroTradeDbContext db,
    IFileStorage fileStorage) : ITradeDocumentService
{
    public async Task<TradeDocumentDto> UploadAsync(
        Guid orderId, Guid userId, UploadTradeDocumentRequest request, Stream content, CancellationToken ct = default)
    {
        var order = await db.Orders
            .Include(o => o.SellerBusiness)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        // An order the caller has no part in is a 404, not a 403: its existence is itself private.
        if (order is null || await PartyAsync(order, userId, ct) == OrderParty.Outsider)
        {
            throw ApiException.NotFound("ORDER_NOT_FOUND", "Order not found.");
        }

        if (!Enum.IsDefined(request.DocType))
        {
            throw ApiException.BadRequest("INVALID_DOC_TYPE", "That document type is not recognised.");
        }

        // A cancelled order is closed to new paperwork: the trade already unwound, so accepting
        // an invoice against it would be recording a sale that will never be fulfilled.
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Completed)
        {
            throw ApiException.BadRequest(
                "ORDER_CLOSED",
                order.Status == OrderStatus.Cancelled
                    ? "This order was cancelled, so no further documents can be added."
                    : "This order is complete, so no further documents can be added.");
        }

        if (request.ExpiresOnUtc is not null && request.IssuedOnUtc is not null
            && request.ExpiresOnUtc < request.IssuedOnUtc)
        {
            throw ApiException.BadRequest("INVALID_EXPIRY", "The expiry date cannot be before the issue date.");
        }

        var stored = await fileStorage.SaveAsync(content, request.FileName, request.ContentType, ct);

        var document = new TradeDocument
        {
            OrderId = orderId,
            DocType = request.DocType,
            DocumentReference = request.DocumentReference?.Trim() ?? null,
            UploadedByUserId = userId,
            FileUrl = stored.Url,
            FileName = stored.FileName,
            FileSizeBytes = stored.SizeBytes,
            ContentType = stored.ContentType,
            // Straight to the queue. 'Draft' is not used: a document that has not been attached
            // to an order does not exist yet, so there is nothing for a draft row to represent.
            Status = DocumentStatus.Uploaded,
            IssuedOnUtc = request.IssuedOnUtc,
            ExpiresOnUtc = request.ExpiresOnUtc,
        };

        db.TradeDocuments.Add(document);
        await db.SaveChangesAsync(ct);

        // The uploader's own name is resolved here rather than left blank, so the order page
        // can attribute paperwork without a second request.
        var uploaders = await DisplayNamesAsync([userId], ct);
        var dto = Map(document, order);
        return uploaders.TryGetValue(userId, out var name)
            ? dto with { UploadedByDisplayName = name }
            : dto;
    }

    public async Task<IReadOnlyList<TradeDocumentDto>> GetForOrderAsync(
        Guid orderId, Guid userId, bool isAdmin, CancellationToken ct = default)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.SellerBusiness)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order is null || (!isAdmin && await PartyAsync(order, userId, ct) == OrderParty.Outsider))
        {
            throw ApiException.NotFound("ORDER_NOT_FOUND", "Order not found.");
        }

        var documents = await db.TradeDocuments
            .AsNoTracking()
            .Where(d => d.OrderId == orderId)
            .OrderBy(d => d.DocType)
            .ThenByDescending(d => d.CreatedAtUtc)
            .ToListAsync(ct);

        var uploaders = await DisplayNamesAsync(documents.Select(d => d.UploadedByUserId), ct);

        return documents.Select(d =>
        {
            var dto = Map(d, order);
            if (d.UploadedByUserId is not null && uploaders.TryGetValue(d.UploadedByUserId.Value, out var name))
            {
                dto = dto with { UploadedByDisplayName = name };
            }

            return dto;
        }).ToList();
    }

    public async Task<TradeDocumentDto> GetAsync(
        Guid documentId, Guid userId, bool isAdmin, CancellationToken ct = default)
    {
        var document = await db.TradeDocuments
            .AsNoTracking()
            .Include(d => d.Order)
                .ThenInclude(o => o!.SellerBusiness)
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);

        if (document?.Order is null || (!isAdmin && await PartyAsync(document.Order, userId, ct) == OrderParty.Outsider))
        {
            throw ApiException.NotFound("DOCUMENT_NOT_FOUND", "Document not found.");
        }

        var uploaders = await DisplayNamesAsync(
            document.UploadedByUserId is null ? [] : [document.UploadedByUserId.Value], ct);

        var dto = Map(document, document.Order);
        return document.UploadedByUserId is not null
               && uploaders.TryGetValue(document.UploadedByUserId.Value, out var name)
            ? dto with { UploadedByDisplayName = name }
            : dto;
    }

    public async Task DeleteAsync(Guid documentId, Guid userId, bool isAdmin, CancellationToken ct = default)
    {
        var document = await db.TradeDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId, ct)
            ?? throw ApiException.NotFound("DOCUMENT_NOT_FOUND", "Document not found.");

        if (document.Status == DocumentStatus.Verified)
        {
            // A verified document is part of the trade record. Removing it would quietly erase
            // evidence the other side relies on, so it stays and the order keeps its paperwork.
            throw ApiException.BadRequest(
                "DOCUMENT_ALREADY_VERIFIED",
                "A verified document cannot be withdrawn. Ask an administrator if it must be replaced.");
        }

        var ownsUpload = document.UploadedByUserId == userId;
        if (!isAdmin && !ownsUpload)
        {
            // 404 rather than 403, matching the order read rule.
            throw ApiException.NotFound("DOCUMENT_NOT_FOUND", "Document not found.");
        }

        // The stored file goes with the row. Failing to delete it would leave an orphan on disk
        // with nothing pointing at it, so the exception is allowed to propagate and roll back.
        if (!string.IsNullOrWhiteSpace(document.FileUrl))
        {
            await fileStorage.DeleteAsync(document.FileUrl, ct);
        }

        db.TradeDocuments.Remove(document);
        await db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<TradeDocumentDto>> SearchQueueAsync(
        TradeDocumentSearchQuery query, CancellationToken ct = default)
    {
        var (page, pageSize) = PageHelper.Normalize(query.Page, query.PageSize);

        IQueryable<TradeDocument> documents = db.TradeDocuments.AsNoTracking();

        if (query.Status is not null)
        {
            documents = documents.Where(d => d.Status == query.Status.Value);
        }

        if (query.DocType is not null)
        {
            documents = documents.Where(d => d.DocType == query.DocType.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            documents = documents.Where(d =>
                (d.FileName != null && EF.Functions.Like(d.FileName, $"%{term}%"))
                || (d.DocumentReference != null && EF.Functions.Like(d.DocumentReference, $"%{term}%"))
                // A document without an order cannot exist (cascade delete), but the navigation
                // is declared nullable, so it is suppressed rather than guarding in SQL: a
                // missing order row simply fails to match the term, which is the right answer.
                || EF.Functions.Like(d.Order!.OrderNumber, $"%{term}%"));
        }

        var total = await documents.CountAsync(ct);
        var rows = await documents
            .OrderByDescending(d => d.CreatedAtUtc)
            .ThenBy(d => d.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new { Document = d, d.Order!.OrderNumber, d.Order.SellerBusiness!.TradingName })
            .ToListAsync(ct);

        var uploaders = await DisplayNamesAsync(rows.Select(r => r.Document.UploadedByUserId), ct);

        var items = rows.Select(r =>
        {
            var dto = Map(r.Document) with
            {
                OrderNumber = r.OrderNumber,
                OrderCounterparty = r.TradingName,
            };
            return r.Document.UploadedByUserId is not null
                   && uploaders.TryGetValue(r.Document.UploadedByUserId.Value, out var name)
                ? dto with { UploadedByDisplayName = name }
                : dto;
        }).ToList();

        return new PagedResult<TradeDocumentDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }

    public async Task<TradeDocumentDto> ReviewAsync(
        Guid documentId, Guid adminUserId, ReviewDocumentRequest request, CancellationToken ct = default)
    {
        if (request.Status is not (DocumentStatus.Verified or DocumentStatus.Rejected))
        {
            throw ApiException.BadRequest(
                "INVALID_REVIEW_STATUS",
                "A review can only mark a document verified or rejected.");
        }

        var document = await db.TradeDocuments
            .Include(d => d.Order)
                .ThenInclude(o => o!.SellerBusiness)
            .FirstOrDefaultAsync(d => d.Id == documentId, ct)
            ?? throw ApiException.NotFound("DOCUMENT_NOT_FOUND", "Document not found.");

        if (document.Status == request.Status)
        {
            throw ApiException.Conflict(
                "DOCUMENT_ALREADY_REVIEWED",
                $"This document is already {request.Status}.");
        }

        if (request.Status == DocumentStatus.Rejected && string.IsNullOrWhiteSpace(request.Notes))
        {
            // Rejecting without saying why leaves the uploader unable to do anything with the
            // decision, which is the one outcome a review queue must never produce.
            throw ApiException.BadRequest("REVIEW_NOTES_REQUIRED", "Rejecting a document requires a note explaining why.");
        }

        document.Status = request.Status;
        document.ReviewNotes = request.Notes?.Trim() ?? null;
        document.ReviewedByUserId = adminUserId;
        document.ReviewedAtUtc = DateTime.UtcNow;

        var uploaders = await DisplayNamesAsync(
            document.UploadedByUserId is null ? [] : [document.UploadedByUserId.Value], ct);

        // The uploader is told either way: a rejection with no notification is a document that
        // silently never becomes valid.
        if (document.UploadedByUserId is not null)
        {
            var approved = request.Status == DocumentStatus.Verified;
            db.Notifications.Add(new Notification
            {
                UserId = document.UploadedByUserId.Value,
                Type = NotificationType.Document,
                Title = approved
                    ? $"{DocLabel(document.DocType)} verified"
                    : $"{DocLabel(document.DocType)} rejected",
                Body = approved
                    ? $"Your document on order {document.Order?.OrderNumber} was verified by an administrator."
                    : $"An administrator rejected your document on order {document.Order?.OrderNumber}. {document.ReviewNotes}",
                Link = $"/dashboard/orders/{document.OrderId}",
            });
        }

        await db.SaveChangesAsync(ct);

        var dto = Map(document, document.Order);
        return document.UploadedByUserId is not null
               && uploaders.TryGetValue(document.UploadedByUserId.Value, out var name)
            ? dto with { UploadedByDisplayName = name }
            : dto;
    }

    // ---- Helpers ----

    private async Task<OrderParty> PartyAsync(Order order, Guid userId, CancellationToken ct)
    {
        if (order.BuyerUserId == userId)
        {
            return OrderParty.Buyer;
        }

        return await db.Businesses
            .AsNoTracking()
            .AnyAsync(b => b.Id == order.SellerBusinessId && b.OwnerUserId == userId, ct)
            ? OrderParty.Seller
            : OrderParty.Outsider;
    }

    /// <summary>Resolves display names for a set of uploader ids in one round trip.</summary>
    private async Task<Dictionary<Guid, string>> DisplayNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var keys = ids.Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        if (keys.Count == 0)
        {
            return [];
        }

        return await db.Users
            .AsNoTracking()
            .Where(u => keys.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }

    private static TradeDocumentDto Map(TradeDocument d, Order? order = null) => new()
    {
        Id = d.Id,
        OrderId = d.OrderId,
        DocType = d.DocType,
        DocumentReference = d.DocumentReference,
        FileUrl = d.FileUrl,
        FileName = d.FileName,
        FileSizeBytes = d.FileSizeBytes,
        ContentType = d.ContentType,
        Status = d.Status,
        ReviewNotes = d.ReviewNotes,
        ReviewedByUserId = d.ReviewedByUserId,
        ReviewedAtUtc = d.ReviewedAtUtc,
        IssuedOnUtc = d.IssuedOnUtc,
        ExpiresOnUtc = d.ExpiresOnUtc,
        CreatedAtUtc = d.CreatedAtUtc,
        UploadedByUserId = d.UploadedByUserId,
        OrderNumber = order?.OrderNumber ?? string.Empty,
        OrderCounterparty = order?.SellerBusiness?.TradingName,
    };

    /// <summary>Human label for a document type, used in notification titles.</summary>
    private static string DocLabel(DocumentType type) => type switch
    {
        DocumentType.CommercialInvoice => "Commercial invoice",
        DocumentType.PackingList => "Packing list",
        DocumentType.CertificateOfOrigin => "Certificate of origin",
        DocumentType.BillOfLading => "Bill of lading",
        DocumentType.PhytosanitaryCertificate => "Phytosanitary certificate",
        DocumentType.InspectionCertificate => "Inspection certificate",
        DocumentType.ExportPermit => "Export permit",
        DocumentType.ImportPermit => "Import permit",
        _ => "Document",
    };
}