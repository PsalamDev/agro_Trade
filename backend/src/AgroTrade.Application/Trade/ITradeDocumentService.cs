using AgroTrade.Application.Common;

namespace AgroTrade.Application.Trade;

/// <summary>
/// Trade documents against an order — commercial invoice, packing list, certificate of
/// origin, bill of lading, phytosanitary certificate and the rest — plus the admin review
/// queue that moves them from Uploaded to Verified or Rejected (plan §02 — Documents).
///
/// <para>
/// Both parties may upload: in practice the seller issues the invoice and shipping papers and
/// the buyer the import permit. Both see everything on the order they share — a commercial
/// invoice is not a secret from the counterparty paying it — so visibility follows the order
/// rather than the document.
/// </para>
/// </summary>
public interface ITradeDocumentService
{
    /// <summary>
    /// Stores a document against an order in <see cref="Domain.Enums.DocumentStatus.Uploaded"/>
    /// and queues it for admin review. Either party to the order may upload.
    /// </summary>
    Task<TradeDocumentDto> UploadAsync(
        Guid orderId, Guid userId, UploadTradeDocumentRequest request, Stream content, CancellationToken ct = default);

    /// <summary>Documents on an order, filtered to what the viewer is allowed to see.</summary>
    Task<IReadOnlyList<TradeDocumentDto>> GetForOrderAsync(Guid orderId, Guid userId, bool isAdmin, CancellationToken ct = default);

    /// <summary>One document. Returns 404 to anyone who cannot see it, matching the order read rule.</summary>
    Task<TradeDocumentDto> GetAsync(Guid documentId, Guid userId, bool isAdmin, CancellationToken ct = default);

    /// <summary>Withdraws a document the viewer uploaded. Verified documents cannot be withdrawn.</summary>
    Task DeleteAsync(Guid documentId, Guid userId, bool isAdmin, CancellationToken ct = default);

    /// <summary>Admin queue: documents awaiting review, optionally filtered by status and type.</summary>
    Task<PagedResult<TradeDocumentDto>> SearchQueueAsync(TradeDocumentSearchQuery query, CancellationToken ct = default);

    /// <summary>Approves or rejects a document. Rejection requires notes explaining why.</summary>
    Task<TradeDocumentDto> ReviewAsync(Guid documentId, Guid adminUserId, ReviewDocumentRequest request, CancellationToken ct = default);
}