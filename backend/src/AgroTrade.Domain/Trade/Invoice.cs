using AgroTrade.Domain.Common;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Domain.Trade;

public sealed class Invoice : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public DateTime IssuedAtUtc { get; set; }
    public DateTime? DueDateUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public string? Notes { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Order? Order { get; set; }
}