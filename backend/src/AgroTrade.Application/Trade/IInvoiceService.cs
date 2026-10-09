namespace AgroTrade.Application.Trade;

/// <summary>
/// Invoices raised against a placed order — the document the buyer is asked to pay — and the
/// read both parties use to see what is owed. Scoped to the order, so the viewer must be a
/// party to it (or an administrator), matching the order and trade-document read rule.
/// </summary>
public interface IInvoiceService
{
    /// <summary>
    /// Issues the order's invoice for its total, in <c>Issued</c>. The seller's act; one per
    /// order. An order that already has one is a 409, and a non-party is a 404.
    /// </summary>
    Task<InvoiceResponse> GenerateInvoiceAsync(
        Guid userId, bool isAdmin, GenerateInvoiceRequest request, CancellationToken ct);

    /// <summary>The order's invoice. 404 to anyone who is not a party (or an admin).</summary>
    Task<InvoiceResponse> GetInvoiceAsync(Guid orderId, Guid userId, bool isAdmin, CancellationToken ct);
}
