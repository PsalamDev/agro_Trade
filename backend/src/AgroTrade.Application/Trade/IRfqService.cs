using AgroTrade.Application.Common;

namespace AgroTrade.Application.Trade;

/// <summary>
/// Importer RFQs: publish with destination + trade terms, open directory, exporter
/// quotations with full trade terms, buyer comparison, accept/withdraw, and the
/// negotiation thread (plan §03 — RFQs / Trade Requests). Order handoff is Phase 2.
/// </summary>
public interface IRfqService
{
    Task<TradeRequestDetailDto> CreateAsync(Guid buyerUserId, CreateTradeRequestRequest request, CancellationToken ct = default);

    Task<PagedResult<TradeRequestCardDto>> GetOpenDirectoryAsync(OpenRfqFilters filters, CancellationToken ct = default);

    Task<PagedResult<TradeRequestCardDto>> MineAsync(Guid userId, RfqListQuery query, CancellationToken ct = default);

    Task<TradeRequestDetailDto> GetAsync(Guid id, Guid userId, CancellationToken ct = default);

    // ---- Quotations ----
    Task<QuotationDto> CreateQuotationAsync(Guid rfqId, Guid supplierUserId, CreateQuotationRequest request, CancellationToken ct = default);

    Task<PagedResult<QuotationDto>> GetQuotationsAsync(Guid rfqId, Guid userId, CancellationToken ct = default);

    Task<QuotationDto> GetQuotationAsync(Guid quotationId, Guid userId, CancellationToken ct = default);

    Task<QuotationDto> AcceptQuotationAsync(Guid quotationId, Guid buyerUserId, CancellationToken ct = default);

    Task<QuotationDto> WithdrawQuotationAsync(Guid quotationId, Guid supplierUserId, CancellationToken ct = default);

    /// <summary>
    /// Marks Open RFQs whose deadline has passed as Expired and notifies their buyers. Read paths
    /// call this lazily; the RFQ expiry worker calls it on a schedule. Exposing it keeps a single
    /// definition of "expired". Returns how many RFQs were closed in this pass.
    /// </summary>
    Task<int> ExpireOverdueAsync(CancellationToken ct = default);
}