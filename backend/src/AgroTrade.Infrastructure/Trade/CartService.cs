using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using AgroTrade.Domain.Communication;
using AgroTrade.Domain.Enums;
using AgroTrade.Domain.Trade;
using AgroTrade.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Infrastructure.Trade;

/// <summary>
/// Domestic buy-now / cart (Phase 4). One cart per buyer, created lazily on first use; lines
/// hold product ids and quantities, and every read resolves the listing's live price, delivery
/// fee and availability.
///
/// <para>
/// Checkout fans the cart out into one order per seller family — an order row carries exactly
/// one seller and one currency — with a delivery address, a summed delivery fee, and stock
/// reserved across the whole cart in one all-or-nothing change set: every seller gets their
/// order or no seller does. Domestic orders fix their trade terms to what a doorstep delivery
/// actually is: DAP (Delivered at Place), road transport, destination = the seller's own
/// country, and payment up front.
/// </para>
/// </summary>
public sealed class CartService(AgroTradeDbContext db, IInventoryService inventory) : ICartService
{
    public async Task<CartDto> GetMineAsync(Guid userId, CancellationToken ct = default)
    {
        var cart = await LoadCartForReadAsync(userId, ct);
        return ToDto(cart);
    }

    public async Task<CartDto> AddAsync(Guid userId, AddToCartRequest request, CancellationToken ct = default)
    {
        var product = await db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.ProductId, ct)
            ?? throw ApiException.NotFound("PRODUCT_NOT_FOUND", "Product not found.");

        var cart = await CartRowAsync(userId, ct);
        var existing = cart.Items.FirstOrDefault(i => i.ProductId == product.Id)?.Quantity ?? 0m;

        // Checked against the available bucket, not just the increment: a second Add on the same
        // listing is a bigger order, and stock is just as finite either way.
        EnsurePurchasable(product, existing + request.Quantity);

