using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using AgroTrade.Domain.Communication;
using AgroTrade.Domain.Enums;
using AgroTrade.Domain.Trade;
using AgroTrade.Infrastructure.Data;
using AgroTrade.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Infrastructure.Trade;

/// <summary>
/// Trade execution, task 2.1. Converts an accepted quotation into an order without losing
/// any agreed term, or creates one by hand. Trade terms are copied onto the order rather
/// than referenced, so later edits to the RFQ or quotation cannot rewrite what was agreed
/// (plan §02 — Orders).
/// </summary>
public sealed class OrderService(AgroTradeDbContext db, IInventoryService inventory) : IOrderService
{
    public async Task<OrderDetailDto> CreateFromQuotationAsync(
        Guid buyerUserId,
        CreateOrderFromQuotationRequest request,
        CancellationToken ct = default)
    {
        var quotation = await db.Quotations
            .Include(q => q.TradeRequest)
            .FirstOrDefaultAsync(q => q.Id == request.QuotationId, ct)
            ?? throw ApiException.NotFound("QUOTATION_NOT_FOUND", "Quotation not found.");

        // Only the RFQ's own buyer may convert, and only a quotation they accepted.
        var rfq = quotation.TradeRequest
            ?? throw ApiException.NotFound("RFQ_NOT_FOUND", "The RFQ behind this quotation no longer exists.");

        if (rfq.BuyerUserId != buyerUserId)
        {
            throw ApiException.Forbidden("NOT_RFQ_BUYER", "Only the buyer who published this RFQ can place the order.");
        }

        if (quotation.Status != QuotationStatus.Accepted)
        {
            throw ApiException.BadRequest(
                "QUOTATION_NOT_ACCEPTED",
                "Only an accepted quotation can become an order.");
        }

        // The unique filtered index on QuotationId is the real guard against a double
        // handoff; this check just turns the race into a clean 409 instead of a 500.
        if (await db.Orders.AnyAsync(o => o.QuotationId == quotation.Id, ct))
        {
            throw ApiException.Conflict(
                "QUOTATION_ALREADY_CONVERTED",
                "This quotation has already been converted into an order.");
        }

        if (quotation.ValidUntil is not null && quotation.ValidUntil < DateTime.UtcNow)
        {
            throw ApiException.BadRequest(
                "QUOTATION_EXPIRED",
                "This quotation's validity period has passed; ask the supplier for a new one.");
        }

        var order = new Order
        {
            OrderNumber = await NextOrderNumberAsync(ct),
            QuotationId = quotation.Id,
            TradeRequestId = quotation.TradeRequestId,
            BuyerUserId = buyerUserId,
            SellerBusinessId = quotation.SupplierBusinessId,
            Status = request.PlaceImmediately ? OrderStatus.Placed : OrderStatus.Draft,
            PaymentStatus = PaymentStatus.None,
            // Trade terms copied verbatim from the accepted quotation.
            Currency = quotation.Currency.ToUpperInvariant(),
            Subtotal = quotation.Total,
            Discount = 0m,
            Total = quotation.Total,
            Incoterms = quotation.Incoterms,
            PortOfLoading = quotation.PortOfLoading,
            PortOfDischarge = quotation.PortOfDischarge,
            PaymentTerms = quotation.PaymentTerms,
            DestinationCountry = rfq.DestinationCountry,
            TransportMode = rfq.TransportMode,
            DestinationAddressId = request.DestinationAddressId ?? await DefaultAddressIdAsync(buyerUserId, ct),
            ExpectedDeliveryUtc = rfq.DeliveryDeadline,
            PlacedAtUtc = request.PlaceImmediately ? DateTime.UtcNow : null,
            Notes = request.Notes?.Trim() ?? quotation.Notes,
        };

        // An RFQ is a description of what to buy, so it carries the quantity but no priced
        // line. The agreed total becomes a single snapshotted line rather than an invented
        // unit price - quoting per-unit here would misrepresent what the parties agreed.
        order.Items.Add(new OrderItem
        {
            ProductNameSnapshot = string.IsNullOrWhiteSpace(rfq.Title) ? "Sourced consignment" : rfq.Title.Trim(),
            Quantity = rfq.Quantity,
            Unit = rfq.Unit,
            UnitPrice = rfq.Quantity > 0 ? decimal.Round(quotation.Total / rfq.Quantity, 2) : quotation.Total,
            LineTotal = quotation.Total,
            Currency = order.Currency,
        });

        db.Orders.Add(order);

        // The RFQ and quotation are now spent; leaving them 'Accepted' would let a buyer
        // keep converting the same quote if the unique index were ever dropped.
        rfq.Status = RfqStatus.ConvertedToOrder;

        await NotifyAsync(
            recipientUserId: await SellerOwnerUserIdAsync(quotation.SupplierBusinessId, ct),
            type: NotificationType.Order,
            title: $"New order {order.OrderNumber}",
            body: $"{rfq.Title} was converted into an order.",
            link: $"/dashboard/orders/{order.Id}",
            ct);

        await SaveAsync(ct);
        return await ToDetailAsync(order, buyerUserId, ct);
    }

