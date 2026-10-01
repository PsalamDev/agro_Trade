using AgroTrade.Application.Common;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Application.Trade;

/// <summary>
/// Trade execution: orders created from an accepted quotation (lossless) or by manual entry,
/// buyer/seller order lists, detail reads, and the order lifecycle with stock reservation
/// (plan §03 — Orders).
/// </summary>
public interface IOrderService
{
    Task<OrderDetailDto> CreateFromQuotationAsync(Guid buyerUserId, CreateOrderFromQuotationRequest request, CancellationToken ct = default);

    Task<OrderDetailDto> CreateManualAsync(Guid buyerUserId, CreateManualOrderRequest request, CancellationToken ct = default);

    Task<PagedResult<OrderCardDto>> GetMineAsync(Guid userId, OrderListQuery query, CancellationToken ct = default);

    /// <summary>Orders where the caller's business is the seller.</summary>
    Task<PagedResult<OrderCardDto>> GetSellingAsync(Guid userId, OrderListQuery query, CancellationToken ct = default);

    Task<OrderDetailDto> GetAsync(Guid id, Guid userId, CancellationToken ct = default);

    // ---- Lifecycle (task 2.2) ----
    Task<OrderDetailDto> TransitionAsync(Guid id, Guid userId, OrderTransitionRequest request, CancellationToken ct = default);
}