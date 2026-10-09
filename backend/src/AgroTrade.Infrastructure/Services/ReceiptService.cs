using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using AgroTrade.Domain.Enums;
using AgroTrade.Domain.Trade;
using AgroTrade.Infrastructure.Data;
using AgroTrade.Infrastructure.Trade;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Infrastructure.Services;

/// <summary>
/// Receipts for settled payments. Settlement already writes one automatically; this service is
/// the read, plus an idempotent re-issue for a client that needs the document on demand.
/// </summary>
public sealed class ReceiptService(AgroTradeDbContext db) : IReceiptService
{
    public async Task<ReceiptResponse> GenerateReceiptAsync(
        Guid userId, bool isAdmin, Guid orderId, Guid paymentId, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw ApiException.NotFound("ORDER_NOT_FOUND", "Order not found.");

        if (!isAdmin && await PartyAsync(order, userId, ct) == OrderParty.Outsider)
        {
            throw ApiException.NotFound("ORDER_NOT_FOUND", "Order not found.");
        }

        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId && p.OrderId == orderId, ct)
            ?? throw ApiException.NotFound("PAYMENT_NOT_FOUND", "Payment not found.");

        // A receipt attests that money arrived, so it is only issued for a settled payment.
        if (payment.Status != PaymentStatus.Paid)
        {
            throw ApiException.Conflict(
                "PAYMENT_NOT_SETTLED",
                "A receipt can only be issued for a settled payment.");
        }

        // Idempotent: settlement already writes the receipt, so asking again returns it rather
        // than minting a second one for the same money. The filtered unique index on PaymentId
        // is the backstop for two requests that race past this check.
        var existing = await db.Receipts.FirstOrDefaultAsync(r => r.PaymentId == payment.Id, ct);
        if (existing is not null)
        {
            return ToDto(existing);
        }

        var receipt = new Receipt
        {
            OrderId = orderId,
            PaymentId = payment.Id,
            InvoiceId = await db.Invoices
                .Where(i => i.OrderId == orderId)
                .Select(i => (Guid?)i.Id)
                .FirstOrDefaultAsync(ct),
            ReceiptNumber = $"RCPT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            Amount = payment.Amount,
            Currency = payment.Currency,
            IssuedAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow,
        };

        db.Receipts.Add(receipt);
        await db.SaveChangesAsync(ct);
        return ToDto(receipt);
    }

    public async Task<IReadOnlyList<ReceiptResponse>> ListForOrderAsync(
        Guid orderId, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw ApiException.NotFound("ORDER_NOT_FOUND", "Order not found.");

        if (!isAdmin && await PartyAsync(order, userId, ct) == OrderParty.Outsider)
        {
            throw ApiException.NotFound("ORDER_NOT_FOUND", "Order not found.");
        }

        return await db.Receipts.AsNoTracking()
            .Where(r => r.OrderId == orderId)
            .OrderByDescending(r => r.IssuedAtUtc)
            .Select(r => new ReceiptResponse(
                r.Id, r.OrderId, r.PaymentId, r.InvoiceId, r.ReceiptNumber, r.Amount, r.Currency, r.IssuedAtUtc, r.Notes))
            .ToListAsync(ct);
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

    private static ReceiptResponse ToDto(Receipt r) =>
        new(r.Id, r.OrderId, r.PaymentId, r.InvoiceId, r.ReceiptNumber, r.Amount, r.Currency, r.IssuedAtUtc, r.Notes);
}