    public async Task<OrderDetailDto> CreateManualAsync(
        Guid buyerUserId,
        CreateManualOrderRequest request,
        CancellationToken ct = default)
    {
        if (request.Items.Count == 0)
        {
            throw ApiException.BadRequest("ORDER_NO_ITEMS", "An order needs at least one line item.");
        }

        var seller = await db.Businesses
            .FirstOrDefaultAsync(b => b.Id == request.SellerBusinessId && !b.IsDeleted, ct)
            ?? throw ApiException.NotFound("BUSINESS_NOT_FOUND", "Seller business not found.");

        if (seller.OwnerUserId == buyerUserId)
        {
            throw ApiException.BadRequest(
                "ORDER_SELF_SALE",
                "You cannot place an order with your own business.");
        }

        var currency = request.Currency.ToUpperInvariant();

        if (request.Items.Any(i => string.IsNullOrWhiteSpace(i.ProductName)))
        {
            throw ApiException.BadRequest(
                "ORDER_ITEM_NO_NAME",
                "Every line item needs a product name.");
        }

        var order = new Order
        {
            OrderNumber = await NextOrderNumberAsync(ct),
            BuyerUserId = buyerUserId,
            SellerBusinessId = seller.Id,
            Status = OrderStatus.Placed,
            PaymentStatus = PaymentStatus.None,
            Currency = currency,
            Incoterms = (Incoterms)request.Incoterms,
            PaymentTerms = (PaymentTerms)request.PaymentTerms,
            PortOfLoading = request.PortOfLoading?.Trim(),
            PortOfDischarge = request.PortOfDischarge?.Trim(),
            DestinationCountry = request.DestinationCountry?.Trim().ToUpperInvariant(),
            TransportMode = request.TransportMode is null ? null : (ShipmentMode)request.TransportMode.Value,
            DestinationAddressId = request.DestinationAddressId ?? await DefaultAddressIdAsync(buyerUserId, ct),
            ExpectedDeliveryUtc = request.ExpectedDeliveryUtc,
            PlacedAtUtc = DateTime.UtcNow,
            Notes = request.Notes?.Trim(),
        };

        foreach (var line in request.Items)
        {
            order.Items.Add(new OrderItem
            {
                ProductId = line.ProductId,
                ProductNameSnapshot = line.ProductName.Trim(),
                ProductSkuSnapshot = line.Sku?.Trim(),
                Quantity = line.Quantity,
                Unit = (PriceUnit)line.Unit,
                UnitPrice = line.UnitPrice,
                LineTotal = decimal.Round(line.Quantity * line.UnitPrice, 2),
                Currency = currency,
            });
        }

        order.Subtotal = order.Items.Sum(i => i.LineTotal);
        order.Discount = request.Discount;
        order.Total = order.Subtotal - order.Discount;

        if (order.Total < 0)
        {
            throw ApiException.BadRequest(
                "ORDER_DISCOUNT_TOO_LARGE",
                "The discount cannot exceed the order subtotal.");
        }

        db.Orders.Add(order);

        await NotifyAsync(
            recipientUserId: seller.OwnerUserId,
            type: NotificationType.Order,
            title: $"New order {order.OrderNumber}",
            body: $"You received an order worth {currency} {order.Total:N2}.",
            link: $"/dashboard/orders/{order.Id}",
            ct);

        // Manual orders are placed on creation, so hold the stock now. Reservation is computed
        // from the in-memory lines and lands in the same SaveChanges as the order itself — if
        // stock is short the whole thing fails together and nothing is left behind.
        await inventory.ReserveAsync(
            inventory.QuantitiesFor(order.Items.Select(i => (i.ProductId, i.Quantity))), ct);

        await SaveAsync(ct);
        return await ToDetailAsync(order, buyerUserId, ct);
    }

