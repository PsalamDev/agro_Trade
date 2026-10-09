using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using AgroTrade.Domain.Enums;
using AgroTrade.Domain.Trade;
using AgroTrade.Infrastructure.Data;
using AgroTrade.Infrastructure.Trade;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Infrastructure.Services;

/// <summary>
/// Invoices raised against a placed order (the document the buyer pays), and the read the buyer
/// uses to see what is owed.
/// </summary>
public sealed class InvoiceService(AgroTradeDbContext db) : IInvoiceService
{
    public async Task<InvoiceResponse> GenerateInvoiceAsync(
        Guid userId, bool isAdmin, GenerateInvoiceRequest request, CancellationToken ct)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == request.OrderId, ct)
            ?? throw ApiException.NotFound("ORDER_NOT_FOUND", "Order not found.");

        // Issuing is the seller's act, so a non-party gets the same 404 an unknown order gets:
        // the existence of somebody else's trade is not the caller's to learn.
        if (!isAdmin && await PartyAsync(order, userId, ct) != OrderParty.Seller)
        {
            throw ApiException.NotFound("ORDER_NOT_FOUND", "Order not found.");
        }

        // One invoice per order. A second would be a second claim on the same money, and the
        // order's total is the only figure this document carries. The unique index on OrderId
        // is the backstop for two requests that race past this check.
        if (await db.Invoices.AnyAsync(i => i.OrderId == order.Id, ct))
        {
            throw ApiException.Conflict(
                "INVOICE_EXISTS", "An invoice has already been issued for this order.");
        }

        var invoice = new Domain.Trade.Invoice
        {
            OrderId = order.Id,
            InvoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            Amount = order.Total,
            Currency = order.Currency,
            Status = InvoiceStatus.Issued,
            IssuedAtUtc = DateTime.UtcNow,
            DueDateUtc = request.DueDateUtc,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
        };

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);

        return ToDto(invoice);
    }

    public async Task<InvoiceResponse> GetInvoiceAsync(Guid orderId, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw ApiException.NotFound("ORDER_NOT_FOUND", "Order not found.");

        if (!isAdmin && await PartyAsync(order, userId, ct) == OrderParty.Outsider)
        {
            throw ApiException.NotFound("ORDER_NOT_FOUND", "Order not found.");
        }

        var invoice = await db.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.OrderId == orderId, ct)
            ?? throw ApiException.NotFound("INVOICE_NOT_FOUND", "No invoice has been issued for this order.");

        return ToDto(invoice);
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

    private static InvoiceResponse ToDto(Domain.Trade.Invoice i) =>
        new(i.Id, i.OrderId, i.InvoiceNumber, i.Amount, i.Currency, i.Status, i.IssuedAtUtc, i.DueDateUtc, i.PaidAtUtc);
}
