using AgroTrade.Application.Common;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Application.Trade;

/// <summary>
/// Trade execution, task 2.1: orders created from an accepted quotation (lossless) or by
/// manual entry, with buyer/seller order lists and detail reads (plan §03 — Orders).
/// Lifecycle transitions beyond Placed, inventory reservation, documents, payments and
/// shipments are tasks 2.2-2.6.
/// </summary>
public interface IOrderService
{
    Task<OrderDetailDto> CreateFromQuotationAsync(Guid buyerUserId, CreateOrderFromQuotationRequest request, CancellationToken ct = default);

    Task<OrderDetailDto> CreateManualAsync(Guid buyerUserId, CreateManualOrderRequest request, CancellationToken ct = default);

    Task<PagedResult<OrderCardDto>> GetMineAsync(Guid userId, OrderListQuery query, CancellationToken ct = default);

    /// <summary>Orders where the caller's business is the seller.</summary>
    Task<PagedResult<OrderCardDto>> GetSellingAsync(Guid userId, OrderListQuery query, CancellationToken ct = default);

    Task<OrderDetailDto> GetAsync(Guid id, Guid userId, CancellationToken ct = default);
}