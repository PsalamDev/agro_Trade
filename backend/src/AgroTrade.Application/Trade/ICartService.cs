namespace AgroTrade.Application.Trade;

/// <summary>
/// Domestic buy-now / cart (Phase 4). One cart per buyer; adding a listing creates the cart
/// lazily and increments the line. Checkout splits the cart into one order per seller — an
/// order row carries exactly one seller and one currency — with a delivery address, a summed
/// delivery fee, and stock reserved across every seller atomically: either the whole cart
/// becomes orders or nothing does.
/// </summary>
public interface ICartService
{
    Task<CartDto> GetMineAsync(Guid userId, CancellationToken ct = default);

    Task<CartDto> AddAsync(Guid userId, AddToCartRequest request, CancellationToken ct = default);

    Task<CartDto> SetQuantityAsync(Guid userId, Guid productId, SetCartItemQuantityRequest request, CancellationToken ct = default);

    Task<CartDto> RemoveAsync(Guid userId, Guid productId, CancellationToken ct = default);

    Task<CheckoutResultDto> CheckoutAsync(Guid userId, CheckoutRequest request, CancellationToken ct = default);
}