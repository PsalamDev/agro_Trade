using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using AgroTrade.Domain.Trade;
using AgroTrade.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Infrastructure.Trade;

/// <summary>
/// Stock reservation. Every write to stock goes through here so
/// <c>Product.AvailableQuantity</c> cannot drift from <c>Inventory</c> — the two are updated
/// in the same change set, and the caller commits them together.
///
/// <para>
/// Concurrency is optimistic via the Inventory RowVersion. Two buyers racing for the last
/// lot both pass the availability check, but the loser hits the rowversion on save; that is
/// caught and reported as a 409 rather than surfacing as a 500.
/// </para>
/// </summary>
public sealed class InventoryService(AgroTradeDbContext db) : IInventoryService
{
    public IReadOnlyDictionary<Guid, decimal> QuantitiesFor(IEnumerable<(Guid? ProductId, decimal Quantity)> lines) =>
        lines
            .Where(l => l.ProductId is not null && l.Quantity > 0)
            .GroupBy(l => l.ProductId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

    public Task ReserveAsync(IReadOnlyDictionary<Guid, decimal> wantedByProduct, CancellationToken ct = default) =>
        ApplyAsync(wantedByProduct, Reserve, ct);

    public Task CommitAsync(IReadOnlyDictionary<Guid, decimal> wantedByProduct, CancellationToken ct = default) =>
        ApplyAsync(wantedByProduct, Commit, ct);

    public Task ReleaseAsync(IReadOnlyDictionary<Guid, decimal> wantedByProduct, CancellationToken ct = default) =>
        ApplyAsync(wantedByProduct, Release, ct);

    /// <summary>
    /// Loads the affected rows and runs one adjustment over them. Every operation is
    /// all-or-nothing at the caller's discretion: nothing is saved here, so the caller's
    /// SaveChanges persists stock and whatever else it wrote together.
    /// </summary>
    private async Task ApplyAsync(
        IReadOnlyDictionary<Guid, decimal> wantedByProduct,
        Func<Inventory, Product, decimal, ApiException?> adjust,
        CancellationToken ct)
    {
        if (wantedByProduct.Count == 0)
        {
            return;
        }

        var (inventories, products) = await LoadTrackedAsync(wantedByProduct.Keys, ct);

        foreach (var (productId, quantity) in wantedByProduct)
        {
            var failure = adjust(inventories[productId], products[productId], quantity);
            if (failure is not null)
            {
                throw failure;
            }
        }
    }

    private static ApiException? Reserve(Inventory inventory, Product product, decimal quantity)
    {
        if (inventory.Available < quantity)
        {
            return ApiException.Conflict(
                "INSUFFICIENT_STOCK",
                $"Only {inventory.Available:N2} of '{product.Name}' remain unreserved; {quantity:N2} were requested.");
        }

        inventory.QuantityReserved += quantity;
        product.AvailableQuantity = inventory.Available;
        return null;
    }

    private static ApiException? Commit(Inventory inventory, Product product, decimal quantity)
    {
        // Reserved must cover the commit, otherwise the seller re-declared stock away from a
        // live order. Leave the figures alone and make them reconcile rather than silently
        // driving the listing negative.
        if (inventory.QuantityReserved < quantity)
        {
            return ApiException.Conflict(
                "RESERVATION_MISMATCH",
                $"The reserved quantity for '{product.Name}' no longer covers this order. Reconcile stock before confirming.");
        }

        inventory.QuantityOnHand -= quantity;
        inventory.QuantityReserved -= quantity;
        product.AvailableQuantity = inventory.Available;
        return null;
    }

    private static ApiException? Release(Inventory inventory, Product product, decimal quantity)
    {
        // Clamp rather than throw: a cancellation should put back what it can and not be
        // blocked by stock the seller has since trimmed.
        inventory.QuantityReserved = Math.Max(0, inventory.QuantityReserved - quantity);
        product.AvailableQuantity = inventory.Available;
        return null;
    }

    public async Task<InventoryDto> SetStockAsync(
        Guid productId, Guid ownerUserId, SetStockRequest request, CancellationToken ct = default)
    {
        if (request.QuantityOnHand < 0)
        {
            throw ApiException.BadRequest("STOCK_NEGATIVE", "Stock on hand cannot be negative.");
        }

        var product = await db.Products
            .FirstOrDefaultAsync(p => p.Id == productId && !p.IsDeleted, ct)
            ?? throw ApiException.NotFound("PRODUCT_NOT_FOUND", "Product not found.");

        var isOwner = await db.Businesses
            .AnyAsync(b => b.Id == product.SellerBusinessId && b.OwnerUserId == ownerUserId, ct);

        if (!isOwner)
        {
            throw ApiException.Forbidden("NOT_PRODUCT_OWNER", "You can only set stock on your own listings.");
        }

        var inventory = await db.Inventories.FirstOrDefaultAsync(i => i.ProductId == productId, ct);

        if (inventory is null)
        {
            // First declaration. Seed OnHand from the product's advertised figure so a
            // listing that predates the Inventory table does not lose its stock.
            inventory = new Inventory
            {
                ProductId = productId,
                QuantityOnHand = Math.Max(0, product.AvailableQuantity),
                QuantityReserved = 0,
            };
            db.Inventories.Add(inventory);
        }

        if (request.QuantityOnHand < inventory.QuantityReserved)
        {
            // Stock cannot sit below what live orders already hold, or those orders could
            // never confirm. Clamp rather than reject so the seller can still correct a level.
            inventory.QuantityReserved = request.QuantityOnHand;
        }

        inventory.QuantityOnHand = request.QuantityOnHand;
        product.AvailableQuantity = inventory.Available;

        await SaveAsync(ct);

        return ToDto(inventory, product.Name);
    }

    public async Task<InventoryDto> GetAsync(Guid productId, CancellationToken ct = default)
    {
        var inventory = await db.Inventories
            .AsNoTracking()
            .Include(i => i.Product)
            .FirstOrDefaultAsync(i => i.ProductId == productId, ct);

        if (inventory is not null)
        {
            return ToDto(inventory, inventory.Product?.Name ?? string.Empty);
        }

        var name = await db.Products
            .AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(ct);

        if (name is null)
        {
            throw ApiException.NotFound("PRODUCT_NOT_FOUND", "Product not found.");
        }

        // No stock row means nothing has ever been declared for this listing.
        return new InventoryDto { ProductId = productId, ProductName = name };
    }

    // ---- Helpers ----

    /// <summary>
    /// Loads the inventory rows and their products into the change tracker. Both are needed:
    /// inventories are mutated, and products must be tracked so the availability mirror that
    /// the marketplace reads is written in the same change set.
    /// </summary>
    private async Task<(Dictionary<Guid, Inventory> Inventories, Dictionary<Guid, Product> Products)>
        LoadTrackedAsync(IEnumerable<Guid> productIds, CancellationToken ct)
    {
        var ids = productIds.ToList();

        var products = await db.Products
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var inventories = await db.Inventories
            .Where(i => ids.Contains(i.ProductId))
            .ToDictionaryAsync(i => i.ProductId, ct);

        var missing = ids.Where(id => !inventories.ContainsKey(id)).ToList();

        if (missing.Count > 0)
        {
            // A listing with no stock row has never had a stock level declared. Say so
            // explicitly instead of inventing a row, so the seller is told what to set.
            var names = missing
                .Where(id => products.ContainsKey(id))
                .Select(id => products[id].Name)
                .ToList();

            throw ApiException.Conflict(
                "STOCK_NOT_SET",
                $"Set a stock level for: {string.Join(", ", names)} before ordering against it.");
        }

        return (inventories, products);
    }

    /// <summary>
    /// Saves and translates a lost rowversion race into a clean 409: two buyers can both pass
    /// the availability check for the last lot, but only one can win.
    /// </summary>
    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ApiException.Conflict(
                "STOCK_CHANGED",
                "Stock levels changed while this order was being placed. Review the order and try again.");
        }
    }

    private static InventoryDto ToDto(Inventory inventory, string productName) => new()
    {
        ProductId = inventory.ProductId,
        ProductName = productName,
        QuantityOnHand = inventory.QuantityOnHand,
        QuantityReserved = inventory.QuantityReserved,
        Available = inventory.Available,
    };
}
