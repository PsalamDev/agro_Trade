using AgroTrade.Domain.Common;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Domain.Trade;

/// <summary>
/// A payment against an order. Off-platform transfers (bank T/T, L/C) are recorded here
/// with <see cref="PaymentProvider.ManualTransfer"/> and a remittance reference — no
/// gateway, no webhook (plan §02 — Payments).
/// </summary>
public sealed class Payment : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }

    /// <summary>Human-facing reference, unique. e.g. "PAY-2026-000001".</summary>
    public string Reference { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public PaymentProvider Provider { get; set; } = PaymentProvider.ManualTransfer;
    public PaymentMethod Method { get; set; } = PaymentMethod.BankTransfer;

    /// <summary>Bank reference / remittance advice for manual transfers.</summary>
    public string? RemittanceReference { get; set; }
    /// <summary>Bank name for manual transfers, so the buyer can reconcile.</summary>
    public string? BankName { get; set; }
    public DateTime? ValueDateUtc { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? FailureReason { get; set; }

    public DateTime InitiatedAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public string? Notes { get; set; }

    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public Order? Order { get; set; }
}