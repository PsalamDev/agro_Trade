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
/// Importer RFQs: publish with destination + trade terms, open directory, exporter
/// quotations with full trade terms, buyer comparison, accept/withdraw, and RFQ
/// negotiation context (plan §03 — RFQs / Trade Requests). Order handoff is Phase 2.
/// </summary>
public sealed class RfqService(AgroTradeDbContext db) : IRfqService
{
    public async Task<TradeRequestDetailDto> CreateAsync(Guid buyerUserId, CreateTradeRequestRequest request, CancellationToken ct = default)
    {
        await ExpireOverdueAsync(ct);

        var rfq = new TradeRequest
        {
            BuyerUserId = buyerUserId,
            RfqNumber = await NextRfqNumberAsync(ct),
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            CategoryId = request.CategoryId,
            Quantity = request.Quantity,
            Unit = (PriceUnit)request.Unit,
            DestinationCountry = request.DestinationCountry.ToUpperInvariant(),
            DestinationPortOrCity = request.DestinationPortOrCity?.Trim(),
            PreferredIncoterm = request.PreferredIncoterm is null ? null : (Incoterms)request.PreferredIncoterm.Value,
            TransportMode = request.TransportMode is null ? null : (ShipmentMode)request.TransportMode.Value,
            InsuranceRequired = request.InsuranceRequired,
            TargetSpecs = JsonLists.ToJsonDict(request.TargetSpecs),
            CertificationsRequired = JsonLists.ToJsonList(request.CertificationsRequired),
            PreferredCurrency = request.PreferredCurrency.ToUpperInvariant(),
            PaymentTermsPreferred = request.PaymentTermsPreferred is null ? null : (PaymentTerms)request.PaymentTermsPreferred.Value,
            DeliveryDeadline = request.DeliveryDeadline,
            BudgetMin = request.BudgetMin,
            BudgetMax = request.BudgetMax,
            ExpiresAt = request.ExpiresAt,
        };

        db.TradeRequests.Add(rfq);
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(rfq, buyerUserId, ct);
    }

