using System.Text;
using System.Text.RegularExpressions;
using AgroTrade.Application.Common;
using AgroTrade.Application.Trade;
using AgroTrade.Domain.Enums;
using AgroTrade.Domain.Trade;
using AgroTrade.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Infrastructure.Trade;

/// <summary>
/// Export listings lifecycle + marketplace search with origin/destination/
/// commodity/verified filters and sort (plan §03 — Products).
/// </summary>
public sealed partial class ProductService(
    AgroTradeDbContext db,
    IFileStorage fileStorage,
    IInventoryService inventory) : IProductService
{
    public async Task<PagedResult<ProductCardDto>> SearchAsync(ProductSearchQuery query, CancellationToken ct = default)
    {
        var (page, pageSize) = PageHelper.Normalize(query.Page, query.PageSize);

        IQueryable<Product> baseQuery = db.Products
            .AsNoTracking()
            // Visible means Active AND not suspended. Both conditions, here and in every other
            // read path, so there is exactly one definition of what a buyer can see.
            .Where(p => !p.IsDeleted
                && p.ListingStatus == ListingStatus.Active
                && p.ModerationStatus == ModerationStatus.Cleared)
            .Include(p => p.SellerBusiness!).ThenInclude(b => b.TradeCountries)
            .Include(p => p.Images);

        if (query.VerifiedOnly)
        {
            baseQuery = baseQuery.Where(p => p.VerificationStatus == VerificationStatus.Verified);
        }
        if (query.CategoryId is not null)
        {
            baseQuery = baseQuery.Where(p => p.CategoryId == query.CategoryId.Value);
        }
        if (!string.IsNullOrWhiteSpace(query.HsCode))
        {
            baseQuery = baseQuery.Where(p => p.HsCode == query.HsCode.Trim());
        }
        if (!string.IsNullOrWhiteSpace(query.OriginCountry))
        {
            var country = query.OriginCountry.ToUpperInvariant();
            baseQuery = baseQuery.Where(p => p.OriginCountry == country);
        }
        if (!string.IsNullOrWhiteSpace(query.ExportsToCountry))
        {
            var country = query.ExportsToCountry.ToUpperInvariant();
            baseQuery = baseQuery.Where(p => p.SellerBusiness!.TradeCountries.Any(t =>
                t.Direction == TradeDirection.Export && t.Country == country));
        }
        if (query.Incoterms is not null)
        {
            baseQuery = baseQuery.Where(p => (int?)p.Incoterms == query.Incoterms.Value);
        }
        if (!string.IsNullOrWhiteSpace(query.Currency))
        {
            baseQuery = baseQuery.Where(p => p.Currency == query.Currency.Trim().ToUpperInvariant());
        }
        if (query.MinPrice is not null)
        {
            baseQuery = baseQuery.Where(p => p.Price >= query.MinPrice.Value);
        }
        if (query.MaxPrice is not null)
        {
            baseQuery = baseQuery.Where(p => p.Price <= query.MaxPrice.Value);
        }
        if (query.SellerBusinessId is not null)
        {
            baseQuery = baseQuery.Where(p => p.SellerBusinessId == query.SellerBusinessId.Value);
        }
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var q = query.Q.Trim();
            baseQuery = baseQuery.Where(p => p.Name.Contains(q) || (p.HsCode != null && p.HsCode.Contains(q)));
        }

        var total = await baseQuery.CountAsync(ct);

        var ordered = query.SortBy?.ToLowerInvariant() switch
        {
            "price" => query.SortDir?.ToLowerInvariant() == "asc"
                ? baseQuery.OrderBy(p => p.Price)
                : baseQuery.OrderByDescending(p => p.Price),
            "name" => query.SortDir?.ToLowerInvariant() == "desc"
                ? baseQuery.OrderByDescending(p => p.Name)
                : baseQuery.OrderBy(p => p.Name),
            _ => baseQuery.OrderByDescending(p => p.CreatedAtUtc),
        };

        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<ProductCardDto>
        {
            Items = items.Select(p => ToCard(p)).ToList(),
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }

    public async Task<ProductDetailDto> GetAsync(string idOrSlug, Guid? viewerUserId, CancellationToken ct = default)
    {
        var product = await FindProductAsync(idOrSlug, includeSeller: true, ct);
        if (product is null || product.IsDeleted)
        {
            throw ApiException.NotFound("PRODUCT_NOT_FOUND", "Product not found.");
        }

        var isOwner = viewerUserId is not null && product.SellerBusiness?.OwnerUserId == viewerUserId;
        if (product.ListingStatus != ListingStatus.Active && !isOwner)
        {
            throw ApiException.NotFound("PRODUCT_NOT_FOUND", "Product not found.");
        }

        // A suspended listing is invisible to everyone except the seller it belongs to. The seller
        // keeps access on purpose: they need to see that it is suspended, and why, or they are left
        // guessing why their listing vanished from the marketplace. 404 rather than 403 for
        // everyone else, matching the search path, so a suspended listing cannot be probed for.
        if (product.ModerationStatus == ModerationStatus.Suspended && !isOwner)
        {
            throw ApiException.NotFound("PRODUCT_NOT_FOUND", "Product not found.");
        }

        return await ToDetailAsync(product, ct);
    }

    public async Task<ProductDetailDto> CreateAsync(Guid ownerUserId, string? businessIdOrName, CreateProductRequest request, CancellationToken ct = default)
    {
        var business = await ResolveSellerBusinessAsync(ownerUserId, businessIdOrName, ct)
            ?? throw ApiException.BadRequest("NO_BUSINESS", "Register and complete a business profile first.");

        var wantsActive = request.ListingStatus == (int)ListingStatus.Active;
        if (wantsActive && business.VerificationStatus != VerificationStatus.Verified)
        {
            throw ApiException.Forbidden("BUSINESS_NOT_VERIFIED", "Your business must be verified before listings can go live.");
        }

        var product = new Product
        {
            SellerBusinessId = business.Id,
            CategoryId = request.CategoryId,
            Name = request.Name.Trim(),
            Slug = await GenerateUniqueSlugAsync(request.Name.Trim(), ct),
            HsCode = request.HsCode?.Trim(),
            OriginCountry = request.OriginCountry.ToUpperInvariant(),
            PortOfLoading = request.PortOfLoading?.Trim(),
            Price = request.Price,
            PriceUnit = (PriceUnit)request.PriceUnit,
            Currency = request.Currency.ToUpperInvariant(),
            AvailableQuantity = request.AvailableQuantity,
            MinimumOrderQuantity = request.MinimumOrderQuantity,
            Incoterms = request.Incoterms is null ? null : (Incoterms)request.Incoterms.Value,
            LeadTimeDays = request.LeadTimeDays,
            PaymentTermsOffered = JsonLists.ToJsonList(request.PaymentTermsOffered),
            QualitySpecs = JsonLists.ToJsonDict(request.QualitySpecs),
            PackagingOptions = JsonLists.ToJsonList(request.PackagingOptions),
            Certifications = JsonLists.ToJsonList(request.Certifications),
            ListingStatus = wantsActive ? ListingStatus.Active : ListingStatus.Draft,
            VerificationStatus = business.VerificationStatus,
        };

        db.Products.Add(product);

        // Seed the stock row from the level declared at creation so the listing is orderable
        // straight away. Both rows go in together; Product.AvailableQuantity is the mirror of
        // Inventory.Available, never the source of truth.
        db.Inventories.Add(new Inventory
        {
            ProductId = product.Id,
            QuantityOnHand = request.AvailableQuantity,
            QuantityReserved = 0,
        });

        await db.SaveChangesAsync(ct);

        var created = await FindProductAsync(product.Id.ToString(), includeSeller: true, ct);
        return await ToDetailAsync(created!, ct);
    }

    public async Task<ProductDetailDto> UpdateAsync(Guid id, Guid ownerUserId, UpdateProductRequest request, CancellationToken ct = default)
    {
        var product = await FindProductAsync(id.ToString(), includeSeller: true, ct)
            ?? throw ApiException.NotFound("PRODUCT_NOT_FOUND", "Product not found.");
        EnsureOwner(product, ownerUserId);

        if (product.ListingStatus != ListingStatus.Draft && product.ListingStatus != ListingStatus.Active)
        {
            throw ApiException.Conflict("LISTING_INACTIVE", "Only draft or active listings can be edited.");
        }

        if (request.Name is not null && request.Name.Trim() != product.Name)
        {
            product.Name = request.Name.Trim();
            product.Slug = await GenerateUniqueSlugAsync(product.Name, ct, excludeId: id);
        }
        if (request.Description is not null) product.Description = request.Description.Trim();
        if (request.HsCode is not null) product.HsCode = request.HsCode.Trim();
        if (request.PortOfLoading is not null) product.PortOfLoading = request.PortOfLoading.Trim();
        if (request.Price is not null) product.Price = request.Price.Value;
        if (request.PriceUnit is not null) product.PriceUnit = (PriceUnit)request.PriceUnit.Value;
        if (request.Currency is not null) product.Currency = request.Currency.ToUpperInvariant();
        if (request.MinimumOrderQuantity is not null) product.MinimumOrderQuantity = request.MinimumOrderQuantity.Value;
        if (request.Incoterms is not null) product.Incoterms = (Incoterms)request.Incoterms.Value;
        if (request.LeadTimeDays is not null) product.LeadTimeDays = request.LeadTimeDays.Value;
        if (request.PaymentTermsOffered is not null) product.PaymentTermsOffered = JsonLists.ToJsonList(request.PaymentTermsOffered);
        if (request.QualitySpecs is not null) product.QualitySpecs = JsonLists.ToJsonDict(request.QualitySpecs);
        if (request.PackagingOptions is not null) product.PackagingOptions = JsonLists.ToJsonList(request.PackagingOptions);
        if (request.Certifications is not null) product.Certifications = JsonLists.ToJsonList(request.Certifications);

        if (request.ListingStatus is not null && request.ListingStatus.Value == (int)ListingStatus.Active)
        {
            // Refused while suspended, and this is the whole reason moderation is a separate
            // field rather than a ListingStatus value: without the check a seller undoes an
            // administrator's suspension with a single PATCH, and a control that the subject
            // can switch off is not a control. The reason is echoed back because the seller is
            // entitled to know what they are being held for.
            if (product.ModerationStatus == ModerationStatus.Suspended)
            {
                throw ApiException.Forbidden(
                    "LISTING_SUSPENDED",
                    $"This listing is suspended by a platform administrator and cannot be published. Reason: {product.ModerationReason}");
            }

            var business = await db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == product.SellerBusinessId, ct)
                ?? throw ApiException.NotFound("BUSINESS_NOT_FOUND", "Seller business not found.");
            if (business.VerificationStatus != VerificationStatus.Verified)
            {
                throw ApiException.Forbidden("BUSINESS_NOT_VERIFIED", "Your business must be verified before listings can go live.");
            }
            product.VerificationStatus = VerificationStatus.Verified;
            if (request.ListingStatus.Value != (int)product.ListingStatus)
            {
                product.ListingStatus = ListingStatus.Active;
            }
        }
        else if (request.ListingStatus is not null && request.ListingStatus.Value != (int)product.ListingStatus)
        {
            product.ListingStatus = (ListingStatus)request.ListingStatus.Value;
        }

        if (request.AvailableQuantity is not null)
        {
            // The edit form's stock field is a seller-declared level, not a live availability
            // figure, so it goes through the inventory service: that clamps reservations
            // already held by live orders and re-derives the listing's availability from it.
            // It also flushes the field edits above, so the save below is a no-op.
            await inventory.SetStockAsync(
                product.Id,
                ownerUserId,
                new SetStockRequest { QuantityOnHand = request.AvailableQuantity.Value },
                ct);
        }

        await db.SaveChangesAsync(ct);
        var updated = await FindProductAsync(id.ToString(), includeSeller: true, ct);
        return await ToDetailAsync(updated!, ct);
    }

    public async Task DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct = default)
    {
        var product = await FindProductAsync(id.ToString(), includeSeller: true, ct)
            ?? throw ApiException.NotFound("PRODUCT_NOT_FOUND", "Product not found.");
        EnsureOwner(product, ownerUserId);

        product.IsDeleted = true;
        await db.SaveChangesAsync(ct);
    }

    public async Task<ProductImageDto> UploadImageAsync(Guid id, Guid ownerUserId, string fileName, string contentType, Stream content, bool isPrimary, CancellationToken ct = default)
    {
        var product = await FindProductAsync(id.ToString(), includeSeller: true, ct)
            ?? throw ApiException.NotFound("PRODUCT_NOT_FOUND", "Product not found.");
        EnsureOwner(product, ownerUserId);

        var stored = await fileStorage.SaveAsync(content, fileName, contentType, ct);
        var image = new ProductImage
        {
            ProductId = id,
            Url = stored.Url,
            AltText = Path.GetFileNameWithoutExtension(fileName),
            SortOrder = await db.ProductImages.CountAsync(i => i.ProductId == id, ct),
            IsPrimary = isPrimary,
        };
        if (image.IsPrimary || image.SortOrder == 0)
        {
            await ClearPrimaryAsync(id, ct);
            image.IsPrimary = true;
        }

        db.ProductImages.Add(image);
        await db.SaveChangesAsync(ct);

        return new ProductImageDto
        {
            Id = image.Id,
            Url = image.Url,
            AltText = image.AltText,
            SortOrder = image.SortOrder,
            IsPrimary = image.IsPrimary,
        };
    }

    public async Task DeleteImageAsync(Guid id, Guid imageId, Guid ownerUserId, CancellationToken ct = default)
    {
        var product = await FindProductAsync(id.ToString(), includeSeller: true, ct)
            ?? throw ApiException.NotFound("PRODUCT_NOT_FOUND", "Product not found.");
        EnsureOwner(product, ownerUserId);

        var image = await db.ProductImages.FirstOrDefaultAsync(i => i.Id == imageId && i.ProductId == id, ct)
            ?? throw ApiException.NotFound("IMAGE_NOT_FOUND", "Image not found.");

        var wasPrimary = image.IsPrimary;
        db.ProductImages.Remove(image);
        await fileStorage.DeleteAsync(image.Url, ct);

        if (wasPrimary)
        {
            var next = await db.ProductImages
                .Where(i => i.ProductId == id)
                .OrderBy(i => i.SortOrder)
                .FirstOrDefaultAsync(ct);
            if (next is not null)
            {
                next.IsPrimary = true;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    // ---- internals ----

    private Task<Product?> FindProductAsync(string idOrSlug, bool includeSeller = false, CancellationToken ct = default)
    {
        // Tracked so that Update/Delete mutations persist via SaveChanges.
        IQueryable<Product> query = db.Products
            .Include(p => p.Images);

        if (includeSeller)
        {
            query = query.Include(p => p.SellerBusiness!).ThenInclude(b => b.TradeCountries);
        }

        return Guid.TryParse(idOrSlug, out var id)
            ? query.FirstOrDefaultAsync(p => p.Id == id, ct)
            : query.FirstOrDefaultAsync(p => p.Slug == idOrSlug, ct);
    }

    private async Task<Business?> ResolveSellerBusinessAsync(Guid ownerUserId, string? businessIdOrName, CancellationToken ct)
    {
        IQueryable<Business> businesses = db.Businesses
            .AsNoTracking()
            .Where(b => b.OwnerUserId == ownerUserId && !b.IsDeleted);

        if (!string.IsNullOrWhiteSpace(businessIdOrName))
        {
            var input = businessIdOrName.Trim();
            if (Guid.TryParse(input, out var id))
            {
                return await businesses.FirstOrDefaultAsync(b => b.Id == id, ct);
            }
            return await businesses.FirstOrDefaultAsync(b => b.TradingName == input, ct);
        }

        return await businesses.OrderBy(b => b.CreatedAtUtc).FirstOrDefaultAsync(ct);
    }

    private static void EnsureOwner(Product product, Guid ownerUserId)
    {
        if (product.SellerBusinessId == Guid.Empty || product.SellerBusiness is null)
        {
            // Owner check needs the seller; caller loaded with includeSeller.
            throw ApiException.Forbidden();
        }
        if (product.SellerBusiness.OwnerUserId != ownerUserId)
        {
            throw ApiException.Forbidden();
        }
    }

    private async Task<string> GenerateUniqueSlugAsync(string name, CancellationToken ct, Guid? excludeId = null)
    {
        var baseSlug = Slugify(name);
        var slug = baseSlug;
        var counter = 1;
        while (await db.Products.AnyAsync(p => p.Slug == slug && p.Id != excludeId, ct))
        {
            slug = $"{baseSlug}-{counter++}";
        }
        return slug;
    }

    private static string Slugify(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var c in normalized)
        {
            if (char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        }
        var slug = NonAlphaNumRegex().Replace(builder.ToString(), "-");
        return slug.Trim('-').Length > 0 ? slug.Trim('-') : "listing";
    }

    private static ProductCardDto ToCard(Product p)
    {
        var primary = p.Images.FirstOrDefault(i => i.IsPrimary) ?? p.Images.FirstOrDefault();
        return new ProductCardDto
        {
            Id = p.Id,
            Slug = p.Slug,
            Name = p.Name,
            HsCode = p.HsCode,
            OriginCountry = p.OriginCountry,
            PortOfLoading = p.PortOfLoading,
            Price = p.Price,
            PriceUnit = (int)p.PriceUnit,
            Currency = p.Currency,
            MinimumOrderQuantity = p.MinimumOrderQuantity,
            AvailableQuantity = p.AvailableQuantity,
            Incoterms = p.Incoterms is null ? null : (int)p.Incoterms.Value,
            LeadTimeDays = p.LeadTimeDays,
            ListingStatus = (int)p.ListingStatus,
            VerificationStatus = (int)p.VerificationStatus,
            IsVerified = p.VerificationStatus == VerificationStatus.Verified,
            SellerBusinessId = p.SellerBusinessId,
            SellerTradingName = p.SellerBusiness?.TradingName ?? string.Empty,
            IsSellerVerifiedBadgeVisible = p.SellerBusiness?.VerificationStatus == VerificationStatus.Verified,
            PrimaryImageUrl = primary?.Url,
            CategoryId = p.CategoryId,
            CreatedAtUtc = p.CreatedAtUtc,
        };
    }

    private async Task<ProductDetailDto> ToDetailAsync(Product product, CancellationToken ct)
    {
        var seller = product.SellerBusiness;
        return new ProductDetailDto
        {
            Id = product.Id,
            Slug = product.Slug,
            Name = product.Name,
            Description = product.Description,
            HsCode = product.HsCode,
            OriginCountry = product.OriginCountry,
            PortOfLoading = product.PortOfLoading,
            Price = product.Price,
            PriceUnit = (int)product.PriceUnit,
            Currency = product.Currency,
            MinimumOrderQuantity = product.MinimumOrderQuantity,
            AvailableQuantity = product.AvailableQuantity,
            Incoterms = product.Incoterms is null ? null : (int)product.Incoterms.Value,
            LeadTimeDays = product.LeadTimeDays,
            PaymentTermsOffered = JsonLists.FromJsonList(product.PaymentTermsOffered),
            QualitySpecs = JsonLists.FromJsonDict(product.QualitySpecs),
            PackagingOptions = JsonLists.FromJsonList(product.PackagingOptions),
            Certifications = JsonLists.FromJsonList(product.Certifications),
            ListingStatus = (int)product.ListingStatus,
            VerificationStatus = (int)product.VerificationStatus,
            IsVerified = product.VerificationStatus == VerificationStatus.Verified,
            SellerBusinessId = product.SellerBusinessId,
            SellerTradingName = seller?.TradingName ?? string.Empty,
            IsSellerVerifiedBadgeVisible = seller?.VerificationStatus == VerificationStatus.Verified,
            PrimaryImageUrl = product.Images.FirstOrDefault(i => i.IsPrimary)?.Url ?? product.Images.FirstOrDefault()?.Url,
            CategoryId = product.CategoryId,
            CreatedAtUtc = product.CreatedAtUtc,
            Images = product.Images
                .OrderBy(i => i.SortOrder)
                .Select(i => new ProductImageDto
                {
                    Id = i.Id,
                    Url = i.Url,
                    AltText = i.AltText,
                    SortOrder = i.SortOrder,
                    IsPrimary = i.IsPrimary,
                })
                .ToList(),
            Seller = new SellerCardDto
            {
                BusinessId = seller?.Id ?? Guid.Empty,
                TradingName = seller?.TradingName ?? string.Empty,
                VerificationStatus = seller is null ? (int)VerificationStatus.Unverified : (int)seller.VerificationStatus,
                IsVerifiedBadgeVisible = seller?.VerificationStatus == VerificationStatus.Verified,
                Rating = seller?.Rating ?? 0,
                RatingCount = seller?.RatingCount ?? 0,
            },
        };
    }

    private async Task ClearPrimaryAsync(Guid productId, CancellationToken ct)
    {
        var primary = await db.ProductImages.Where(i => i.ProductId == productId && i.IsPrimary).ToListAsync(ct);
        foreach (var image in primary)
        {
            image.IsPrimary = false;
        }
    }

    [GeneratedRegex("-{2,}")]
    private static partial Regex NonAlphaNumRegex();
}