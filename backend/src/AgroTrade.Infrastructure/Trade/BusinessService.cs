using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using AgroTrade.Domain.Communication;
using AgroTrade.Domain.Enums;
using AgroTrade.Domain.Trade;
using AgroTrade.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Infrastructure.Trade;

/// <summary>
/// Business lifecycle: register, update, trade-country reachability (imports-from /
/// exports-to), KYC document upload, verification workflow with badge, and public
/// search (plan §03 — Businesses).
/// </summary>
public sealed class BusinessService(
    AgroTradeDbContext db,
    IFileStorage fileStorage) : IBusinessService
{
    public async Task<BusinessDetailDto> CreateAsync(Guid ownerUserId, RegisterBusinessRequest request, CancellationToken ct = default)
    {
        var business = new Business
        {
            OwnerUserId = ownerUserId,
            LegalName = request.LegalName.Trim(),
            TradingName = string.IsNullOrWhiteSpace(request.TradingName) ? request.LegalName.Trim() : request.TradingName.Trim(),
            RegistrationNumber = request.RegistrationNumber?.Trim(),
            BusinessType = (BusinessType)request.BusinessType,
            CategoryFocusId = request.CategoryFocusId,
            Website = request.Website?.Trim(),
            Description = request.Description?.Trim(),
        };

        db.Businesses.Add(business);
        await db.SaveChangesAsync(ct);
        return await GetAsync(business.Id, ownerUserId, false, ct);
    }

    public async Task<PagedResult<BusinessSummaryDto>> SearchAsync(BusinessSearchQuery query, CancellationToken ct = default)
    {
        var (page, pageSize) = PageHelper.Normalize(query.Page, query.PageSize);
        var q = query.Q?.Trim();

        IQueryable<Business> baseQuery = db.Businesses
            .AsNoTracking()
            .Where(b => !b.IsDeleted && b.IsActive)
            .Include(b => b.TradeCountries);

        if (query.VerifiedOnly)
        {
            baseQuery = baseQuery.Where(b => b.VerificationStatus == VerificationStatus.Verified);
        }
        if (query.Type is not null)
        {
            baseQuery = baseQuery.Where(b => (int)b.BusinessType == query.Type.Value);
        }
        if (!string.IsNullOrWhiteSpace(query.ExportsTo))
        {
            var country = query.ExportsTo.ToUpperInvariant();
            baseQuery = baseQuery.Where(b => b.TradeCountries.Any(t => t.Direction == TradeDirection.Export && t.Country == country));
        }
        if (!string.IsNullOrWhiteSpace(query.ImportsFrom))
        {
            var country = query.ImportsFrom.ToUpperInvariant();
            baseQuery = baseQuery.Where(b => b.TradeCountries.Any(t => t.Direction == TradeDirection.Import && t.Country == country));
        }
        if (!string.IsNullOrWhiteSpace(query.Country))
        {
            var country = query.Country.ToUpperInvariant();
            baseQuery = baseQuery.Where(b => b.TradeCountries.Any(t => t.Country == country));
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            baseQuery = baseQuery.Where(b =>
                b.TradingName.Contains(q) || b.LegalName.Contains(q));
        }

        var total = await baseQuery.CountAsync(ct);
        var items = await baseQuery
            .OrderBy(b => b.TradingName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<BusinessSummaryDto>
        {
            Items = items.Select(b => ToSummary(b, isOwner: false)).ToList(),
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }

    public async Task<BusinessDetailDto> GetAsync(Guid id, Guid? viewerUserId, bool viewerIsAdmin, CancellationToken ct = default)
    {
        var business = await LoadBusinessAsync(id, withDocuments: viewerIsAdmin || viewerUserId is not null, ct);
        if (business is null || business.IsDeleted)
        {
            throw ApiException.NotFound("BUSINESS_NOT_FOUND", "Business not found.");
        }

        var isOwner = viewerUserId == business.OwnerUserId;
        var products = await db.Products
            .AsNoTracking()
            .Where(p => p.SellerBusinessId == id && !p.IsDeleted && (isOwner || p.ListingStatus == ListingStatus.Active))
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);

        return await ToDetailAsync(business, isOwner, viewerIsAdmin, [.. products.Select(p => p.Id)], ct);
    }

    public async Task<BusinessDetailDto> UpdateAsync(Guid id, Guid ownerUserId, UpdateBusinessRequest request, CancellationToken ct = default)
    {
        var business = await LoadBusinessAsync(id, ct: ct)
            ?? throw ApiException.NotFound("BUSINESS_NOT_FOUND", "Business not found.");
        EnsureOwner(business, ownerUserId);

        if (!string.IsNullOrWhiteSpace(request.TradingName)) business.TradingName = request.TradingName.Trim();
        if (request.Website is not null) business.Website = request.Website.Trim();
        if (request.Description is not null) business.Description = request.Description.Trim();

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ownerUserId, false, ct);
    }

    public async Task<BusinessDetailDto> SetTradeCountriesAsync(Guid id, Guid ownerUserId, TradeCountriesRequest request, CancellationToken ct = default)
    {
        var business = await LoadBusinessAsync(id, ct: ct)
            ?? throw ApiException.NotFound("BUSINESS_NOT_FOUND", "Business not found.");
        EnsureOwner(business, ownerUserId);

        var existing = await db.BusinessTradeCountries
            .Where(t => t.BusinessId == id)
            .ToListAsync(ct);
        db.BusinessTradeCountries.RemoveRange(existing);

        var next = new List<BusinessTradeCountry>();
        next.AddRange(request.ExportsTo.Distinct().Select((c, i) => new BusinessTradeCountry
        {
            BusinessId = id,
            Direction = TradeDirection.Export,
            Country = c.ToUpperInvariant(),
            IsPrimary = i == 0,
        }));
        next.AddRange(request.ImportsFrom.Distinct().Select((c, i) => new BusinessTradeCountry
        {
            BusinessId = id,
            Direction = TradeDirection.Import,
            Country = c.ToUpperInvariant(),
            IsPrimary = i == 0,
        }));
        db.BusinessTradeCountries.AddRange(next);

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ownerUserId, false, ct);
    }

    public async Task<IReadOnlyList<BusinessSummaryDto>> MineAsync(Guid ownerUserId, CancellationToken ct = default)
    {
        var businesses = await db.Businesses
            .AsNoTracking()
            .Where(b => b.OwnerUserId == ownerUserId && !b.IsDeleted)
            .Include(b => b.TradeCountries)
            .OrderBy(b => b.TradingName)
            .ToListAsync(ct);
        return businesses.Select(b => ToSummary(b, isOwner: true)).ToList();
    }

    // ---- Documents ----

    public async Task<BusinessDocumentDto> UploadDocumentAsync(
        Guid id, Guid ownerUserId, int docType, string fileName, string contentType,
        Stream content, CancellationToken ct = default)
    {
        var business = await LoadBusinessAsync(id, ct: ct)
            ?? throw ApiException.NotFound("BUSINESS_NOT_FOUND", "Business not found.");
        EnsureOwner(business, ownerUserId);

        var type = (DocumentType)docType;
        var stored = await fileStorage.SaveAsync(content, fileName, contentType, ct);

        // One active copy per (BusinessId, DocType): retire the previous one.
        var previous = await db.BusinessDocuments
            .Where(d => d.BusinessId == id && d.DocType == type)
            .ToListAsync(ct);
        foreach (var doc in previous)
        {
            await fileStorage.DeleteAsync(doc.FileUrl, ct);
        }
        db.BusinessDocuments.RemoveRange(previous);

        var document = new BusinessDocument
        {
            BusinessId = id,
            DocType = type,
            FileName = fileName,
            FileUrl = stored.Url,
            UploadedAtUtc = DateTime.UtcNow,
        };
        db.BusinessDocuments.Add(document);
        await db.SaveChangesAsync(ct);

        return ToDocumentDto(document);
    }

    public async Task<IReadOnlyList<BusinessDocumentDto>> GetDocumentsAsync(Guid id, Guid viewerUserId, bool isAdmin, CancellationToken ct = default)
    {
        var business = await LoadBusinessAsync(id, ct: ct)
            ?? throw ApiException.NotFound("BUSINESS_NOT_FOUND", "Business not found.");
        if (!isAdmin && business.OwnerUserId != viewerUserId)
        {
            throw ApiException.Forbidden();
        }

        var documents = await db.BusinessDocuments
            .AsNoTracking()
            .Where(d => d.BusinessId == id)
            .OrderByDescending(d => d.UploadedAtUtc)
            .ToListAsync(ct);
        return documents.Select(ToDocumentDto).ToList();
    }

    // ---- Verification ----

    public async Task<VerificationStateDto> SubmitForVerificationAsync(Guid id, Guid ownerUserId, CancellationToken ct = default)
    {
        var business = await LoadBusinessAsync(id, ct: ct)
            ?? throw ApiException.NotFound("BUSINESS_NOT_FOUND", "Business not found.");
        EnsureOwner(business, ownerUserId);

        if (business.VerificationStatus == VerificationStatus.Pending)
        {
            return await GetVerificationAsync(id, ownerUserId, false, ct);
        }
        if (business.VerificationStatus == VerificationStatus.Verified)
        {
            throw ApiException.Conflict("ALREADY_VERIFIED", "This business is already verified.");
        }

        var hasDocuments = await db.BusinessDocuments.AnyAsync(d => d.BusinessId == id, ct);
        if (!hasDocuments)
        {
            throw ApiException.BadRequest("NO_DOCUMENTS", "Upload at least one business document before submitting for verification.");
        }

        business.VerificationStatus = VerificationStatus.Pending;
        db.BusinessVerifications.Add(new BusinessVerification
        {
            BusinessId = id,
            Status = VerificationStatus.Pending,
            RequestedAtUtc = DateTime.UtcNow,
            VerifiedLevel = "Basic",
        });
        await db.SaveChangesAsync(ct);

        return await GetVerificationAsync(id, ownerUserId, false, ct);
    }

    public async Task<VerificationStateDto> GetVerificationAsync(Guid id, Guid viewerUserId, bool isAdmin, CancellationToken ct = default)
    {
        var business = await LoadBusinessAsync(id, ct: ct)
            ?? throw ApiException.NotFound("BUSINESS_NOT_FOUND", "Business not found.");
        if (!isAdmin && business.OwnerUserId != viewerUserId)
        {
            throw ApiException.Forbidden();
        }

        return await ToVerificationStateAsync(business, ct);
    }

    public async Task<BusinessDetailDto> ReviewVerificationAsync(Guid id, Guid adminUserId, ReviewVerificationRequest request, CancellationToken ct = default)
    {
        var business = await LoadBusinessAsync(id, ct: ct)
            ?? throw ApiException.NotFound("BUSINESS_NOT_FOUND", "Business not found.");

        if (business.VerificationStatus != VerificationStatus.Pending)
        {
            throw ApiException.Conflict("NOT_PENDING", "Only businesses with a pending verification can be reviewed.");
        }

        var targetStatus = request.Approve ? VerificationStatus.Verified : VerificationStatus.Rejected;
        business.VerificationStatus = targetStatus;
        db.BusinessVerifications.Add(new BusinessVerification
        {
            BusinessId = id,
            Status = targetStatus,
            RequestedAtUtc = DateTime.UtcNow,
            ReviewedAtUtc = DateTime.UtcNow,
            ReviewedByUserId = adminUserId,
            Notes = request.Notes?.Trim(),
            VerifiedLevel = "Basic",
        });

        db.Notifications.Add(new Notification
        {
            UserId = business.OwnerUserId,
            Type = NotificationType.Verification,
            Title = request.Approve ? "Business verified" : "Verification update",
            Body = request.Approve
                ? $"{business.TradingName} is now verified and shows the Verified badge across the marketplace."
                : $"{business.TradingName} verification was rejected{(string.IsNullOrWhiteSpace(request.Notes) ? "." : $": {request.Notes.Trim()}")}",
            Link = $"/businesses/{business.Id}",
        });

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, business.OwnerUserId, false, ct);
    }

    public async Task<PagedResult<BusinessSummaryDto>> AdminSearchAsync(AdminBusinessSearchQuery query, CancellationToken ct = default)
    {
        var (page, pageSize) = PageHelper.Normalize(query.Page, query.PageSize);
        IQueryable<Business> baseQuery = db.Businesses
            .AsNoTracking()
            .Where(b => !b.IsDeleted)
            .Include(b => b.TradeCountries);

        if (query.Status is not null)
        {
            baseQuery = baseQuery.Where(b => (int)b.VerificationStatus == query.Status.Value);
        }
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            baseQuery = baseQuery.Where(b => b.TradingName.Contains(query.Q) || b.LegalName.Contains(query.Q));
        }

        var total = await baseQuery.CountAsync(ct);
        var items = await baseQuery
            .OrderBy(b => b.VerificationStatus)
            .ThenBy(b => b.TradingName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<BusinessSummaryDto>
        {
            Items = items.Select(b => ToSummary(b, isOwner: false)).ToList(),
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }

    // ---- internals ----

    private Task<Business?> LoadBusinessAsync(Guid id, bool withDocuments = false, CancellationToken ct = default)
    {
        IQueryable<Business> query = db.Businesses
            .Include(b => b.TradeCountries)
            .Include(b => b.VerificationHistory.OrderByDescending(v => v.RequestedAtUtc));
        if (withDocuments)
        {
            query = query.Include(b => b.Documents);
        }
        return query.FirstOrDefaultAsync(b => b.Id == id, ct);
    }

    private static void EnsureOwner(Business business, Guid ownerUserId)
    {
        if (business.OwnerUserId != ownerUserId)
        {
            throw ApiException.Forbidden();
        }
    }

    private static BusinessSummaryDto ToSummary(Business b, bool isOwner) => new()
    {
        Id = b.Id,
        TradingName = b.TradingName,
        LegalName = b.LegalName,
        BusinessType = (int)b.BusinessType,
        VerificationStatus = (int)b.VerificationStatus,
        IsVerifiedBadgeVisible = b.VerificationStatus == VerificationStatus.Verified,
        Rating = b.Rating,
        RatingCount = b.RatingCount,
        CategoryFocusId = b.CategoryFocusId,
        ExportsTo = b.TradeCountries.Where(t => t.Direction == TradeDirection.Export).Select(t => t.Country).ToList(),
        ImportsFrom = b.TradeCountries.Where(t => t.Direction == TradeDirection.Import).Select(t => t.Country).ToList(),
        IsOwner = isOwner,
    };

    private async Task<BusinessDetailDto> ToDetailAsync(Business business, bool isOwner, bool isAdmin, List<Guid> productIds, CancellationToken ct)
    {
        var verification = isOwner || isAdmin ? await ToVerificationStateAsync(business, ct) : null;
        var detail = new BusinessDetailDto
        {
            Id = business.Id,
            OwnerUserId = business.OwnerUserId,
            TradingName = business.TradingName,
            LegalName = business.LegalName,
            BusinessType = (int)business.BusinessType,
            VerificationStatus = (int)business.VerificationStatus,
            IsVerifiedBadgeVisible = business.VerificationStatus == VerificationStatus.Verified,
            Rating = business.Rating,
            RatingCount = business.RatingCount,
            CategoryFocusId = business.CategoryFocusId,
            Website = business.Website,
            Description = business.Description,
            ExportsTo = business.TradeCountries.Where(t => t.Direction == TradeDirection.Export).Select(t => t.Country).ToList(),
            ImportsFrom = business.TradeCountries.Where(t => t.Direction == TradeDirection.Import).Select(t => t.Country).ToList(),
            IsOwner = isOwner,
            ProductIds = productIds,
            Documents = business.Documents.Select(ToDocumentDto).ToList(),
            Verification = verification,
        };

        return detail;
    }

    private async Task<VerificationStateDto> ToVerificationStateAsync(Business business, CancellationToken ct)
    {
        var latest = business.VerificationHistory.FirstOrDefault();
        return new VerificationStateDto
        {
            Status = (int)business.VerificationStatus,
            IsBadgeVisible = business.VerificationStatus == VerificationStatus.Verified,
            VerifiedLevel = latest?.VerifiedLevel ?? "Basic",
            RequestedAtUtc = latest?.RequestedAtUtc,
            ReviewedAtUtc = latest?.ReviewedAtUtc,
            Notes = latest?.Notes,
            History = business.VerificationHistory.Select(v => new VerificationHistoryDto
            {
                Status = (int)v.Status,
                RequestedAtUtc = v.RequestedAtUtc,
                ReviewedAtUtc = v.ReviewedAtUtc,
                Notes = v.Notes,
            }).ToList(),
        };
    }

    private static BusinessDocumentDto ToDocumentDto(BusinessDocument d) => new()
    {
        Id = d.Id,
        DocType = (int)d.DocType,
        FileName = d.FileName,
        FileUrl = d.FileUrl,
        UploadedAtUtc = d.UploadedAtUtc,
    };
}