using AgroTrade.Domain.Common;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Domain.Trade;

/// <summary>
/// A trade document raised against an order — commercial invoice, packing list, bill of
/// lading, phytosanitary certificate and the rest. Uploaded by either party and moved
/// through the admin review queue (plan §02 — Documents).
/// </summary>
public sealed class TradeDocument : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }

    public DocumentType DocType { get; set; }
    /// <summary>Document's own reference as printed on the paper, e.g. "INV-2026-0042".</summary>
    public string? DocumentReference { get; set; }

    /// <summary>Null for per-party uploads; set for documents the platform itself issues.</summary>
    public Guid? UploadedByUserId { get; set; }

    public string? FileUrl { get; set; }
    public string? FileName { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? ContentType { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Uploaded;

    public string? ReviewNotes { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }

    public DateTime? IssuedOnUtc { get; set; }
    public DateTime? ExpiresOnUtc { get; set; }

    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public Order? Order { get; set; }
}