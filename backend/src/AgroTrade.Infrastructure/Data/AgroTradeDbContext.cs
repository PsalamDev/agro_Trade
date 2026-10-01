using AgroTrade.Domain.Common;
using AgroTrade.Domain.Communication;
using AgroTrade.Domain.Identity;
using AgroTrade.Domain.Trade;
using AgroTrade.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Infrastructure.Data;

/// <summary>
/// Application database: ASP.NET Core Identity (users/roles) + domain aggregates.
/// Conventions: Guid keys, UTC timestamps, decimal money, database-generated RowVersion
/// on concurrency-sensitive aggregates (plan §02).
/// </summary>
public class AgroTradeDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public AgroTradeDbContext(DbContextOptions<AgroTradeDbContext> options)
        : base(options)
    {
    }

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // Business & trust
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<BusinessTradeCountry> BusinessTradeCountries => Set<BusinessTradeCountry>();
    public DbSet<BusinessDocument> BusinessDocuments => Set<BusinessDocument>();
    public DbSet<BusinessVerification> BusinessVerifications => Set<BusinessVerification>();

    // Catalog & products
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<Inventory> Inventories => Set<Inventory>();

    // Trade requests
    public DbSet<TradeRequest> TradeRequests => Set<TradeRequest>();
    public DbSet<Quotation> Quotations => Set<Quotation>();

    // Trade execution (Phase 2)
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<TradeDocument> TradeDocuments => Set<TradeDocument>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    // Communication
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Address> Addresses => Set<Address>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(e =>
        {
            e.Property(u => u.DisplayName).HasMaxLength(150).IsRequired();
            e.Property(u => u.HomeCountry).HasMaxLength(2);
            e.Property(u => u.DefaultCurrency).HasMaxLength(3).IsRequired();
            e.HasIndex(u => u.NormalizedEmail).IsUnique();
        });

        builder.Entity<RefreshToken>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
            e.Property(t => t.ReplacedByTokenHash).HasMaxLength(128);
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => new { t.UserId, t.RevokedAtUtc, t.ExpiresAtUtc });
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        ConfigureBusiness(builder);
        ConfigureCatalog(builder);
        ConfigureTradeRequests(builder);
        ConfigureOrders(builder);
        ConfigureCommunication(builder);
    }

    private static void ConfigureBusiness(ModelBuilder builder)
    {
        builder.Entity<Business>(e =>
        {
            e.HasKey(b => b.Id);
            e.Property(b => b.LegalName).HasMaxLength(250).IsRequired();
            e.Property(b => b.TradingName).HasMaxLength(250).IsRequired();
            e.Property(b => b.RegistrationNumber).HasMaxLength(100);
            e.Property(b => b.Website).HasMaxLength(300);
            e.Property(b => b.Description).HasMaxLength(2000);
            e.Property(b => b.Rating).HasPrecision(3, 2);
            e.Ignore(b => b.IsVerifiedBadgeVisible);
            e.HasIndex(b => b.OwnerUserId);
            e.HasIndex(b => new { b.VerificationStatus, b.IsDeleted, b.IsActive });
            e.HasIndex(b => b.TradingName);
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(b => b.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<BusinessTradeCountry>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Country).HasMaxLength(2).IsRequired();
            e.HasIndex(t => new { t.BusinessId, t.Direction, t.Country }).IsUnique();
            e.HasIndex(t => new { t.Direction, t.Country });
            e.HasOne<Business>()
                .WithMany(b => b.TradeCountries)
                .HasForeignKey(t => t.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<BusinessDocument>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.FileUrl).HasMaxLength(600).IsRequired();
            e.Property(d => d.FileName).HasMaxLength(300).IsRequired();
            e.HasIndex(d => new { d.BusinessId, d.DocType });
            e.HasOne<Business>()
                .WithMany(b => b.Documents)
                .HasForeignKey(d => d.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<BusinessVerification>(e =>
        {
            e.HasKey(v => v.Id);
            e.Property(v => v.Notes).HasMaxLength(2000);
            e.Property(v => v.VerifiedLevel).HasMaxLength(20).IsRequired();
            e.HasIndex(v => new { v.BusinessId, v.RequestedAtUtc });
            e.HasOne<Business>()
                .WithMany(b => b.VerificationHistory)
                .HasForeignKey(v => v.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Address>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Line1).HasMaxLength(200).IsRequired();
            e.Property(a => a.Line2).HasMaxLength(200);
            e.Property(a => a.City).HasMaxLength(120).IsRequired();
            e.Property(a => a.State).HasMaxLength(120);
            e.Property(a => a.Country).HasMaxLength(2).IsRequired();
            e.Property(a => a.PostalCode).HasMaxLength(20);
            e.Property(a => a.Latitude).HasPrecision(9, 6);
            e.Property(a => a.Longitude).HasPrecision(9, 6);
            e.HasIndex(a => new { a.OwnerBusinessId, a.IsPrimary });
            e.HasIndex(a => new { a.OwnerUserId, a.IsPrimary });
        });
    }

    private static void ConfigureCatalog(ModelBuilder builder)
    {
        builder.Entity<Category>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).HasMaxLength(150).IsRequired();
            e.Property(c => c.Slug).HasMaxLength(160).IsRequired();
            e.Property(c => c.Icon).HasMaxLength(50);
            e.HasIndex(c => c.Slug).IsUnique();
            e.HasIndex(c => c.ParentId);
        });

        builder.Entity<Product>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).HasMaxLength(200).IsRequired();
            e.Property(p => p.Slug).HasMaxLength(220).IsRequired();
            e.Property(p => p.HsCode).HasMaxLength(10);
            e.Property(p => p.OriginCountry).HasMaxLength(2).IsRequired();
            e.Property(p => p.PortOfLoading).HasMaxLength(120);
            e.Property(p => p.Price).HasPrecision(18, 2);
            e.Property(p => p.Currency).HasMaxLength(3).IsRequired();
            e.Property(p => p.AvailableQuantity).HasPrecision(18, 2);
            e.Property(p => p.MinimumOrderQuantity).HasPrecision(18, 2);
            e.Property(p => p.PaymentTermsOffered).HasColumnType("nvarchar(max)");
            e.Property(p => p.QualitySpecs).HasColumnType("nvarchar(max)");
            e.Property(p => p.PackagingOptions).HasColumnType("nvarchar(max)");
            e.Property(p => p.Certifications).HasColumnType("nvarchar(max)");
            e.HasIndex(p => p.Slug).IsUnique();
            e.HasIndex(p => new { p.IsDeleted, p.ListingStatus, p.VerificationStatus });
            e.HasIndex(p => p.CategoryId);
            e.HasIndex(p => p.SellerBusinessId);
            e.HasIndex(p => p.OriginCountry);
            e.HasIndex(p => p.HsCode);
            e.HasIndex(p => p.CreatedAtUtc);
            e.HasOne(p => p.SellerBusiness)
                .WithMany(b => b.Products)
                .HasForeignKey(p => p.SellerBusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Category>()
                .WithMany()
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ProductImage>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.Url).HasMaxLength(600).IsRequired();
            e.Property(i => i.AltText).HasMaxLength(200);
            e.HasIndex(i => new { i.ProductId, i.SortOrder });
            e.HasOne<Product>()
                .WithMany(p => p.Images)
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureTradeRequests(ModelBuilder builder)
    {
        builder.Entity<TradeRequest>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.RfqNumber).HasMaxLength(20).IsRequired();
            e.Property(r => r.Title).HasMaxLength(200).IsRequired();
            e.Property(r => r.Description).HasMaxLength(2000);
            e.Property(r => r.DestinationCountry).HasMaxLength(2).IsRequired();
            e.Property(r => r.DestinationPortOrCity).HasMaxLength(120);
            e.Property(r => r.Quantity).HasPrecision(18, 2);
            e.Property(r => r.PreferredCurrency).HasMaxLength(3).IsRequired();
            e.Property(r => r.TargetSpecs).HasColumnType("nvarchar(max)");
            e.Property(r => r.CertificationsRequired).HasColumnType("nvarchar(max)");
            e.Property(r => r.BudgetMin).HasPrecision(18, 2);
            e.Property(r => r.BudgetMax).HasPrecision(18, 2);
            e.HasIndex(r => r.RfqNumber).IsUnique();
            e.HasIndex(r => new { r.Status, r.ExpiresAt });
            e.HasIndex(r => r.BuyerUserId);
            e.HasIndex(r => new { r.DestinationCountry, r.Status });
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(r => r.BuyerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Quotation>(e =>
        {
            e.HasKey(q => q.Id);
            e.Property(q => q.Currency).HasMaxLength(3).IsRequired();
            e.Property(q => q.PortOfLoading).HasMaxLength(120);
            e.Property(q => q.PortOfDischarge).HasMaxLength(120);
            e.Property(q => q.Total).HasPrecision(18, 2);
            e.Property(q => q.Notes).HasMaxLength(2000);
            e.Property(q => q.RowVersion).IsRowVersion();
            e.HasIndex(q => new { q.TradeRequestId, q.SupplierBusinessId }).IsUnique();
            e.HasIndex(q => new { q.Status, q.SupplierBusinessId });
            e.HasOne(q => q.TradeRequest)
                .WithMany(r => r.Quotations)
                .HasForeignKey(q => q.TradeRequestId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(q => q.SupplierBusiness)
                .WithMany()
                .HasForeignKey(q => q.SupplierBusinessId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureOrders(ModelBuilder builder)
    {
        builder.Entity<Order>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.OrderNumber).HasMaxLength(20).IsRequired();
            e.Property(o => o.Currency).HasMaxLength(3).IsRequired();
            e.Property(o => o.Subtotal).HasPrecision(18, 2);
            e.Property(o => o.Discount).HasPrecision(18, 2);
            e.Property(o => o.Total).HasPrecision(18, 2);
            e.Property(o => o.PortOfLoading).HasMaxLength(120);
            e.Property(o => o.PortOfDischarge).HasMaxLength(120);
            e.Property(o => o.DestinationCountry).HasMaxLength(2);
            e.Property(o => o.Notes).HasMaxLength(2000);
            e.Property(o => o.RowVersion).IsRowVersion();
            e.HasIndex(o => o.OrderNumber).IsUnique();
            // A quotation can only be converted once, so this guards against a double handoff.
            e.HasIndex(o => o.QuotationId).IsUnique().HasFilter("[QuotationId] IS NOT NULL");
            e.HasIndex(o => new { o.BuyerUserId, o.Status });
            e.HasIndex(o => new { o.SellerBusinessId, o.Status });
            e.HasIndex(o => o.PlacedAtUtc);
            // Navigation properties are named explicitly. Relying on convention here makes
            // EF pair the nav with the FK twice and create shadow FKs (QuotationId1, etc.),
            // because the entity declares both a nav and a matching scalar.
            e.HasOne(o => o.Quotation)
                .WithMany()
                .HasForeignKey(o => o.QuotationId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.TradeRequest)
                .WithMany()
                .HasForeignKey(o => o.TradeRequestId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(o => o.BuyerUserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.SellerBusiness)
                .WithMany()
                .HasForeignKey(o => o.SellerBusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.DestinationAddress)
                .WithMany()
                .HasForeignKey(o => o.DestinationAddressId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Inventory>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.QuantityOnHand).HasPrecision(18, 2);
            e.Property(i => i.QuantityReserved).HasPrecision(18, 2);
            e.Property(i => i.RowVersion).IsRowVersion();
            // One stock row per listing; the unique ProductId is what makes the
            // reserve-on-order path safe to reason about.
            e.HasIndex(i => i.ProductId).IsUnique();
            e.HasOne(i => i.Product)
                .WithOne(p => p.Inventory)
                .HasForeignKey<Inventory>(i => i.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<OrderItem>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.ProductNameSnapshot).HasMaxLength(200).IsRequired();
            e.Property(i => i.ProductSkuSnapshot).HasMaxLength(64);
            e.Property(i => i.Quantity).HasPrecision(18, 2);
            e.Property(i => i.UnitPrice).HasPrecision(18, 2);
            e.Property(i => i.LineTotal).HasPrecision(18, 2);
            e.Property(i => i.Currency).HasMaxLength(3).IsRequired();
            e.HasIndex(i => i.OrderId);
            e.HasIndex(i => i.ProductId);
            e.HasOne(i => i.Order)
                .WithMany(o => o.Items)
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            // SetNull, not Cascade: a deleted listing must never erase agreed order history.
            e.HasOne(i => i.Product)
                .WithMany()
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TradeDocument>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.DocumentReference).HasMaxLength(100);
            e.Property(d => d.FileUrl).HasMaxLength(600);
            e.Property(d => d.FileName).HasMaxLength(260);
            e.Property(d => d.ContentType).HasMaxLength(120);
            e.Property(d => d.ReviewNotes).HasMaxLength(2000);
            e.HasIndex(d => new { d.OrderId, d.DocType });
            e.HasIndex(d => new { d.Status, d.DocType });
            e.HasIndex(d => d.UploadedByUserId);
            e.HasOne(d => d.Order)
                .WithMany(o => o.Documents)
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(d => d.ReviewedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Payment>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Reference).HasMaxLength(32).IsRequired();
            e.Property(p => p.Amount).HasPrecision(18, 2);
            e.Property(p => p.Currency).HasMaxLength(3).IsRequired();
            e.Property(p => p.RemittanceReference).HasMaxLength(100);
            e.Property(p => p.BankName).HasMaxLength(120);
            e.Property(p => p.FailureReason).HasMaxLength(500);
            e.Property(p => p.Notes).HasMaxLength(1000);
            e.HasIndex(p => p.Reference).IsUnique();
            e.HasIndex(p => new { p.OrderId, p.Status });
            e.HasOne(p => p.Order)
                .WithMany(o => o.Payments)
                .HasForeignKey(p => p.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Transaction>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Reference).HasMaxLength(32).IsRequired();
            e.Property(t => t.Amount).HasPrecision(18, 2);
            e.Property(t => t.Currency).HasMaxLength(3).IsRequired();
            e.Property(t => t.Narrative).HasMaxLength(500);
            e.Property(t => t.ExternalReference).HasMaxLength(100);
            e.HasIndex(t => t.Reference).IsUnique();
            e.HasIndex(t => new { t.OrderId, t.OccurredAtUtc });
            // Restrict, not Cascade: Payments already cascades from Order, and Payments
            // back-references Transactions via SetNull. Cascading from both sides makes
            // Orders -> Payments -> ... -> Transactions -> Orders a cycle, which SQL
            // Server rejects outright (error 1785). A ledger row outliving its order's
            // deletion is also the safer accounting behaviour - delete the order's payments
            // first, or mark the order cancelled instead.
            e.HasOne(t => t.Order)
                .WithMany()
                .HasForeignKey(t => t.OrderId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.Payment)
                .WithMany()
                .HasForeignKey(t => t.PaymentId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureCommunication(ModelBuilder builder)
    {
        builder.Entity<Message>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.ThreadKey).HasMaxLength(128).IsRequired();
            e.Property(m => m.Body).HasMaxLength(4000).IsRequired();
            e.Property(m => m.AttachmentUrl).HasMaxLength(600);
            e.HasIndex(m => new { m.ThreadKey, m.SentAtUtc });
            e.HasIndex(m => new { m.RecipientUserId, m.IsRead });
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(m => m.SenderUserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(m => m.RecipientUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Notification>(e =>
        {
            e.HasKey(n => n.Id);
            e.Property(n => n.Title).HasMaxLength(250).IsRequired();
            e.Property(n => n.Body).HasMaxLength(1000);
            e.Property(n => n.Link).HasMaxLength(500);
            e.HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAtUtc });
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAuditFields();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        StampAuditFields();
        return base.SaveChanges();
    }

    private void StampAuditFields()
    {
        var utcNow = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            switch (entry.State)
            {
                case EntityState.Added when entry.Entity is IAuditableEntity added:
                    added.CreatedAtUtc = utcNow;
                    added.UpdatedAtUtc = utcNow;
                    break;
                case EntityState.Modified when entry.Entity is IAuditableEntity modified:
                    modified.UpdatedAtUtc = utcNow;
                    break;
            }
        }
    }
}