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
}