        var line = cart.Items.FirstOrDefault(i => i.ProductId == product.Id);
        if (line is null)
        {
            // Added through the DbSet, not the collection: a new line on an already-persisted
            // cart must be tracked explicitly as Added. Relying on DetectChanges to discover the
            // collection member here mislabels it Modified, so EF generates an UPDATE against a
            // row that does not exist yet ("expected to affect 1 row(s), but actually affected
            // 0") and the insert dies. DbSet.Add produces an INSERT as intended.
            db.CartItems.Add(new CartItem
            {
                CartId = cart.Id,
                ProductId = product.Id,
                Quantity = request.Quantity,
            });
        }
        else
        {
            line.Quantity = existing + request.Quantity;
        }

        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(userId, ct);
    }

    public async Task<CartDto> SetQuantityAsync(
        Guid userId, Guid productId, SetCartItemQuantityRequest request, CancellationToken ct = default)
    {
        var cart = await CartRowAsync(userId, ct);
        var line = cart.Items.FirstOrDefault(i => i.ProductId == productId)
            ?? throw ApiException.NotFound("CART_ITEM_NOT_FOUND", "That item is not in your cart.");

        var product = await db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId, ct)
            ?? throw ApiException.NotFound("PRODUCT_NOT_FOUND", "Product not found.");

        EnsurePurchasable(product, request.Quantity);
        line.Quantity = request.Quantity;

        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(userId, ct);
    }

    public async Task<CartDto> RemoveAsync(Guid userId, Guid productId, CancellationToken ct = default)
    {
        var cart = await CartRowAsync(userId, ct);
        var line = cart.Items.FirstOrDefault(i => i.ProductId == productId);

        // Idempotent: removing something that is already gone is not an error, and it saves the
        // client a conditional after it already cleared the UI.
        if (line is not null)
        {
            cart.Items.Remove(line);
            await db.SaveChangesAsync(ct);
        }

        return await ToDtoAsync(userId, ct);
    }

    public async Task<CheckoutResultDto> CheckoutAsync(Guid userId, CheckoutRequest request, CancellationToken ct = default)
    {
        var cart = await db.Carts
            .Include(c => c.Items)
            .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(c => c.UserId == userId, ct);

        if (cart is null || cart.Items.Count == 0)
        {
            throw ApiException.BadRequest("CART_EMPTY", "Your cart is empty. Add a listing before checking out.");
        }

        // A cart is a promise, not a contract: every line must still be a live, domestic listing
        // at the moment of the sale, or the whole checkout refuses rather than silently dropping
        // money the buyer thought they were spending.
        foreach (var line in cart.Items)
        {
            var p = line.Product;
            if (p is null || p.IsDeleted || p.ListingStatus != ListingStatus.Active
                || p.ModerationStatus == ModerationStatus.Suspended)
            {
                throw ApiException.BadRequest(
                    "PRODUCT_UNAVAILABLE",
                    $"'{p?.Name ?? "An item in your cart"}' is no longer available. Remove it from your cart and try again.");
            }

            if (!p.SellsDomestically)
            {
                throw ApiException.BadRequest(
                    "PRODUCT_NOT_DOMESTIC",
                    $"'{p.Name}' is not available for domestic delivery. Remove it from your cart.");
            }
        }

        // Group by seller. An order row carries one seller and one currency, so a mixed cart
        // becomes several orders rather than one order that cannot represent its own terms.
        var groups = cart.Items
            .GroupBy(i => i.Product!.SellerBusinessId)
            .Select(g => new SellerGroup(g.Key, g.OrderBy(i => i.Product!.Name).ToList()))
            .ToList();

        var businessIds = groups.Select(g => g.SellerBusinessId).ToList();
        var businesses = await db.Businesses
            .AsNoTracking()
            .Where(b => businessIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, ct);

        // Deterministic ordering so a fan-out checkout allocates its numbers and reports its
        // results the same way every run — which is also what makes multi-seller tests stable.
        var ordered = groups
            .OrderBy(g => businesses[g.SellerBusinessId].TradingName)
            .ToList();

        foreach (var group in ordered)
        {
            var business = businesses[group.SellerBusinessId];

            if (business.OwnerUserId == userId)
            {
                throw ApiException.BadRequest(
                    "ORDER_SELF_SALE",
                    $"You cannot buy from '{business.TradingName}' — that is your own business.");
            }

            var currencies = group.Lines.Select(i => i.Product!.Currency).Distinct().ToList();
            if (currencies.Count > 1)
            {
                throw ApiException.BadRequest(
                    "CURRENCY_MIXED",
                    $"'{business.TradingName}' lists its items in different currencies ({string.Join(", ", currencies)}). Buy from them in separate checkouts.");
            }

            // "Domestic" is a fixed fact, not a preference: the seller delivers within its own
            // country, so the checkout's single delivery address has to land there for the order
            // to be domestic. A cross-border address is an export — a different product entirely.
            var origin = group.Lines.Select(i => i.Product!.OriginCountry).Distinct().Single();
            var deliveryCountry = request.Destination.Country.Trim().ToUpperInvariant();
            if (!string.Equals(deliveryCountry, origin, StringComparison.Ordinal))
            {
                throw ApiException.BadRequest(
                    "DELIVERY_COUNTRY_MISMATCH",
                    $"'{business.TradingName}' delivers domestically in {origin}, but your delivery address is in {deliveryCountry}. Enter an address in the seller's country.");
            }
        }

        // The destination is snapshotted onto every order, not written as an address-book row:
        // an order is the record of where goods were to go, and must not change (or vanish)
        // when the buyer later edits their address book.
        var destination = new
        {
            Line1 = request.Destination.Line1.Trim(),
            Line2 = request.Destination.Line2?.Trim(),
            City = request.Destination.City.Trim(),
            State = request.Destination.State?.Trim(),
            Country = request.Destination.Country.Trim().ToUpperInvariant(),
            PostalCode = request.Destination.PostalCode?.Trim(),
        };

        var now = DateTime.UtcNow;
        var orders = new List<Order>();

        foreach (var group in ordered)
        {
            var lines = group.Lines;
            var currency = lines[0].Product!.Currency;
            var leadDays = lines.Max(i => i.Product!.LeadTimeDays ?? 7);

            var order = new Order
            {
                OrderNumber = string.Empty, // assigned under lock with the whole fan-out
                BuyerUserId = userId,
                SellerBusinessId = group.SellerBusinessId,
                Status = OrderStatus.Placed, // buy-now: no draft stage
                PaymentStatus = PaymentStatus.None,
                Currency = currency,
                // Delivered at Place is the incoterm that actually describes a doorstep
                // delivery; the export incoterms on the listing (FOB/CIF…) do not apply locally.
                Incoterms = Incoterms.Dap,
                PaymentTerms = PaymentTerms.TtAdvance,
                DestinationCountry = destination.Country,
                TransportMode = ShipmentMode.Road,
                // The delivery address is snapshotted on the order (see `destination` above);
                // domestic orders therefore leave DestinationAddressId null on purpose.
                DeliveryLine1 = destination.Line1,
                DeliveryLine2 = destination.Line2,
                DeliveryCity = destination.City,
                DeliveryState = destination.State,
                DeliveryPostalCode = destination.PostalCode,
                ExpectedDeliveryUtc = now.AddDays(leadDays),
                PlacedAtUtc = now,
                Notes = BuildNotes(request.Notes),
            };

            foreach (var line in lines)
            {
                var p = line.Product!;
                order.Items.Add(new OrderItem
                {
                    ProductId = p.Id,
                    ProductNameSnapshot = p.Name,
                    ProductSkuSnapshot = null,
                    Quantity = line.Quantity,
                    Unit = p.PriceUnit,
                    UnitPrice = p.Price,
                    LineTotal = decimal.Round(line.Quantity * p.Price, 2),
                    Currency = p.Currency,
                });
            }

            order.Subtotal = order.Items.Sum(i => i.LineTotal);
            order.DeliveryFee = lines.Sum(i => i.Product!.DomesticDeliveryFee);
            order.Total = order.Subtotal + order.DeliveryFee;

            db.Orders.Add(order);
            orders.Add(order);
        }

        // One reservation for the whole cart, in the same change set as the orders: if any
        // seller's stock ran out, everything rolls back and the buyer keeps their cart intact.
        await inventory.ReserveAsync(
            inventory.QuantitiesFor(cart.Items.Select(i => ((Guid?)i.ProductId, i.Quantity))), ct);

        await SequenceAllocator.AllocateRangeAsync(
            db,
            table: "Orders",
            column: "OrderNumber",
            prefix: $"AG-{now.Year}-",
            count: orders.Count,
            assign: numbers =>
            {
                for (var i = 0; i < orders.Count; i++)
                {
                    orders[i].OrderNumber = numbers[i];
                }
            },
            save: async token =>
            {
                foreach (var order in orders)
                {
                    db.Notifications.Add(new Notification
                    {
                        UserId = businesses[order.SellerBusinessId].OwnerUserId,
                        Type = NotificationType.Order,
                        Title = $"New order {order.OrderNumber}",
                        Body = $"You received a domestic order worth {order.Currency} {order.Total:N2}.",
                        Link = $"/dashboard/orders/{order.Id}",
                    });
                }

                // The cart has been converted into orders; the lines must not survive to be
                // checked out twice.
                db.CartItems.RemoveRange(cart.Items);

                await db.SaveChangesAsync(token);
            },
            ct);

        return new CheckoutResultDto
        {
            Orders = orders.Select(o => new CheckoutOrderDto
            {
                OrderId = o.Id,
                OrderNumber = o.OrderNumber,
                SellerBusinessId = o.SellerBusinessId,
                SellerTradingName = businesses[o.SellerBusinessId].TradingName,
                Currency = o.Currency,
                Subtotal = o.Subtotal,
                DeliveryFee = o.DeliveryFee,
                Total = o.Total,
                ItemCount = o.Items.Count,
                ExpectedDeliveryUtc = o.ExpectedDeliveryUtc,
            }).ToList(),
        };
    }

    // ---- Helpers ----

    private static void EnsurePurchasable(Product product, decimal quantity)
    {
        if (product.IsDeleted || product.ListingStatus != ListingStatus.Active
            || product.ModerationStatus == ModerationStatus.Suspended)
        {
            throw ApiException.BadRequest(
                "PRODUCT_UNAVAILABLE",
                $"'{product.Name}' is no longer available for purchase.");
        }

        if (!product.SellsDomestically)
        {
            throw ApiException.BadRequest(
                "PRODUCT_NOT_DOMESTIC",
                $"'{product.Name}' is not available for domestic delivery.");
        }

        // MOQ is a bulk-trade term; domestic retail checks out against the available bucket
        // instead, so a listing can be sold locally from the same stock that backs export lots.
        if (quantity > product.AvailableQuantity)
        {
            throw ApiException.BadRequest(
                "QUANTITY_EXCEEDS_AVAILABLE",
                $"Only {product.AvailableQuantity:N2} of '{product.Name}' are available; you asked for {quantity:N2}.");
        }
    }

    private static string BuildNotes(string? buyerNotes)
    {
        var baseNote = "Placed from cart.";
        return string.IsNullOrWhiteSpace(buyerNotes)
            ? baseNote
            : $"{baseNote}\nBuyer note: {buyerNotes.Trim()}";
    }

    /// <summary>Gets the user's cart, creating the row on first use. Tracked — the caller edits it.</summary>
    private async Task<Cart> CartRowAsync(Guid userId, CancellationToken ct)
    {
        var cart = await db.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (cart is null)
        {
            cart = new Cart { UserId = userId };
            db.Carts.Add(cart);
            await db.SaveChangesAsync(ct);
        }
        return cart;
    }

    private async Task<CartDto> ToDtoAsync(Guid userId, CancellationToken ct)
    {
        var cart = await LoadCartForReadAsync(userId, ct);
        return ToDto(cart);
    }

    private Task<Cart?> LoadCartForReadAsync(Guid userId, CancellationToken ct) =>
        db.Carts
            .AsNoTracking()
            .Include(c => c.Items)
            .ThenInclude(i => i.Product!.SellerBusiness)
            .Include(c => c.Items)
            .ThenInclude(i => i.Product!.Images)
            .FirstOrDefaultAsync(c => c.UserId == userId, ct);

    private static CartDto ToDto(Cart? cart)
    {
        var lines = cart?.Items
            .OrderBy(i => i.Product?.Name ?? string.Empty)
            .Select(i =>
            {
                var p = i.Product;
                var primary = p?.Images.FirstOrDefault(img => img.IsPrimary) ?? p?.Images.FirstOrDefault();
                var live = p is not null && !p.IsDeleted
                    && p.ListingStatus == ListingStatus.Active
                    && p.ModerationStatus == ModerationStatus.Cleared
                    && p.SellsDomestically;
                return new CartItemDto
                {
                    ProductId = i.ProductId,
                    ProductName = p?.Name ?? string.Empty,
                    Price = p?.Price ?? 0m,
                    PriceUnit = p is null ? (int)PriceUnit.Other : (int)p.PriceUnit,
                    Currency = p?.Currency ?? "USD",
                    Quantity = i.Quantity,
                    LineTotal = decimal.Round(i.Quantity * (p?.Price ?? 0m), 2),
                    DeliveryFee = p?.DomesticDeliveryFee ?? 0m,
                    AvailableQuantity = p?.AvailableQuantity ?? 0m,
                    SellerBusinessId = p?.SellerBusinessId ?? Guid.Empty,
                    SellerTradingName = p?.SellerBusiness?.TradingName ?? string.Empty,
                    PrimaryImageUrl = primary?.Url,
                    IsAvailable = live && i.Quantity <= (p?.AvailableQuantity ?? 0m),
                };
            })
            .ToList() ?? [];

        return new CartDto
        {
            Items = lines,
            TotalQuantity = lines.Sum(i => (int)Math.Round(i.Quantity, MidpointRounding.AwayFromZero)),
        };
    }

    /// <summary>One seller's slice of the cart, grouped before fan-out.</summary>
    private sealed record SellerGroup(Guid SellerBusinessId, List<CartItem> Lines);
}