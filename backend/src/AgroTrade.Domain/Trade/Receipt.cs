using AgroTrade.Domain.Common;

namespace AgroTrade.Domain.Trade;

public sealed class Receipt : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? InvoiceId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime IssuedAtUtc { get; set; }
    public string? Notes { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Order? Order { get; set; }
    public Payment? Payment { get; set; }
    public Invoice? Invoice { get; set; }
}