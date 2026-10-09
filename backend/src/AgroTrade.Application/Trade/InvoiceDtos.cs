using AgroTrade.Domain.Enums;

namespace AgroTrade.Application.Trade;

public record InvoiceResponse(
    Guid Id,
    Guid OrderId,
    string InvoiceNumber,
    decimal Amount,
    string Currency,
    InvoiceStatus Status,
    DateTime IssuedAtUtc,
    DateTime? DueDateUtc,
    DateTime? PaidAtUtc);

public record GenerateInvoiceRequest(
    Guid OrderId,
    DateTime? DueDateUtc,
    string? Notes);