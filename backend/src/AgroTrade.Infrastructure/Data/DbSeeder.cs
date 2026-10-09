using System.Text;
using AgroTrade.Application.Common;
using AgroTrade.Domain.Enums;
using AgroTrade.Domain.Trade;
using AgroTrade.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgroTrade.Infrastructure.Data;

/// <summary>
/// Seeds roles (spec §6), the root administrator, the product category tree
/// (spec §4), and idempotent demo corridors — two verified exporters (KE, GH) with
/// live listings plus an importer (NG) with an open RFQ and a quotation (plan §01
/// Phase 1 exit criteria). All idempotent; safe to run on every dev startup.
/// </summary>
public sealed partial class DbSeeder(
    AgroTradeDbContext db,
    RoleManager<IdentityRole<Guid>> roleManager,
    UserManager<ApplicationUser> userManager,
    IConfiguration configuration,
    ILogger<DbSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        if ((await db.Database.GetPendingMigrationsAsync(ct)).Any())
        {
            await db.Database.MigrateAsync(ct);
        }

        foreach (var role in RoleNames.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>
                {
                    Id = Guid.NewGuid(),
                    Name = role,
                    NormalizedName = role.ToUpperInvariant(),
                });
            }
        }

        await SeedAdministratorAsync(ct);
        await SeedCategoriesAsync(ct);
        await SeedDemoCorridorsAsync(ct);
        await SeedCompanyCatalogAsync(ct);
        await SeedMarketDataAsync(ct);
    }

    // ---- Administrator ----

    private async Task SeedAdministratorAsync(CancellationToken ct)
    {
        var email = configuration["Seed:AdminEmail"];
        if (string.IsNullOrWhiteSpace(email))
        {
            logger.LogInformation("Seed:AdminEmail not configured — skipping administrator seed.");
            return;
        }

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var password = configuration["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Seed:AdminPassword must be configured to seed the administrator account. " +
                "Set it via user-secrets (dotnet user-secrets set Seed:AdminPassword ...).");
        }

        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = "Platform Administrator",
            HomeCountry = "NG",
            DefaultCurrency = "NGN",
            IsActive = true,
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow,
        };

        var result = await userManager.CreateAsync(admin, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to seed administrator: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }

        await userManager.AddToRoleAsync(admin, RoleNames.Administrator);
        logger.LogInformation("Seeded administrator: {Email}", email);
    }

    // ---- Categories (spec §4: 12 top-level + commodity children) ----

    private async Task SeedCategoriesAsync(CancellationToken ct)
    {
        (string Name, string Slug, string Icon, (string Name, string Slug)[] Children)[] groups =
        [
            ("Grains & Cereals", "grains-cereals", "🌾", [("Maize", "maize"), ("Rice", "rice"), ("Wheat", "wheat"), ("Sorghum", "sorghum"), ("Millet", "millet")]),
            ("Oilseeds & Pulses", "oilseeds-pulses", "🫘", [("Soybeans", "soybeans"), ("Groundnuts", "groundnuts"), ("Sesame", "sesame"), ("Sunflower", "sunflower"), ("Cowpeas", "cowpeas")]),
            ("Fruits & Vegetables", "fruits-vegetables", "🍅", [("Plantain", "plantain"), ("Mango", "mango"), ("Pineapple", "pineapple"), ("Chillies", "chillies"), ("Tomatoes", "tomatoes")]),
            ("Tree Nuts", "tree-nuts", "🥜", [("Cashew", "cashew"), ("Shea", "shea"), ("Palm Kernels", "palm-kernels")]),
            ("Roots & Tubers", "roots-tubers", "🥔", [("Cassava", "cassava"), ("Yam", "yam"), ("Sweet Potato", "sweet-potato")]),
            ("Spices & Herbs", "spices-herbs", "🌶️", [("Ginger", "ginger"), ("Turmeric", "turmeric"), ("Black Pepper", "black-pepper"), ("Chilli Powder", "chilli-powder")]),
            ("Beverage Crops", "beverage-crops", "☕", [("Coffee", "coffee"), ("Cocoa", "cocoa"), ("Tea", "tea")]),
            ("Livestock & Poultry", "livestock-poultry", "🐄", [("Cattle", "cattle"), ("Sheep & Goats", "sheep-goats"), ("Poultry", "poultry")]),
            ("Fisheries & Aquaculture", "fisheries-aquaculture", "🐟", [("Frozen Fish", "frozen-fish"), ("Tilapia", "tilapia"), ("Catfish", "catfish")]),
            ("Processed Foods", "processed-foods", "🏭", [("Palm Oil", "palm-oil"), ("Shea Butter", "shea-butter"), ("Flour & Meal", "flour-meal"), ("Noodles", "noodles")]),
            ("Fibres & Industrial", "fibres-industrial", "🧶", [("Cotton", "cotton"), ("Sisal", "sisal"), ("Rubber", "rubber")]),
            ("Inputs & Equipment", "inputs-equipment", "🚜", [("Seeds & Seedlings", "seeds-seedlings"), ("Fertilizer", "fertilizer"), ("Farm Machinery", "farm-machinery")]),
        ];

        var existing = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Slug, ct);
        var now = DateTime.UtcNow;

        foreach (var (name, slug, icon, children) in groups)
        {
            var categoryId = existing.TryGetValue(slug, out var found) ? found.Id : Guid.NewGuid();
            var built = new Category
            {
                Id = categoryId,
                ParentId = null,
                Name = name,
                Slug = slug,
                Icon = icon,
                SortOrder = existing.Count + 1,
            };
            if (!existing.ContainsKey(slug))
            {
                db.Categories.Add(built);
            }
            else
            {
                var tracked = await db.Categories.FirstAsync(c => c.Id == categoryId, ct);
                tracked.Name = name;
                tracked.Icon = icon;
            }

            foreach (var (childName, childSlug) in children)
            {
                if (existing.ContainsKey(childSlug))
                {
                    continue;
                }
                db.Categories.Add(new Category
                {
                    Id = Guid.NewGuid(),
                    ParentId = categoryId,
                    Name = childName,
                    Slug = childSlug,
                    SortOrder = 0,
                });
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded category tree.");
    }

    // ---- Demo corridors (Phase 1 exit criteria: 2 corridors, verified exporters) ----

    private async Task SeedDemoCorridorsAsync(CancellationToken ct)
    {
        var cashewId = (await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == "cashew", ct))?.Id;
        var cocoaId = (await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == "cocoa", ct))?.Id;

        const string password = "Demo@12345";

        // ---- Single supplier: the Company (Achievers Global Consult) ----
        // Dedicated supplier account for the marketplace operator; only this role can
        // list products or quote RFQs (see RoleNames.SellerRoles / Policies.Seller).
        var companyOwner = await userManager.FindByEmailAsync("company@achieversglobalconsult.com");
        if (companyOwner is null)
        {
            companyOwner = new ApplicationUser
            {
                UserName = "company@achieversglobalconsult.com",
                Email = "company@achieversglobalconsult.com",
                DisplayName = "Achievers Global Consult",
                HomeCountry = "NG",
                DefaultCurrency = "NGN",
                IsActive = true,
                EmailConfirmed = true,
                CreatedAtUtc = DateTime.UtcNow,
            };
            var companyResult = await userManager.CreateAsync(companyOwner, password);
            if (companyResult.Succeeded)
            {
                await userManager.AddToRoleAsync(companyOwner, RoleNames.Company);
            }
        }

        var company = await db.Businesses.FirstOrDefaultAsync(b => b.TradingName == "Achievers Global Consult", ct);
        if (company is null && companyOwner is not null)
        {
            company = new Business
            {
                OwnerUserId = companyOwner.Id,
                LegalName = "Achievers Global Consult Ltd",
                TradingName = "Achievers Global Consult",
                RegistrationNumber = "NG-RC-2026000001",
                BusinessType = BusinessType.LimitedCompany,
                CategoryFocusId = cashewId ?? cocoaId,
                Website = "https://achieversglobalconsult.com",
                Description = "Lagos-based importer and exporter of premium agro-allied products — cocoa, cashew, palm kernels and more — connecting African origin to world markets.",
                VerificationStatus = VerificationStatus.Verified,
            };
            company.TradeCountries.AddRange(
            [
                new BusinessTradeCountry { Direction = TradeDirection.Export, Country = "NG", IsPrimary = true },
                new BusinessTradeCountry { Direction = TradeDirection.Export, Country = "GH" },
                new BusinessTradeCountry { Direction = TradeDirection.Export, Country = "KE" },
                new BusinessTradeCountry { Direction = TradeDirection.Import, Country = "NL", IsPrimary = true },
                new BusinessTradeCountry { Direction = TradeDirection.Import, Country = "VN" },
            ]);
            company.VerificationHistory.Add(new BusinessVerification
            {
                Status = VerificationStatus.Verified,
                RequestedAtUtc = DateTime.UtcNow.AddDays(-45),
                ReviewedAtUtc = DateTime.UtcNow.AddDays(-43),
                VerifiedLevel = "Basic",
            });
            db.Businesses.Add(company);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded Company account + business (Achievers Global Consult).");
        }

        // ---- Exporter #1: Uzuri Exporters (Kenya) — verified ----
        var uzuriOwner = await userManager.FindByEmailAsync("demo-exporter@agrotrade.local");
        if (uzuriOwner is null)
        {
            uzuriOwner = new ApplicationUser
            {
                UserName = "demo-exporter@agrotrade.local",
                Email = "demo-exporter@agrotrade.local",
                DisplayName = "Grace Wanjiku (Uzuri Exporters)",
                HomeCountry = "KE",
                DefaultCurrency = "USD",
                IsActive = true,
                EmailConfirmed = true,
                CreatedAtUtc = DateTime.UtcNow,
            };
            var result = await userManager.CreateAsync(uzuriOwner, password);
            if (result.Succeeded)
            {
                await userManager.AddToRolesAsync(uzuriOwner, [RoleNames.Farmer, RoleNames.Exporter]);
            }
        }

        var uzuri = await db.Businesses.FirstOrDefaultAsync(b => b.TradingName == "Uzuri Exporters Ltd", ct);
        if (uzuri is null && uzuriOwner is not null)
        {
            uzuri = new Business
            {
                OwnerUserId = uzuriOwner.Id,
                LegalName = "Uzuri Exporters Ltd",
                TradingName = "Uzuri Exporters Ltd",
                RegistrationNumber = "KE-BR-44821",
                BusinessType = BusinessType.LimitedCompany,
                CategoryFocusId = cashewId,
                Website = "https://uzuri-exports.example",
                Description = "Certified East African exporter of cashew and maize from the Coast and Rift Valley regions.",
                VerificationStatus = VerificationStatus.Verified,
                Rating = 0,
                RatingCount = 0,
            };
            uzuri.TradeCountries.AddRange(
            [
                new BusinessTradeCountry { Direction = TradeDirection.Export, Country = "NG", IsPrimary = true },
                new BusinessTradeCountry { Direction = TradeDirection.Export, Country = "KE" },
                new BusinessTradeCountry { Direction = TradeDirection.Export, Country = "GH" },
                new BusinessTradeCountry { Direction = TradeDirection.Export, Country = "DE" },
                new BusinessTradeCountry { Direction = TradeDirection.Import, Country = "CN", IsPrimary = true },
            ]);
            uzuri.VerificationHistory.Add(new BusinessVerification
            {
                Status = VerificationStatus.Verified,
                RequestedAtUtc = DateTime.UtcNow.AddDays(-20),
                ReviewedAtUtc = DateTime.UtcNow.AddDays(-18),
                VerifiedLevel = "Basic",
            });
            db.Businesses.Add(uzuri);
            await db.SaveChangesAsync(ct);
        }

        // ---- Exporter #2: WestGrain Co (Ghana) — verified ----
        var westOwner = await userManager.FindByEmailAsync("demo-exporter2@agrotrade.local");
        if (westOwner is null)
        {
            westOwner = new ApplicationUser
            {
                UserName = "demo-exporter2@agrotrade.local",
                Email = "demo-exporter2@agrotrade.local",
                DisplayName = "Kwame Mensah (WestGrain Co)",
                HomeCountry = "GH",
                DefaultCurrency = "USD",
                IsActive = true,
                EmailConfirmed = true,
                CreatedAtUtc = DateTime.UtcNow,
            };
            var result = await userManager.CreateAsync(westOwner, password);
            if (result.Succeeded)
            {
                await userManager.AddToRolesAsync(westOwner, [RoleNames.Exporter]);
            }
        }

        var westGrain = await db.Businesses.FirstOrDefaultAsync(b => b.TradingName == "WestGrain Co", ct);
        if (westGrain is null && westOwner is not null)
        {
            westGrain = new Business
            {
                OwnerUserId = westOwner.Id,
                LegalName = "WestGrain Co Ltd",
                TradingName = "WestGrain Co",
                RegistrationNumber = "GH-CR-90117",
                BusinessType = BusinessType.LimitedCompany,
                CategoryFocusId = cocoaId,
                Website = "https://westgrain.example",
                Description = "Ghanaian cocoa and oilseed exporter working with farmer cooperatives in Ashanti and Western North.",
                VerificationStatus = VerificationStatus.Verified,
            };
            westGrain.TradeCountries.AddRange(
            [
                new BusinessTradeCountry { Direction = TradeDirection.Export, Country = "NG", IsPrimary = true },
                new BusinessTradeCountry { Direction = TradeDirection.Export, Country = "GH" },
                new BusinessTradeCountry { Direction = TradeDirection.Export, Country = "FR" },
                new BusinessTradeCountry { Direction = TradeDirection.Import, Country = "DE", IsPrimary = true },
            ]);
            westGrain.VerificationHistory.Add(new BusinessVerification
            {
                Status = VerificationStatus.Verified,
                RequestedAtUtc = DateTime.UtcNow.AddDays(-35),
                ReviewedAtUtc = DateTime.UtcNow.AddDays(-33),
                VerifiedLevel = "Basic",
            });
            db.Businesses.Add(westGrain);
            await db.SaveChangesAsync(ct);
        }

        // ---- Importer: Delta Foods (Nigeria) + an open RFQ with a quotation ----
        var deltaOwner = await userManager.FindByEmailAsync("demo-importer@agrotrade.local");
        if (deltaOwner is null)
        {
            deltaOwner = new ApplicationUser
            {
                UserName = "demo-importer@agrotrade.local",
                Email = "demo-importer@agrotrade.local",
                DisplayName = "Chidi Okafor (Delta Foods)",
                HomeCountry = "NG",
                DefaultCurrency = "NGN",
                IsActive = true,
                EmailConfirmed = true,
                CreatedAtUtc = DateTime.UtcNow,
            };
            var result = await userManager.CreateAsync(deltaOwner, password);
            if (result.Succeeded)
            {
                await userManager.AddToRolesAsync(deltaOwner, [RoleNames.Importer]);
            }
        }

        var delta = await db.Businesses.FirstOrDefaultAsync(b => b.TradingName == "Delta Foods Nigeria", ct);
        if (delta is null && deltaOwner is not null)
        {
            delta = new Business
            {
                OwnerUserId = deltaOwner.Id,
                LegalName = "Delta Foods Nigeria Ltd",
                TradingName = "Delta Foods Nigeria",
                RegistrationNumber = "NG-RC-1200441",
                BusinessType = BusinessType.LimitedCompany,
                CategoryFocusId = cashewId,
                Website = "https://deltafoods.example",
                Description = "Lagos-based importer supplying processed cashew and snacks across West Africa.",
                VerificationStatus = VerificationStatus.Unverified,
            };
            delta.TradeCountries.AddRange(
            [
                new BusinessTradeCountry { Direction = TradeDirection.Import, Country = "KE", IsPrimary = true },
                new BusinessTradeCountry { Direction = TradeDirection.Import, Country = "GH" },
            ]);
            db.Businesses.Add(delta);
            await db.SaveChangesAsync(ct);
        }

        // Demo RFQ from the importer, quoted by the Company (corridor KE → NG).
        if (deltaOwner is not null && delta is not null && company is not null)
        {
            var existingRfq = await db.TradeRequests.FirstOrDefaultAsync(r => r.BuyerUserId == deltaOwner.Id, ct);
            if (existingRfq is null)
            {
                var today = DateTime.UtcNow.Date;
                var rfq = new TradeRequest
                {
                    BuyerUserId = deltaOwner.Id,
                    RfqNumber = "RFQ-2026-000001",
                    Title = "Cashew Nuts W320 — 20 MT to Lagos",
                    Description = "Seeking consistent monthly supply of raw cashew W320 for our Lagos processing hub. FCL preferred.",
                    CategoryId = cashewId,
                    Quantity = 20m,
                    Unit = PriceUnit.PerTon,
                    DestinationCountry = "NG",
                    DestinationPortOrCity = "Lagos (LOS)",
                    PreferredIncoterm = Incoterms.Cif,
                    TransportMode = ShipmentMode.Sea,
                    InsuranceRequired = true,
                    CertificationsRequired = JsonLists.ToJsonList(["Phytosanitary", "Certificate of Origin"]),
                    TargetSpecs = JsonLists.ToJsonDict(new Dictionary<string, string>
                    {
                        ["grade"] = "W320",
                        ["moisture"] = "8% max",
                        ["out-turn"] = "50-52 lbs",
                    }),
                    PreferredCurrency = "USD",
                    PaymentTermsPreferred = PaymentTerms.TtPartialDeposit,
                    DeliveryDeadline = today.AddDays(60),
                    BudgetMin = 38000m,
                    BudgetMax = 46000m,
                    Status = RfqStatus.Quoting,
                    ExpiresAt = today.AddDays(21),
                };
                db.TradeRequests.Add(rfq);
                await db.SaveChangesAsync(ct);

                db.Quotations.Add(new Quotation
                {
                    TradeRequestId = rfq.Id,
                    SupplierBusinessId = company.Id,
                    Status = QuotationStatus.Submitted,
                    Currency = "USD",
                    Incoterms = Incoterms.Cif,
                    PortOfLoading = "Mombasa (MBA)",
                    PortOfDischarge = "Lagos (LOS)",
                    LeadTimeDays = 18,
                    PaymentTerms = PaymentTerms.TtPartialDeposit,
                    Total = 43000m,
                    Notes = "Priced CIF Lagos including marine insurance. Fumigation and phytosanitary certification included.",
                    ValidUntil = today.AddDays(14),
                });
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Seeded demo RFQ + quotation (corridor KE -> NG).");
            }
        }

        // ---- Single-supplier pivot: consolidate legacy demo supply under the Company ----
        // Existing databases seeded before the pivot may still list products and quotes
        // under the old demo exporter businesses (Uzuri/WestGrain). Reassign them to the
        // Company business so the marketplace shows exactly one supplier. Idempotent.
        if (company is not null)
        {
            var legacySellerIds = new List<Guid>();
            if (uzuri is not null) legacySellerIds.Add(uzuri.Id);
            if (westGrain is not null) legacySellerIds.Add(westGrain.Id);

            if (legacySellerIds.Count > 0)
            {
                var legacyProducts = await db.Products
                    .Where(p => legacySellerIds.Contains(p.SellerBusinessId))
                    .ToListAsync(ct);
                foreach (var product in legacyProducts)
                {
                    product.SellerBusinessId = company.Id;
                }

                var legacyQuotes = await db.Quotations
                    .Where(q => legacySellerIds.Contains(q.SupplierBusinessId))
                    .ToListAsync(ct);
                foreach (var quote in legacyQuotes)
                {
                    quote.SupplierBusinessId = company.Id;
                }

                if (legacyProducts.Count > 0 || legacyQuotes.Count > 0)
                {
                    await db.SaveChangesAsync(ct);
                    logger.LogInformation(
                        "Reassigned {ProductCount} product(s) and {QuoteCount} quote(s) to the Company business.",
                        legacyProducts.Count, legacyQuotes.Count);
                }
            }
        }
    }

    // ---- Company product catalogue ----

    /// <summary>
    /// The marketplace is single-supplier, so every listing belongs to the Company
    /// business. This catalogue is the single source of truth for the demo range and is
    /// applied on every startup: missing listings are <em>inserted</em>, existing ones are
    /// only backfilled, never overwritten — so edits made through the UI survive a restart.
    /// </summary>
    private async Task SeedCompanyCatalogAsync(CancellationToken ct)
    {
        var company = await db.Businesses.FirstOrDefaultAsync(
            b => b.TradingName == "Achievers Global Consult", ct);
        if (company is null)
        {
            logger.LogInformation("Company business missing — skipping catalogue seed.");
            return;
        }

        var categoryIds = await db.Categories
            .Where(c => c.ParentId != null)
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Slug, c => c.Id, ct);

        // Keyed on name rather than HS code: several grades legitimately share one HS
        // heading (e.g. W180 and W320 cashew are both 0801).
        var existing = await db.Products
            .Where(p => p.SellerBusinessId == company.Id)
            .ToDictionaryAsync(p => p.Name, StringComparer.OrdinalIgnoreCase, ct);

        // Listings that already have a stock row. The Inventory table arrived after these
        // products, so anything missing gets one below rather than staying unorderable.
        var catalogProductIds = existing.Values.Select(p => p.Id).ToList();
        var stocked = (await db.Inventories
                .Where(i => catalogProductIds.Contains(i.ProductId))
                .Select(i => i.ProductId)
                .ToListAsync(ct))
            .ToHashSet();

        var added = 0;
        var stockRowsAdded = 0;

        foreach (var item in CompanyCatalog)
        {
            if (!categoryIds.TryGetValue(item.CategorySlug, out var categoryId))
            {
                logger.LogWarning("Catalogue item '{Name}' skipped — unknown category '{Slug}'.",
                    item.Name, item.CategorySlug);
                continue;
            }

            Product product;

            if (!existing.TryGetValue(item.Name, out var found))
            {
                product = new Product
                {
                    SellerBusinessId = company.Id,
                    CategoryId = categoryId,
                    Name = item.Name,
                    Slug = Slugify(item.Name) + $"-{Guid.NewGuid():N}"[..8],
                    Description = item.Description,
                    HsCode = item.HsCode,
                    OriginCountry = item.Origin,
                    PortOfLoading = item.Port,
                    Price = item.Price,
                    PriceUnit = PriceUnit.PerTon,
                    Currency = "USD",
                    AvailableQuantity = item.Available,
                    MinimumOrderQuantity = item.Moq,
                    Incoterms = item.Incoterm,
                    LeadTimeDays = item.LeadTimeDays,
                    PaymentTermsOffered = JsonLists.ToJsonList(item.PaymentTerms),
                    QualitySpecs = JsonLists.ToJsonDict(new Dictionary<string, string>(item.Specs)),
                    PackagingOptions = JsonLists.ToJsonList(item.Packaging),
                    Certifications = JsonLists.ToJsonList(item.Certifications),
                    ListingStatus = ListingStatus.Active,
                    VerificationStatus = VerificationStatus.Verified,
                };
                db.Products.Add(product);
                added++;
            }
            else
            {
                product = found;

                // Backfill only. Pre-pivot seeds had no description and stored every spec in a
                // single "grade" key, which rendered as one long row on the detail page.
                if (string.IsNullOrWhiteSpace(product.Description))
                {
                    product.Description = item.Description;
                }

                var legacySpecs = JsonLists.FromJsonDict(product.QualitySpecs);
                if (legacySpecs.Count == 1 && legacySpecs.ContainsKey("grade"))
                {
                    product.QualitySpecs = JsonLists.ToJsonDict(new Dictionary<string, string>(item.Specs));
                }
            }

            // Every listing needs a stock row or orders against it fail with STOCK_NOT_SET.
            // Seeded from the level the listing advertises, which for a new product is the
            // catalogue's own figure and for an existing one is whatever it last published.
            if (!stocked.Contains(product.Id))
            {
                db.Inventories.Add(new Inventory
                {
                    ProductId = product.Id,
                    QuantityOnHand = product.AvailableQuantity,
                    QuantityReserved = 0,
                });
                stockRowsAdded++;
            }
        }

        if (added + stockRowsAdded > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation("Company catalogue applied — {Added} new listing(s), {StockRows} new stock row(s), {Total} in catalogue.",
            added, stockRowsAdded, CompanyCatalog.Count);
    }

    /// <summary>One row of the demo range. Prices are indicative USD per metric ton.</summary>
    private sealed record CatalogItem(
        string Name,
        string CategorySlug,
        string HsCode,
        string Origin,
        string Port,
        decimal Price,
        decimal Available,
        decimal Moq,
        Incoterms Incoterm,
        int LeadTimeDays,
        string Description,
        Dictionary<string, string> Specs,
        List<string> PaymentTerms,
        List<string> Packaging,
        List<string> Certifications);

    private static readonly IReadOnlyList<CatalogItem> CompanyCatalog =
    [
        // ---- Cocoa: Ghana -> Europe ----
        new("Cocoa Beans (Fermented)", "cocoa", "1801", "GH", "Tema (TMA)",
            2450m, 60m, 10m, Incoterms.Fob, 12,
            "Sun-dried and well-fermented cocoa beans from Ashanti and Western North farmer cooperatives. "
            + "Cut-test and moisture verified before vessel loading, with full traceability to the harvest window.",
            new() { ["Grade"] = "Well fermented, 80% minimum", ["Moisture"] = "7.5% max", ["Bean count"] = "95-100 per 100 g", ["Total defects"] = "Below 5%", ["Crop year"] = "2025/26 main crop" },
            ["L/C at sight", "T/T 50/50"],
            ["65 kg jute bag", "250 kg bulk bag"],
            ["UTZ Certified", "Phytosanitary", "Fair Trade"]),

        new("Cocoa Butter Block 10 kg", "cocoa", "1804", "GH", "Tema (TMA)",
            4350m, 40m, 5m, Incoterms.Cif, 21,
            "Deodorised cocoa butter pressed from the current season's bean intake, moulded in 10 kg blocks "
            + "and foil-wrapped for industrial chocolate and cosmetics lines.",
            new() { ["Cocoa butter fat"] = "49-52%", ["Free fatty acid"] = "1.75% max", ["Moisture"] = "1% max", ["Block weight"] = "10 kg", ["Shelf life"] = "18 months" },
            ["L/C at sight", "T/T 30/70"],
            ["10 kg aluminium block, shrink-wrapped", "20 kg carton, 2 blocks"],
            ["Food-grade certificate", "Halal"]),

        new("Cocoa Powder (Alkalised)", "cocoa", "1801.32", "GH", "Tema (TMA)",
            3980m, 35m, 5m, Incoterms.Cif, 18,
            "Fully alkalised cocoa powder sieved to 200 mesh for bakeries and beverage manufacturers. "
            + "Bulk and retail-packed quantities available from the Tema bonded warehouse.",
            new() { ["Alkalisation"] = "Dutch process, pH 7.2-7.5", ["Fat content"] = "10-12%", ["Moisture"] = "3.5% max", ["Fineness"] = "200 mesh", ["Colour"] = "Brown to dark brown" },
            ["L/C at sight", "T/T 30/70"],
            ["25 kg kraft bag", "1 MT jumbo bag"],
            ["Food-grade certificate", "ISO 22000"]),

        // ---- Cashew: East Africa + Vietnam ----
        new("Cashew Nuts W320", "cashew", "0801", "KE", "Mombasa (MBA)",
            1800m, 120m, 5m, Incoterms.Fob, 14,
            "Raw cashew kernels graded W320 from Coast and Rift Valley lots, hand-sorted and moisture-controlled "
            + "for processors. The standard benchmark grade for our cashew corridor.",
            new() { ["Grade"] = "W320", ["Moisture"] = "8% max", ["Out-turn"] = "50-52 lbs per 50 kg", ["Colour"] = "Pale cream", ["Total defects"] = "Under 3%" },
            ["T/T 50/50", "L/C at sight"],
            ["50 kg jute bag", "1 MT jumbo bag"],
            ["Phytosanitary", "GlobalG.A.P.", "ISO 22000"]),

        new("Cashew Nuts W180", "cashew", "0801", "GH", "Tema (TMA)",
            2050m, 90m, 5m, Incoterms.Fob, 12,
            "Premium whole cashew kernels, grade W180 — the largest count per pound, packed for snack and "
            + "gift-grade roasters. Full moisture and aflatoxin screening included.",
            new() { ["Grade"] = "W180", ["Moisture"] = "8% max", ["Count"] = "170-180 per lb", ["Colour"] = "Uniform pale cream", ["Aflatoxin"] = "Below 4 ppb" },
            ["T/T 50/50", "L/C at sight"],
            ["25 kg vacuum bag", "1 MT jumbo bag"],
            ["Phytosanitary", "ISO 22000"]),

        new("Roasted Cashew Kernels W240", "cashew", "0801.91", "VN", "Ho Chi Minh (SGN)",
            3450m, 25m, 2m, Incoterms.Cif, 18,
            "Salt-roasted, ready-to-eat cashew kernels in retail and catering packs, produced on our Vietnam "
            + "packing line. Shelf-stable, with nitrogen-flushed options for export.",
            new() { ["Grade"] = "W240 roasted", ["Roast"] = "180°C, low smoke", ["Salting"] = "1.2% sea salt", ["Moisture"] = "2.5% max", ["Shelf life"] = "12 months" },
            ["T/T 30/70", "L/C at sight"],
            ["250 g retail pouch", "5 kg catering pack"],
            ["Halal", "Food-grade certificate"]),

        // ---- Palm kernel: Nigeria ----
        new("Palm Kernel", "palm-kernels", "0803", "NG", "Port Harcourt (PHC)",
            620m, 200m, 25m, Incoterms.Fob, 8,
            "Kernel-grade palm nuts recovered at our Port Harcourt crushing plant, dried and sieved for "
            + "oleochemical and feed clients. Shipment-ready within a week of order confirmation.",
            new() { ["Moisture"] = "8% max", ["Free fatty acid"] = "Below 5%", ["Oil content"] = "48-50%", ["Impurities"] = "1.5% max", ["Screening"] = "Sieved 8-12 mm" },
            ["T/T advance", "L/C at sight"],
            ["50 kg PP bag", "1 MT jumbo bag"],
            ["Phytosanitary", "GlobalG.A.P."]),

        new("Palm Kernel Cake (PKC)", "palm-kernels", "2304", "NG", "Port Harcourt (PHC)",
            185m, 300m, 25m, Incoterms.Fob, 8,
            "Press cake left after kernel oil extraction, pelletised for aquaculture and livestock feed. "
            + "Consistent protein content, with every lot tested at the mill.",
            new() { ["Crude protein"] = "15-18%", ["Moisture"] = "8% max", ["Crude fibre"] = "Below 18%", ["Pellet diameter"] = "6 mm", ["Aflatoxin"] = "Below 20 ppb" },
            ["T/T advance", "L/C at sight"],
            ["40 kg PP bag", "1 MT bulk bag"],
            ["Phytosanitary", "Feed-mill acceptance test"]),

        // ---- Fishery: Nigeria + Ghana -> Asia ----
        new("Frozen Atlantic Mackerel (HLSO)", "frozen-fish", "0303", "NG", "Lagos (LOS)",
            1450m, 150m, 25m, Incoterms.Cif, 16,
            "Frozen whole mackerel, headless and gutted, block-frozen on board within hours of landing. "
            + "Twelve-month shelf life at -18°C for Asian and Middle Eastern retail chains.",
            new() { ["Species"] = "Scomber scombrus", ["Size"] = "300-500 g per piece", ["Freezing"] = "Block frozen at sea", ["Glass ice"] = "65-70%", ["Shelf life"] = "12 months at -18°C" },
            ["L/C at sight", "T/T 50/50"],
            ["20 kg carton block", "40ft reefer container"],
            ["Health certificate", "MSC chain of custody"]),

        new("Frozen Shrimp HLSO 16/20", "frozen-fish", "0306", "GH", "Tema (TMA)",
            5200m, 45m, 5m, Incoterms.Cif, 24,
            "Headless shell-on shrimp, block-frozen at 16/20 count per pound for foodservice and retail. "
            + "Individually quick-frozen and plate-graded for consistent piece weight.",
            new() { ["Count"] = "16/20 per lb", ["Species"] = "Litopenaeus setiferus", ["Freezing"] = "IQF on plate", ["Glazing"] = "10% max", ["Pack"] = "10 kg master carton" },
            ["L/C at sight", "T/T 30/70"],
            ["10 kg master carton", "20ft reefer container"],
            ["Health certificate", "HACCP", "Halal"]),

        // ---- Grains: East Africa + Nigeria ----
        new("White Maize (Grade 1)", "maize", "1005", "KE", "Mombasa (MBA)",
            340m, 800m, 25m, Incoterms.Fob, 10,
            "Dry white maize from Rift Valley cooperatives, cleaned and graded for milling and feed use. "
            + "Moisture and aflatoxin tested per lot before bagging.",
            new() { ["Grade"] = "Grade 1", ["Moisture"] = "13% max", ["Test weight"] = "72 kg/hl", ["Foreign matter"] = "Below 2%", ["Aflatoxin"] = "Below 5 ppb" },
            ["T/T advance", "L/C at sight"],
            ["50 kg PP bag", "1 MT jumbo bag"],
            ["Phytosanitary"]),

        new("Yellow Maize (Grade 2)", "maize", "1005", "NG", "Lagos (LOS)",
            295m, 600m, 25m, Incoterms.Fob, 9,
            "Yellow maize from our own Kano aggregation, suitable for feed mills and starch processing. "
            + "Dried to a stable moisture so it holds through the shipping season.",
            new() { ["Grade"] = "Grade 2", ["Moisture"] = "13.5% max", ["Test weight"] = "68 kg/hl", ["Foreign matter"] = "Below 3%", ["Storage"] = "Aerated flat store" },
            ["T/T advance", "T/T 50/50"],
            ["50 kg PP bag", "Bulk vessel option"],
            ["Phytosanitary"]),

        new("Milled Rice 5% Broken", "rice", "1004", "NG", "Lagos (LOS)",
            620m, 450m, 25m, Incoterms.Cif, 15,
            "Parboiled milled rice polished to 5% broken, packed at our Lagos mill for West African "
            + "distribution. Consistent grain length across the season.",
            new() { ["Broken"] = "5% max", ["Grain length"] = "6.2 mm average", ["Moisture"] = "13% max", ["Parboiled"] = "Yes", ["Packing"] = "50 kg woven PP bag" },
            ["L/C at sight", "T/T 50/50"],
            ["50 kg woven PP bag", "1 MT jumbo bag"],
            ["NAFDAC", "Phytosanitary"]),
    ];

    /// <summary>
    /// Lowercase, hyphen-separated, collapsing runs of separators so "White Maize
    /// (Grade 1)" becomes "white-maize-grade-1" rather than "white-maize--grade-1--".
    /// </summary>
    private static string Slugify(string value)
    {
        var slug = new StringBuilder(value.Length);
        var pendingSeparator = false;

        foreach (var c in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSeparator && slug.Length > 0)
                {
                    slug.Append('-');
                }

                slug.Append(c);
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return slug.ToString();
    }
}