    public async Task<PagedResult<OrderCardDto>> GetMineAsync(Guid userId, OrderListQuery query, CancellationToken ct = default)
    {
        var (page, pageSize) = PageHelper.Normalize(query.Page, query.PageSize);

        var baseQuery = db.Orders
            .AsNoTracking()
            .Where(o => o.BuyerUserId == userId);

        baseQuery = ApplyStatus(baseQuery, query.Status);
        return await ToCardsAsync(baseQuery, page, pageSize, ct);
    }

    public async Task<PagedResult<OrderCardDto>> GetSellingAsync(Guid userId, OrderListQuery query, CancellationToken ct = default)
    {
        var businessIds = await db.Businesses
            .AsNoTracking()
            .Where(b => b.OwnerUserId == userId && !b.IsDeleted)
            .Select(b => b.Id)
            .ToListAsync(ct);

        if (businessIds.Count == 0)
        {
            // No businesses, so nothing to sell - an empty page rather than an error.
            return new PagedResult<OrderCardDto>();
        }

        var (page, pageSize) = PageHelper.Normalize(query.Page, query.PageSize);

        var baseQuery = db.Orders
            .AsNoTracking()
            .Where(o => businessIds.Contains(o.SellerBusinessId));

        baseQuery = ApplyStatus(baseQuery, query.Status);
        return await ToCardsAsync(baseQuery, page, pageSize, ct);
    }

    public async Task<OrderDetailDto> GetAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var order = await OrdersWithDetail()
            .FirstOrDefaultAsync(o => o.Id == id, ct);

        if (order is null)
        {
            throw ApiException.NotFound("ORDER_NOT_FOUND", "Order not found.");
        }

        var isBuyer = order.BuyerUserId == userId;
        var isSeller = await db.Businesses
            .AsNoTracking()
            .AnyAsync(b => b.Id == order.SellerBusinessId && b.OwnerUserId == userId, ct);

        if (!isBuyer && !isSeller)
        {
            // Same 404 as a missing order: a non-party should not learn the order exists.
            throw ApiException.NotFound("ORDER_NOT_FOUND", "Order not found.");
        }