    public async Task<PagedResult<TradeRequestCardDto>> GetOpenDirectoryAsync(OpenRfqFilters filters, CancellationToken ct = default)
    {
        await ExpireOverdueAsync(ct);

        var (page, pageSize) = PageHelper.Normalize(filters.Page, filters.PageSize);
        var baseQuery = db.TradeRequests
            .AsNoTracking()
            .Where(r => r.Status == RfqStatus.Open || r.Status == RfqStatus.Quoting || r.Status == RfqStatus.Negotiating);

        if (!string.IsNullOrWhiteSpace(filters.DestinationCountry))
        {
            var country = filters.DestinationCountry.ToUpperInvariant();
            baseQuery = baseQuery.Where(r => r.DestinationCountry == country);
        }
        if (filters.CategoryId is not null)
        {
            baseQuery = baseQuery.Where(r => r.CategoryId == filters.CategoryId.Value);
        }
        if (!string.IsNullOrWhiteSpace(filters.Q))
        {
            var q = filters.Q.Trim();
            baseQuery = baseQuery.Where(r => r.Title.Contains(q));
        }

        var total = await baseQuery.CountAsync(ct);
        var items = await baseQuery
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var quotationCounts = await db.Quotations
            .AsNoTracking()
            .Where(q => items.Select(i => i.Id).Contains(q.TradeRequestId) && q.Status != QuotationStatus.Withdrawn)
            .GroupBy(q => q.TradeRequestId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return new PagedResult<TradeRequestCardDto>
        {
            Items = items.Select(r => ToCard(r, quotationCounts.GetValueOrDefault(r.Id), isMine: false, canQuote: false)).ToList(),
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }

    public async Task<PagedResult<TradeRequestCardDto>> MineAsync(Guid userId, RfqListQuery query, CancellationToken ct = default)
    {
        await ExpireOverdueAsync(ct);

        var (page, pageSize) = PageHelper.Normalize(query.Page, query.PageSize);
        var isBuyerView = string.Equals(query.As, "buyer", StringComparison.OrdinalIgnoreCase);
        var supplierBusinessIds = isBuyerView
            ? []
            : await db.Businesses.AsNoTracking().Where(b => b.OwnerUserId == userId).Select(b => b.Id).ToListAsync(ct);

        var baseQuery = db.TradeRequests
            .AsNoTracking()
            .Where(r =>
                (isBuyerView && r.BuyerUserId == userId) ||
                (!isBuyerView && r.Quotations.Any(q => supplierBusinessIds.Contains(q.SupplierBusinessId))));

        if (query.Status is not null)
        {
            baseQuery = baseQuery.Where(r => (int)r.Status == query.Status.Value);
        }

        var total = await baseQuery.CountAsync(ct);
        var items = await baseQuery
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var quotationCounts = await db.Quotations
            .AsNoTracking()
            .Where(q => items.Select(i => i.Id).Contains(q.TradeRequestId) && q.Status != QuotationStatus.Withdrawn)
            .GroupBy(q => q.TradeRequestId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return new PagedResult<TradeRequestCardDto>
        {
            Items = items
                .Select(r => ToCard(r, quotationCounts.GetValueOrDefault(r.Id), isMine: true, canQuote: !isBuyerView))
                .ToList(),
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }

    public async Task<TradeRequestDetailDto> GetAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        await ExpireOverdueAsync(ct);

        var rfq = await LoadRfqAsync(id, ct)
            ?? throw ApiException.NotFound("RFQ_NOT_FOUND", "RFQ not found.");

        var isBuyer = rfq.BuyerUserId == userId;
        var isSupplier = rfq.Quotations.Any(q => q.Status != QuotationStatus.Withdrawn && q.SupplierBusiness!.OwnerUserId == userId);
        if (!isBuyer && !isSupplier)
        {
            throw ApiException.Forbidden("RFQ_NOT_PARTICIPANT", "Only the buyer and quoting suppliers can view this RFQ.");
        }

        return await ToDetailAsync(rfq, userId, ct);
    }

    // ---- Quotations ----

    public async Task<QuotationDto> CreateQuotationAsync(Guid rfqId, Guid supplierUserId, CreateQuotationRequest request, CancellationToken ct = default)
    {
        await ExpireOverdueAsync(ct);

        var rfq = await LoadRfqAsync(rfqId, ct)
            ?? throw ApiException.NotFound("RFQ_NOT_FOUND", "RFQ not found.");

        if (rfq.Status is RfqStatus.Expired or RfqStatus.Accepted or RfqStatus.ConvertedToOrder or RfqStatus.Closed or RfqStatus.Cancelled)
        {
            throw ApiException.Conflict("QUOTATION_CLOSED", "This RFQ is closed for quotations.");
        }
        if (rfq.ExpiresAt < DateTime.UtcNow)
        {
            rfq.Status = RfqStatus.Expired;
            await db.SaveChangesAsync(ct);
            throw ApiException.Conflict("QUOTATION_CLOSED", "This RFQ has expired.");
        }

        var business = await ResolveSupplierBusinessAsync(supplierUserId, request.BusinessId, ct)
            ?? throw ApiException.Forbidden("BUSINESS_NOT_VERIFIED", "You need a verified business to quote on RFQs.");

        if (await db.Quotations.AnyAsync(q => q.TradeRequestId == rfqId && q.SupplierBusinessId == business.Id, ct))
        {
            throw ApiException.Conflict("QUOTATION_ALREADY_SUBMITTED", "You have already quoted on this RFQ.");
        }

        var quotation = new Quotation
        {
            TradeRequestId = rfqId,
            SupplierBusinessId = business.Id,
            Status = QuotationStatus.Submitted,
            Currency = request.Currency.ToUpperInvariant(),
            Incoterms = (Incoterms)request.Incoterms,
            PortOfLoading = request.PortOfLoading?.Trim(),
            PortOfDischarge = request.PortOfDischarge?.Trim(),
            LeadTimeDays = request.LeadTimeDays,
            PaymentTerms = (PaymentTerms)request.PaymentTerms,
            Total = request.Total,
            Notes = request.Notes?.Trim(),
            ValidUntil = request.ValidUntil,
        };
        db.Quotations.Add(quotation);

        if (rfq.Status == RfqStatus.Open)
        {
            rfq.Status = RfqStatus.Quoting;
        }

        db.Notifications.Add(new Notification
        {
            UserId = rfq.BuyerUserId,
            Type = NotificationType.Quotation,
            Title = "New quotation",
            Body = $"{business.TradingName} quoted on “{rfq.Title}”.",
            Link = $"/rfqs/{rfqId}",
        });

        await db.SaveChangesAsync(ct);
        return ToQuotationDto(quotation, business, isMine: true);
    }

    public async Task<PagedResult<QuotationDto>> GetQuotationsAsync(Guid rfqId, Guid userId, CancellationToken ct = default)
    {
        await ExpireOverdueAsync(ct);

        var rfq = await LoadRfqAsync(rfqId, ct)
            ?? throw ApiException.NotFound("RFQ_NOT_FOUND", "RFQ not found.");

        var isBuyer = rfq.BuyerUserId == userId;
        var isSupplier = rfq.Quotations.Any(q => q.SupplierBusiness!.OwnerUserId == userId);
        if (!isBuyer && !isSupplier)
        {
            throw ApiException.Forbidden();
        }

        var quotations = await db.Quotations
            .AsNoTracking()
            .Include(q => q.SupplierBusiness)
            .Where(q => q.TradeRequestId == rfqId)
            .OrderBy(q => q.Status)
            .ThenByDescending(q => q.CreatedAtUtc)
            .ToListAsync(ct);

        return new PagedResult<QuotationDto>
        {
            Items = quotations
                .Select(q => ToQuotationDto(q, q.SupplierBusiness!, isMine: q.SupplierBusiness!.OwnerUserId == userId))
                .ToList(),
            Page = 1,
            PageSize = quotations.Count,
            Total = quotations.Count,
            TotalPages = 1,
        };
    }

    public async Task<QuotationDto> GetQuotationAsync(Guid quotationId, Guid userId, CancellationToken ct = default)
    {
        var quotation = await db.Quotations
            .Include(q => q.SupplierBusiness)
            .Include(q => q.TradeRequest)
            .FirstOrDefaultAsync(q => q.Id == quotationId, ct)
            ?? throw ApiException.NotFound("QUOTATION_NOT_FOUND", "Quotation not found.");

        var isBuyer = quotation.TradeRequest!.BuyerUserId == userId;
        var isSupplierOwner = quotation.SupplierBusiness!.OwnerUserId == userId;
        if (!isBuyer && !isSupplierOwner)
        {
            throw ApiException.Forbidden();
        }

        return ToQuotationDto(quotation, quotation.SupplierBusiness, isMine: isSupplierOwner);
    }

    public async Task<QuotationDto> AcceptQuotationAsync(Guid quotationId, Guid buyerUserId, CancellationToken ct = default)
    {
        var quotation = await db.Quotations
            .Include(q => q.SupplierBusiness)
            .Include(q => q.TradeRequest)
            .FirstOrDefaultAsync(q => q.Id == quotationId, ct)
            ?? throw ApiException.NotFound("QUOTATION_NOT_FOUND", "Quotation not found.");

        if (quotation.TradeRequest!.BuyerUserId != buyerUserId)
        {
            throw ApiException.Forbidden();
        }
        if (quotation.Status is QuotationStatus.Accepted or QuotationStatus.Withdrawn or QuotationStatus.Declined or QuotationStatus.Expired)
        {
            throw ApiException.Conflict("QUOTATION_NOT_ACCEPTABLE", "This quotation can no longer be accepted.");
        }
        if (quotation.TradeRequest.Status is RfqStatus.Accepted or RfqStatus.ConvertedToOrder or RfqStatus.Cancelled)
        {
            throw ApiException.Conflict("RFQ_CLOSED", "The RFQ is already closed.");
        }

        quotation.Status = QuotationStatus.Accepted;
        quotation.TradeRequest.Status = RfqStatus.Accepted;

        var others = await db.Quotations
            .Where(q => q.TradeRequestId == quotation.TradeRequestId && q.Id != quotationId)
            .ToListAsync(ct);
        foreach (var other in others)
        {
            if (other.Status is QuotationStatus.Submitted or QuotationStatus.UnderReview or QuotationStatus.Negotiating)
            {
                other.Status = QuotationStatus.Declined;
            }
        }

        db.Notifications.Add(new Notification
        {
            UserId = quotation.SupplierBusiness!.OwnerUserId,
            Type = NotificationType.Quotation,
            Title = "Quotation accepted",
            Body = $"Your quotation on “{quotation.TradeRequest.Title}” was accepted.",
            Link = $"/rfqs/{quotation.TradeRequestId}",
        });

        await db.SaveChangesAsync(ct);

        // Phase 2 hands off here: create the Order from the accepted quotation.
        return ToQuotationDto(quotation, quotation.SupplierBusiness, isMine: false);
    }

    public async Task<QuotationDto> WithdrawQuotationAsync(Guid quotationId, Guid supplierUserId, CancellationToken ct = default)
    {
        var quotation = await db.Quotations
            .Include(q => q.SupplierBusiness)
            .Include(q => q.TradeRequest)
            .FirstOrDefaultAsync(q => q.Id == quotationId, ct)
            ?? throw ApiException.NotFound("QUOTATION_NOT_FOUND", "Quotation not found.");

        if (quotation.SupplierBusiness!.OwnerUserId != supplierUserId)
        {
            throw ApiException.Forbidden();
        }
        if (quotation.Status is QuotationStatus.Accepted or QuotationStatus.Declined or QuotationStatus.Expired)
        {
            throw ApiException.Conflict("QUOTATION_FINALIZED", "This quotation can no longer be withdrawn.");
        }

        quotation.Status = QuotationStatus.Withdrawn;

        var remaining = await db.Quotations.CountAsync(q =>
            q.TradeRequestId == quotation.TradeRequestId &&
            (q.Status == QuotationStatus.Submitted || q.Status == QuotationStatus.UnderReview || q.Status == QuotationStatus.Negotiating), ct);
        if (remaining == 0)
        {
            quotation.TradeRequest!.Status = RfqStatus.Open;
        }

        await db.SaveChangesAsync(ct);
        return ToQuotationDto(quotation, quotation.SupplierBusiness, isMine: true);
    }

    // ---- internals ----

    private Task<TradeRequest?> LoadRfqAsync(Guid id, CancellationToken ct) =>
        db.TradeRequests
            .AsNoTracking()
            .Include(r => r.Quotations).ThenInclude(q => q.SupplierBusiness)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    private async Task<Business?> ResolveSupplierBusinessAsync(Guid ownerUserId, Guid? businessId, CancellationToken ct)
    {
        var baseQuery = db.Businesses
            .AsNoTracking()
            .Where(b => b.OwnerUserId == ownerUserId && !b.IsDeleted && b.VerificationStatus == VerificationStatus.Verified);

        if (businessId is not null)
        {
            return await baseQuery.FirstOrDefaultAsync(b => b.Id == businessId.Value, ct);
        }

        return await baseQuery.OrderBy(b => b.CreatedAtUtc).FirstOrDefaultAsync(ct);
    }

    private async Task<string> NextRfqNumberAsync(CancellationToken ct)
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"RFQ-{year}-";
        var max = await db.TradeRequests
            .AsNoTracking()
            .Where(r => r.RfqNumber.StartsWith(prefix))
            .Select(r => r.RfqNumber)
            .ToListAsync(ct);
        var next = max.Count == 0
            ? 1
            : max.Select(n => int.TryParse(n[prefix.Length..], out var v) ? v : 0).DefaultIfEmpty(0).Max() + 1;
        return $"{prefix}{next:D6}";
    }

    private async Task ExpireOverdueAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var overdue = await db.TradeRequests
            .Where(r => r.Status == RfqStatus.Open && r.ExpiresAt < now)
            .ToListAsync(ct);
        if (overdue.Count == 0)
        {
            return;
        }

        foreach (var rfq in overdue)
        {
            rfq.Status = RfqStatus.Expired;
            db.Notifications.Add(new Notification
            {
                UserId = rfq.BuyerUserId,
                Type = NotificationType.Rfq,
                Title = "RFQ expired",
                Body = $"“{rfq.Title}” closed without an accepted quotation.",
                Link = $"/rfqs/{rfq.Id}",
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task<TradeRequestDetailDto> ToDetailAsync(TradeRequest rfq, Guid userId, CancellationToken ct)
    {
        var isBuyer = rfq.BuyerUserId == userId;
        var buyerName = isBuyer
            ? "You"
            : (await db.Users.AsNoTracking().Where(u => u.Id == rfq.BuyerUserId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct) ?? "Buyer");

        var quotations = rfq.Quotations
            .OrderBy(q => q.Status)
            .ThenByDescending(q => q.CreatedAtUtc)
            .Select(q => ToQuotationDto(q, q.SupplierBusiness!, isMine: q.SupplierBusiness!.OwnerUserId == userId))
            .ToList();

        return new TradeRequestDetailDto
        {
            Id = rfq.Id,
            RfqNumber = rfq.RfqNumber,
            Title = rfq.Title,
            Description = rfq.Description,
            CategoryId = rfq.CategoryId,
            Quantity = rfq.Quantity,
            Unit = (int)rfq.Unit,
            DestinationCountry = rfq.DestinationCountry,
            DestinationPortOrCity = rfq.DestinationPortOrCity,
            PreferredIncoterm = rfq.PreferredIncoterm is null ? null : (int)rfq.PreferredIncoterm.Value,
            TransportMode = rfq.TransportMode is null ? null : (int)rfq.TransportMode.Value,
            InsuranceRequired = rfq.InsuranceRequired,
            TargetSpecs = JsonLists.FromJsonDict(rfq.TargetSpecs),
            CertificationsRequired = JsonLists.FromJsonList(rfq.CertificationsRequired),
            PreferredCurrency = rfq.PreferredCurrency,
            PaymentTermsPreferred = rfq.PaymentTermsPreferred is null ? null : (int)rfq.PaymentTermsPreferred.Value,
            DeliveryDeadline = rfq.DeliveryDeadline,
            BudgetMin = rfq.BudgetMin,
            BudgetMax = rfq.BudgetMax,
            Status = (int)rfq.Status,
            ExpiresAt = rfq.ExpiresAt,
            CreatedAtUtc = rfq.CreatedAtUtc,
            QuotationCount = quotations.Count(q => q.Status != (int)QuotationStatus.Withdrawn),
            IsMine = isBuyer,
            IsBuyer = isBuyer,
            CanQuote = false,
            BuyerDisplayName = buyerName,
            Quotations = quotations,
        };
    }

    private static TradeRequestCardDto ToCard(TradeRequest r, int quotationCount, bool isMine, bool canQuote) => new()
    {
        Id = r.Id,
        RfqNumber = r.RfqNumber,
        Title = r.Title,
        CategoryId = r.CategoryId,
        Quantity = r.Quantity,
        Unit = (int)r.Unit,
        DestinationCountry = r.DestinationCountry,
        DestinationPortOrCity = r.DestinationPortOrCity,
        PreferredIncoterm = r.PreferredIncoterm is null ? null : (int)r.PreferredIncoterm.Value,
        TransportMode = r.TransportMode is null ? null : (int)r.TransportMode.Value,
        PreferredCurrency = r.PreferredCurrency,
        Status = (int)r.Status,
        ExpiresAt = r.ExpiresAt,
        CreatedAtUtc = r.CreatedAtUtc,
        QuotationCount = quotationCount,
        CertificationsRequired = JsonLists.FromJsonList(r.CertificationsRequired),
        IsMine = isMine,
        CanQuote = canQuote,
    };

    private static QuotationDto ToQuotationDto(Quotation q, Business supplier, bool isMine) => new()
    {
        Id = q.Id,
        TradeRequestId = q.TradeRequestId,
        SupplierBusinessId = q.SupplierBusinessId,
        SupplierOwnerUserId = supplier.OwnerUserId,
        SupplierTradingName = supplier.TradingName,
        IsSupplierVerifiedBadgeVisible = supplier.VerificationStatus == VerificationStatus.Verified,
        Status = (int)q.Status,
        Currency = q.Currency,
        Incoterms = (int)q.Incoterms,
        PortOfLoading = q.PortOfLoading,
        PortOfDischarge = q.PortOfDischarge,
        LeadTimeDays = q.LeadTimeDays,
        PaymentTerms = (int)q.PaymentTerms,
        Total = q.Total,
        Notes = q.Notes,
        ValidUntil = q.ValidUntil,
        CreatedAtUtc = q.CreatedAtUtc,
        IsMine = isMine,
    };
}