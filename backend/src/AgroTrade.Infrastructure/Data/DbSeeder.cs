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
public sealed class DbSeeder(
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
        var maizeId = (await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == "maize", ct))?.Id;
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

            if (maizeId is not null && cashewId is not null && company is not null)
            {
                await SeedProductAsync(company.Id, cashewId.Value, "Cashew Nuts W320", "0801", "KE", "Mombasa (MBA)",
                    1800m, PriceUnit.PerTon, "USD", 120m, 5m, Incoterms.Fob, 14,
                    ["T/T 50/50", "L/C at sight"],
                    "Grade W320 | moisture 8% max | out-turn 50-52 lbs",
                    ["50 kg jute bag", "1 MT jumbo bag"],
                    ["Phytosanitary", "GlobalG.A.P.", "ISO 22000"], ct);

                await SeedProductAsync(company.Id, maizeId.Value, "White Maize (Grade 1)", "1005", "KE", "Mombasa (MBA)",
                    340m, PriceUnit.PerTon, "USD", 800m, 25m, Incoterms.Fob, 10,
                    ["T/T advance", "L/C at sight"],
                    "Grade 1 | moisture 13% max | test weight 72 kg/hl",
                    ["50 kg PP bag"],
                    ["Phytosanitary"], ct);
            }
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

            if (cocoaId is not null && company is not null)
            {
                await SeedProductAsync(company.Id, cocoaId.Value, "Cocoa Beans (Fermented)", "1801", "GH", "Tema (TMA)",
                    2450m, PriceUnit.PerTon, "USD", 60m, 10m, Incoterms.Fob, 12,
                    ["L/C at sight", "T/T 50/50"],
                    "Well fermented | moisture 7.5% max | bean count 95-100/100g",
                    ["65 kg jute bag"],
                    ["UTZ Certified", "Phytosanitary", "Fair Trade"], ct);
            }
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

    private async Task SeedProductAsync(
        Guid businessId, Guid categoryId, string name, string hsCode, string origin,
        string port, decimal price, PriceUnit unit, string currency, decimal available,
        decimal moq, Incoterms incoterms, int leadTimeDays,
        List<string> paymentTerms, string qualitySpecs, List<string> packaging,
        List<string> certifications, CancellationToken ct)
    {
        if (await db.Products.AnyAsync(p => p.HsCode == hsCode && p.SellerBusinessId == businessId, ct))
        {
            return;
        }

        db.Products.Add(new Product
        {
            SellerBusinessId = businessId,
            CategoryId = categoryId,
            Name = name,
            Slug = Slugify(name) + $"-{Guid.NewGuid():N}"[..8],
            HsCode = hsCode,
            OriginCountry = origin,
            PortOfLoading = port,
            Price = price,
            PriceUnit = unit,
            Currency = currency,
            AvailableQuantity = available,
            MinimumOrderQuantity = moq,
            Incoterms = incoterms,
            LeadTimeDays = leadTimeDays,
            PaymentTermsOffered = JsonLists.ToJsonList(paymentTerms),
            QualitySpecs = JsonLists.ToJsonDict(new Dictionary<string, string> { ["grade"] = qualitySpecs }),
            PackagingOptions = JsonLists.ToJsonList(packaging),
            Certifications = JsonLists.ToJsonList(certifications),
            ListingStatus = ListingStatus.Active,
            VerificationStatus = VerificationStatus.Verified,
        });

        await db.SaveChangesAsync(ct);
    }

    private static string Slugify(string value) =>
        string.Concat(value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-'));
}