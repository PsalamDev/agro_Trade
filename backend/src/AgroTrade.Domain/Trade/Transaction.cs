using AgroTrade.Domain.Common;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Domain.Trade;

/// <summary>
/// Append-only ledger of money movements. Payments record intent against an order; the
/// ledger is the durable financial history and survives payments being corrected
/// (plan §02 — Transactions).
/// </summary>
public sealed class Transaction : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }

    /// <summary>Unique ledger reference, e.g. "TXN-2026-000001".</summary>
    public string Reference { get; set; } = string.Empty;

    public TransactionType Type { get; set; } = TransactionType.Payment;

    /// <summary>Positive for money in, negative for refunds out.</summary>
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>Null for off-platform entries that have no originating payment row.</summary>
    public Guid? PaymentId { get; set; }

    public DateTime OccurredAtUtc { get; set; }
    public string? Narrative { get; set; }

    /// <summary>Counterparty reference, e.g. the bank's own trace id.</summary>
    public string? ExternalReference { get; set; }

    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public Order? Order { get; set; }
    public Payment? Payment { get; set; }
}