        return Map(order, isBuyer);
    }

    public async Task<OrderDetailDto> TransitionAsync(
        Guid id, Guid userId, OrderTransitionRequest request, CancellationToken ct = default)
    {
        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw ApiException.NotFound("ORDER_NOT_FOUND", "Order not found.");

        var party = await PartyAsync(order, userId, ct);

        if (party == OrderParty.Outsider)
        {
            // Same 404 as a missing order: a non-party should not learn the order exists.
            throw ApiException.NotFound("ORDER_NOT_FOUND", "Order not found.");
        }

        var from = order.Status;
        var to = request.To;

        if (OrderLifecycle.IsTerminal(from))
        {
            throw ApiException.Conflict(
                "ORDER_FINAL_STATE",
                $"Order {order.OrderNumber} is {from} and can no longer change.");
        }

        if (!OrderLifecycle.IsLegal(from, to))
        {
            throw ApiException.Conflict(
                "ORDER_ILLEGAL_TRANSITION",
                $"An order cannot go from {from} to {to}. From {from} it may go to: {OrderLifecycle.NextOptions(from)}.");
        }

        if (!OrderLifecycle.CanPerform(from, to, party))
        {
            throw ApiException.Forbidden(
                "ORDER_TRANSITION_NOT_YOURS",
                $"The {RequiredParty(to).ToString().ToLowerInvariant()} moves an order to {to}, not the {party.ToString().ToLowerInvariant()}.");
        }

        var wanted = inventory.QuantitiesFor(order.Items.Select(i => (i.ProductId, i.Quantity)));

        // Stock moves with the order: held at placement, consumed on confirmation, released
        // on cancellation. Consignment orders (raised from a quotation) hold nothing, so the
        // reservation is empty and these calls are no-ops.
        if (to == OrderStatus.Placed)
        {
            await inventory.ReserveAsync(wanted, ct);
        }
        else if (OrderLifecycle.CommitsStock(to))
        {
            await inventory.CommitAsync(wanted, ct);
        }
        else if (to == OrderStatus.Cancelled)
        {
            await inventory.ReleaseAsync(wanted, ct);
        }

        order.Status = to;

        if (to == OrderStatus.Completed)
        {
            order.CompletedAtUtc = DateTime.UtcNow;
        }

        if (to == OrderStatus.Cancelled)
        {
            order.Notes = AppendLine(order.Notes, $"Cancelled: {request.Reason?.Trim() ?? "no reason given"}");
        }

        await NotifyCounterpartyAsync(
            order,
            party,
            NotificationType.Order,
            $"Order {order.OrderNumber} is now {to}",
            TransitionNarrative(from, to),
            ct);

        await SaveAsync(ct);
        return await ToDetailAsync(order, userId, ct);
    }

    // ---- Helpers ----

    /// <summary>Which side of the trade the caller is on, for authorisation decisions.</summary>
    private async Task<OrderParty> PartyAsync(Order order, Guid userId, CancellationToken ct)
    {
        if (order.BuyerUserId == userId)
        {
            return OrderParty.Buyer;
        }

        return await db.Businesses
            .AsNoTracking()
            .AnyAsync(b => b.Id == order.SellerBusinessId && b.OwnerUserId == userId, ct)
            ? OrderParty.Seller
            : OrderParty.Outsider;
    }

    /// <summary>Party the lifecycle rules require for a given target state, for error messages.</summary>
    private static OrderParty RequiredParty(OrderStatus to) => to switch
    {
        OrderStatus.Placed or OrderStatus.Completed => OrderParty.Buyer,
        _ => OrderParty.Seller,
    };

    private static string TransitionNarrative(OrderStatus from, OrderStatus to) => (from, to) switch
    {
        (OrderStatus.Placed, OrderStatus.Confirmed) => "The seller confirmed the order and reserved stock for dispatch.",
        (OrderStatus.Confirmed, OrderStatus.InTransit) => "The consignment has been dispatched.",
        (OrderStatus.InTransit, OrderStatus.Delivered) => "The consignment has been delivered.",
        (OrderStatus.Delivered, OrderStatus.Completed) => "The buyer confirmed receipt. The order is complete.",
        (_, OrderStatus.Cancelled) => "The order was cancelled.",
        (_, OrderStatus.Placed) => "The buyer placed the order.",
        _ => $"The order moved from {from} to {to}.",
    };

    /// <summary>Appends to a note without discarding what is already there.</summary>
    private static string? AppendLine(string? existing, string line)
    {
        var trimmed = existing?.Trim();
        return string.IsNullOrEmpty(trimmed) ? line : $"{trimmed}\n{line}";
    }

    /// <summary>Tells the other side of the trade, not the caller who made the change.</summary>
    private async Task NotifyCounterpartyAsync(
        Order order,
        OrderParty actor,
        NotificationType type,
        string title,
        string body,
        CancellationToken ct)
    {
        var recipient = actor == OrderParty.Buyer
            ? await SellerOwnerUserIdAsync(order.SellerBusinessId, ct)
            : order.BuyerUserId;

        await NotifyAsync(recipient, type, title, body, $"/dashboard/orders/{order.Id}", ct);
    }

    /// <summary>
    /// Saves, translating a lost rowversion race into a clean 409. Two parties acting on the
    /// same order at once must not surface as a 500.
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
                "ORDER_CHANGED",
                "This order was updated by someone else at the same time. Reload it and try again.");
        }
    }

    private IQueryable<Order> OrdersWithDetail() =>
        db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .Include(o => o.Documents)
            .Include(o => o.SellerBusiness)
            .Include(o => o.TradeRequest);

    private static IQueryable<Order> ApplyStatus(IQueryable<Order> query, string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return query;
        }

        if (!Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var parsed))
        {
            // An unknown status is treated as no match rather than a 400, so a stale filter in
            // the UI shows an empty list instead of an error the user cannot act on.
            return query.Where(_ => false);
        }

        return query.Where(o => o.Status == parsed);
    }

    private static async Task<PagedResult<OrderCardDto>> ToCardsAsync(
        IQueryable<Order> query,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(o => o.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new OrderCardDto
            {
                Id = o.Id,
                OrderNumber = o.OrderNumber,
                Status = o.Status,
                PaymentStatus = o.PaymentStatus,
                Currency = o.Currency,
                Total = o.Total,
                SellerTradingName = o.SellerBusiness!.TradingName,
                Incoterms = o.Incoterms,
                PortOfLoading = o.PortOfLoading,
                PortOfDischarge = o.PortOfDischarge,
                ItemCount = o.Items.Count,
                ExpectedDeliveryUtc = o.ExpectedDeliveryUtc,
                PlacedAtUtc = o.PlacedAtUtc,
                CreatedAtUtc = o.CreatedAtUtc,
            })
            .ToListAsync(ct);

        return new PagedResult<OrderCardDto>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }

    private async Task<OrderDetailDto> ToDetailAsync(Order order, Guid userId, CancellationToken ct)
    {
        var loaded = await OrdersWithDetail()
            .FirstAsync(o => o.Id == order.Id, ct);

        return Map(loaded, isBuyer: loaded.BuyerUserId == userId);
    }

    private static OrderDetailDto Map(Order order, bool isBuyer) => new()
    {
        Id = order.Id,
        OrderNumber = order.OrderNumber,
        QuotationId = order.QuotationId,
        TradeRequestId = order.TradeRequestId,
        RfqNumber = order.TradeRequest?.RfqNumber,
        Status = order.Status,
        PaymentStatus = order.PaymentStatus,
        BuyerUserId = order.BuyerUserId,
        // The buyer is always the caller here, since GetAsync already established that.
        BuyerDisplayName = "You",
        SellerBusinessId = order.SellerBusinessId,
        SellerTradingName = order.SellerBusiness?.TradingName ?? string.Empty,
        Currency = order.Currency,
        Subtotal = order.Subtotal,
        Discount = order.Discount,
        Total = order.Total,
        AmountPaid = order.Payments
            .Where(p => p.Status == PaymentStatus.Paid)
            .Sum(p => p.Amount),
        BalanceDue = order.Total - order.Payments
            .Where(p => p.Status == PaymentStatus.Paid)
            .Sum(p => p.Amount),
        Incoterms = order.Incoterms,
        PortOfLoading = order.PortOfLoading,
        PortOfDischarge = order.PortOfDischarge,
        PaymentTerms = order.PaymentTerms,
        DestinationCountry = order.DestinationCountry,
        TransportMode = order.TransportMode,
        DestinationAddressId = order.DestinationAddressId,
        ExpectedDeliveryUtc = order.ExpectedDeliveryUtc,
        PlacedAtUtc = order.PlacedAtUtc,
        CompletedAtUtc = order.CompletedAtUtc,
        CreatedAtUtc = order.CreatedAtUtc,
        UpdatedAtUtc = order.UpdatedAtUtc,
        Notes = order.Notes,
        Items = order.Items.Select(i => new OrderItemDto
        {
            Id = i.Id,
            ProductId = i.ProductId,
            ProductNameSnapshot = i.ProductNameSnapshot,
            ProductSkuSnapshot = i.ProductSkuSnapshot,
            Quantity = i.Quantity,
            Unit = i.Unit,
            UnitPrice = i.UnitPrice,
            LineTotal = i.LineTotal,
            Currency = i.Currency,
        }).ToList(),
        Payments = order.Payments.Select(p => new PaymentDto
        {
            Id = p.Id,
            OrderId = p.OrderId,
            Reference = p.Reference,
            Amount = p.Amount,
            Currency = p.Currency,
            Provider = p.Provider,
            Method = p.Method,
            Status = p.Status,
            RemittanceReference = p.RemittanceReference,
            BankName = p.BankName,
            ValueDateUtc = p.ValueDateUtc,
            InitiatedAtUtc = p.InitiatedAtUtc,
            PaidAtUtc = p.PaidAtUtc,
            FailureReason = p.FailureReason,
            Notes = p.Notes,
        }).ToList(),
        Documents = order.Documents.Select(d => new TradeDocumentDto
        {
            Id = d.Id,
            OrderId = d.OrderId,
            DocType = d.DocType,
            DocumentReference = d.DocumentReference,
            FileUrl = d.FileUrl,
            FileName = d.FileName,
            FileSizeBytes = d.FileSizeBytes,
            ContentType = d.ContentType,
            Status = d.Status,
            ReviewNotes = d.ReviewNotes,
            ReviewedByUserId = d.ReviewedByUserId,
            ReviewedAtUtc = d.ReviewedAtUtc,
            IssuedOnUtc = d.IssuedOnUtc,
            ExpiresOnUtc = d.ExpiresOnUtc,
            CreatedAtUtc = d.CreatedAtUtc,
        }).ToList(),
    };

    /// <summary>
    /// Per-year sequence, matching the RFQ number scheme. Computed from existing rows rather
    /// than a sequence object; the unique index on OrderNumber is the actual guarantee.
    /// </summary>
    private async Task<string> NextOrderNumberAsync(CancellationToken ct)
    {
        var prefix = $"AG-{DateTime.UtcNow.Year}-";
        var existing = await db.Orders
            .AsNoTracking()
            .Where(o => o.OrderNumber.StartsWith(prefix))
            .Select(o => o.OrderNumber)
            .ToListAsync(ct);

        var next = existing.Count == 0
            ? 1
            : existing
                .Select(n => int.TryParse(n[prefix.Length..], out var v) ? v : 0)
                .DefaultIfEmpty(0)
                .Max() + 1;

        return $"{prefix}{next:D6}";
    }

    private async Task<Guid?> DefaultAddressIdAsync(Guid userId, CancellationToken ct) =>
        await db.Addresses
            .AsNoTracking()
            .Where(a => a.OwnerUserId == userId)
            .OrderByDescending(a => a.IsPrimary)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);

    private async Task<Guid?> SellerOwnerUserIdAsync(Guid businessId, CancellationToken ct) =>
        await db.Businesses
            .AsNoTracking()
            .Where(b => b.Id == businessId)
            .Select(b => (Guid?)b.OwnerUserId)
            .FirstOrDefaultAsync(ct);

    private async Task NotifyAsync(
        Guid? recipientUserId,
        NotificationType type,
        string title,
        string body,
        string link,
        CancellationToken ct)
    {
        if (recipientUserId is null)
        {
            return;
        }

        // Persisted by the caller's SaveChangesAsync, so the notification and the order
        // land together or not at all.
        db.Notifications.Add(new Notification
        {
            UserId = recipientUserId.Value,
            Type = type,
            Title = title,
            Body = body,
            Link = link,
        });
    }
}