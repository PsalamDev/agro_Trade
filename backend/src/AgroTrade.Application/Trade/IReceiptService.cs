namespace AgroTrade.Application.Trade;

/// <summary>
/// Receipts for settled payments. Settlement writes one automatically; this service is the read,
/// plus an idempotent re-issue so a client can fetch the document on demand. Scoped to the order,
/// so the viewer must be a party to it (or an administrator).
/// </summary>
public interface IReceiptService
{
    /// <summary>
    /// Returns the receipt for a settled payment, writing it if settlement has not already.
    /// A payment that has not settled is a 409; a non-party is a 404.
    /// </summary>
    Task<ReceiptResponse> GenerateReceiptAsync(
        Guid userId, bool isAdmin, Guid orderId, Guid paymentId, CancellationToken ct);

    /// <summary>Receipts on the order, newest first. Empty for a non-party.</summary>
    Task<IReadOnlyList<ReceiptResponse>> ListForOrderAsync(
        Guid orderId, Guid userId, bool isAdmin, CancellationToken ct);
}
