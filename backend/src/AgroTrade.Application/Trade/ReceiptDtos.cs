namespace AgroTrade.Application.Trade;

public record ReceiptResponse(
    Guid Id,
    Guid OrderId,
    Guid? PaymentId,
    Guid? InvoiceId,
    string ReceiptNumber,
    decimal Amount,
    string Currency,
    DateTime IssuedAtUtc,
    string? Notes);