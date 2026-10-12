using AgroTrade.Domain.Admin;
using AgroTrade.Domain.Common;
using AgroTrade.Domain.Communication;
using AgroTrade.Domain.Identity;
using AgroTrade.Domain.Trade;
using AgroTrade.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();

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

    // Domestic commerce (Phase 4)
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();

    // Trade requests
    public DbSet<TradeRequest> TradeRequests => Set<TradeRequest>();
    public DbSet<Quotation> Quotations => Set<Quotation>();

    // Trade execution (Phase 2)
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<TradeDocument> TradeDocuments => Set<TradeDocument>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<PaymentWebhookEvent> PaymentWebhookEvents => Set<PaymentWebhookEvent>();

    // Logistics
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentTrackingEvent> ShipmentTrackingEvents => Set<ShipmentTrackingEvent>();

    // Logistics-provider directory + booking (Phase 4)
    public DbSet<LogisticsProvider> LogisticsProviders => Set<LogisticsProvider>();
    public DbSet<LogisticsProviderCountry> LogisticsProviderCountries => Set<LogisticsProviderCountry>();
    public DbSet<LogisticsBooking> LogisticsBookings => Set<LogisticsBooking>();
    public DbSet<LogisticsProviderRating> LogisticsProviderRatings => Set<LogisticsProviderRating>();
    public DbSet<Domain.Intelligence.MarketData> MarketData => Set<Domain.Intelligence.MarketData>();
    public DbSet<Domain.Trust.Review> Reviews => Set<Domain.Trust.Review>();
    public DbSet<Domain.Trade.Dispute> Disputes => Set<Domain.Trade.Dispute>();
    public DbSet<Domain.Trade.Invoice> Invoices => Set<Domain.Trade.Invoice>();
    public DbSet<Domain.Trade.Receipt> Receipts => Set<Domain.Trade.Receipt>();

    // Communication
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Address> Addresses => Set<Address>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        ApplyUtcConventions(builder);

        builder.Entity<ApplicationUser>(e =>
        {
            e.Property(u => u.DisplayName).HasMaxLength(150).IsRequired();
            e.Property(u => u.HomeCountry).HasMaxLength(2);
            e.Property(u => u.DefaultCurrency).HasMaxLength(3).IsRequired();
            e.Property(u => u.BuyerId).HasMaxLength(32);
            e.HasIndex(u => u.NormalizedEmail).IsUnique();
            // Unique only where set: the column is null for accounts that never registered as a
            // buyer (the administrator, the Company supplier). SQL Server's unique index treats
            // NULLs as equal, so without the filter a second null BuyerId would collide with the
            // first and block that account from ever being created.
            e.HasIndex(u => u.BuyerId).IsUnique().HasFilter("[BuyerId] IS NOT NULL");
        });

        builder.Entity<RefreshToken>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
            e.Property(t => t.ReplacedByTokenHash).HasMaxLength(128);
            // Rotation is read-then-write; the version makes a concurrent second rotation fail
            // rather than mint a second live successor for one token.
            e.Property(t => t.RowVersion).IsRowVersion();
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
        ConfigureCarts(builder);
        ConfigureLogisticsDirectory(builder);
        ConfigureCommunication(builder);
    }

    /// <summary>
    /// Marks every timestamp read from the database as UTC.
    /// </summary>
    /// <remarks>
    /// SQL Server's <c>datetime2</c> carries no offset, so EF materialises these as
    /// <see cref="DateTimeKind.Unspecified"/> and the API serialises them without a trailing
    /// <c>Z</c>. A client then reads a bare date-time as *local* time, shifting every displayed
    /// value by the reader's offset — risk R15. Declaring the kind here makes the wire value an
    /// unambiguous UTC instant for every consumer, including a future mobile client. Writes are
    /// untouched: the value is stored exactly as supplied, and the application only ever writes
    /// <see cref="DateTime.UtcNow"/> or something already UTC.
    /// </remarks>
    private static void ApplyUtcConventions(ModelBuilder builder)
    {
        foreach (var property in builder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
        {
            if (property.ClrType == typeof(DateTime))
            {
                property.SetValueConverter(new ValueConverter<DateTime, DateTime>(
                    value => value,
                    value => DateTime.SpecifyKind(value, DateTimeKind.Utc)));
            }
            else if (property.ClrType == typeof(DateTime?))
            {
                property.SetValueConverter(new ValueConverter<DateTime?, DateTime?>(
                    value => value,
                    value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value));
            }
        }
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
            e.Property(p => p.DomesticDeliveryFee).HasPrecision(18, 2);
            e.Property(p => p.PaymentTermsOffered).HasColumnType("nvarchar(max)");
            e.Property(p => p.QualitySpecs).HasColumnType("nvarchar(max)");
            e.Property(p => p.PackagingOptions).HasColumnType("nvarchar(max)");
            e.Property(p => p.Certifications).HasColumnType("nvarchar(max)");
            e.Property(p => p.ModerationReason).HasMaxLength(400);
            e.HasIndex(p => p.Slug).IsUnique();
            e.HasIndex(p => new { p.IsDeleted, p.ListingStatus, p.VerificationStatus });

            // Moderation is filtered on far more often than it is written, and the queue's default
            // view is "suspended only", so it leads the index rather than trailing it.
            e.HasIndex(p => new { p.ModerationStatus, p.CreatedAtUtc });
            e.HasIndex(p => p.CategoryId);

            // Restrict, for the same reason AuditLog.UserId restricts: a suspension is a decision
            // about a seller's revenue, and the record of who made it has to outlive that account.
            // The alternative - letting the row null out on delete - produces a listing that is
            // suspended with nobody accountable for it and no way to find out why.
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(p => p.SuspendedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
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
            e.Property(o => o.DeliveryFee).HasPrecision(18, 2);
            e.Property(o => o.Total).HasPrecision(18, 2);
            e.Property(o => o.PortOfLoading).HasMaxLength(120);
            e.Property(o => o.PortOfDischarge).HasMaxLength(120);
            e.Property(o => o.DestinationCountry).HasMaxLength(2);
            e.Property(o => o.DeliveryLine1).HasMaxLength(200);
            e.Property(o => o.DeliveryLine2).HasMaxLength(200);
            e.Property(o => o.DeliveryCity).HasMaxLength(120);
            e.Property(o => o.DeliveryState).HasMaxLength(120);
            e.Property(o => o.DeliveryPostalCode).HasMaxLength(20);
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
            e.Property(p => p.ProviderRef).HasMaxLength(100);
            e.Property(p => p.IdempotencyKey).HasMaxLength(100);
            // Concurrent webhooks for one charge must not silently overwrite each other; the
            // version makes the later writer lose so it can re-evaluate instead of clobbering.
            e.Property(p => p.RowVersion).IsRowVersion();
            // A gateway names only its own charge id, so this is the column a webhook is
            // matched on. Filtered because a manual transfer has no gateway to name one.
            e.HasIndex(p => new { p.Provider, p.ProviderRef })
                .IsUnique()
                .HasFilter("[ProviderRef] IS NOT NULL");
            // Client-supplied retry guard. Generated when absent, so the filter keeps those
            // rows out of the index rather than forcing every payment to carry a key.
            e.HasIndex(p => p.IdempotencyKey)
                .IsUnique()
                .HasFilter("[IdempotencyKey] IS NOT NULL");
            // The bank's trace id is the one externally-checkable detail on a manual transfer, so
            // the same one appearing twice on an order is a double-count rather than a second
            // payment. Scoped per order rather than globally: trace ids are only unique within a
            // bank, so two different banks can legitimately issue the same string.
            e.HasIndex(p => new { p.OrderId, p.RemittanceReference })
                .IsUnique()
                .HasFilter("[RemittanceReference] IS NOT NULL");
            e.HasOne(p => p.Order)
                .WithMany(o => o.Payments)
                .HasForeignKey(p => p.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PaymentWebhookEvent>(e =>
        {
            e.HasKey(w => w.Id);
            e.Property(w => w.ExternalEventId).HasMaxLength(100).IsRequired();
            e.Property(w => w.EventType).HasMaxLength(80).IsRequired();
            e.Property(w => w.ProviderRef).HasMaxLength(100).IsRequired();
            e.Property(w => w.Signature).HasMaxLength(200);
            e.Property(w => w.Outcome).HasMaxLength(40);
            // Payload is deliberately unlengthed: it is the provider's document, not ours, and
            // truncating it would defeat the point of keeping it.
            e.Property(w => w.Payload).IsRequired();
            // The idempotency guard. A redelivered event fails this insert, and the handler
            // reads that as "already applied" and answers 200 without touching the payment.
            e.HasIndex(w => new { w.Provider, w.ExternalEventId }).IsUnique();
            e.HasIndex(w => new { w.Provider, w.ProviderRef });
            e.HasIndex(w => w.ReceivedAtUtc);
            // No relationship to Payment, on purpose — see the entity's remarks. This is a
            // RESTRICT-free, navigation-free registration so the row outlives its payment.
        });

        builder.Entity<Shipment>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Reference).HasMaxLength(20).IsRequired();
            e.Property(s => s.CarrierName).HasMaxLength(120).IsRequired();
            e.Property(s => s.TrackingReference).HasMaxLength(100);
            e.Property(s => s.OriginCountry).HasMaxLength(2);
            e.Property(s => s.OriginPort).HasMaxLength(120);
            e.Property(s => s.DestinationCountry).HasMaxLength(2);
            e.Property(s => s.DestinationPort).HasMaxLength(120);
            e.Property(s => s.PublicTrackingTokenHash).HasMaxLength(64);
            e.Property(s => s.RowVersion).IsRowVersion();
            e.HasIndex(s => s.Reference).IsUnique();
            // One consignment per order. Splitting a shipment would mean allocating quantities
            // across order items, which the model does not express; see the entity's remarks.
            e.HasIndex(s => s.OrderId).IsUnique();
            // The guest tracking link is looked up by the hash of the presented token, so this
            // index is the whole lookup path for the anonymous endpoint. Filtered because
            // guest tracking is optional and unset shipments must not collide on a NULL.
            e.HasIndex(s => s.PublicTrackingTokenHash)
                .IsUnique()
                .HasFilter("[PublicTrackingTokenHash] IS NOT NULL");
            e.HasIndex(s => new { s.Mode, s.EstimatedArrivalUtc });
            e.HasOne(s => s.Order)
                .WithMany(o => o.Shipments)
                .HasForeignKey(s => s.OrderId)
                // Cascade, like trade documents: a shipment is meaningless without its order and
                // has no independent existence to preserve. Its tracking events cascade with it.
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ShipmentTrackingEvent>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Location).HasMaxLength(120);
            e.Property(t => t.Description).HasMaxLength(500);
            e.HasIndex(t => new { t.ShipmentId, t.OccurredAtUtc });
            e.HasIndex(t => t.Status);
            e.HasOne(t => t.Shipment)
                .WithMany(s => s.Events)
                .HasForeignKey(t => t.ShipmentId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(t => t.RecordedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
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
            // One settlement row per payment. The webhook handler already dedupes, but a
            // ledger that can double-count a payment is worse than one that is merely slow to
            // reconcile, so the constraint is also enforced here where it cannot be bypassed.
            // Filtered to Type = Payment because a refund legitimately shares the PaymentId.
            e.HasIndex(t => t.PaymentId)
                .IsUnique()
                .HasFilter("[PaymentId] IS NOT NULL AND [Type] = 0");
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

    private static void ConfigureCarts(ModelBuilder builder)
    {
        // One cart per user. The cart itself is created lazily on the first add, so reads can
        // assume absence rather than creating an empty row for users who never buy anything.
        builder.Entity<Cart>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.UserId).IsUnique();
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<CartItem>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.Quantity).HasPrecision(18, 2);
            e.HasIndex(i => i.CartId);
            e.HasIndex(i => i.ProductId);
            e.HasOne(i => i.Cart)
                .WithMany(c => c.Items)
                .HasForeignKey(i => i.CartId)
                .OnDelete(DeleteBehavior.Cascade);
            // A cart line is a working list, not history, so a hard-deleted listing takes its
            // lines with it. Listings are soft-deleted in practice (IsDeleted), which leaves
            // the rows in place and the cart read filters them out.
            e.HasOne(i => i.Product)
                .WithMany()
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureLogisticsDirectory(ModelBuilder builder)
    {
        builder.Entity<LogisticsProvider>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.CompanyName).HasMaxLength(200).IsRequired();
            e.Property(p => p.Description).HasMaxLength(2000);
            e.Property(p => p.HomeCountry).HasMaxLength(2).IsRequired();
            e.Property(p => p.ContactEmail).HasMaxLength(200);
            e.Property(p => p.ContactPhone).HasMaxLength(40);
            e.Property(p => p.Website).HasMaxLength(300);
            e.Property(p => p.Rating).HasPrecision(3, 2);
            // One profile per owner, and the service reads it back with FirstOrDefault: the index
            // is what makes "my profile" a single answer rather than a race between two rows.
            e.HasIndex(p => p.OwnerUserId).IsUnique();
            // The directory's default view is active providers, ranked verified first.
            e.HasIndex(p => new { p.IsActive, p.IsVerified });
            e.HasIndex(p => p.HomeCountry);
            e.Ignore(p => p.ModeList);
            e.Ignore(p => p.ServiceList);
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(p => p.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<LogisticsProviderCountry>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Country).HasMaxLength(2).IsRequired();
            e.HasIndex(c => new { c.LogisticsProviderId, c.Country }).IsUnique();
            e.HasIndex(c => c.Country);
            e.HasOne<LogisticsProvider>()
                .WithMany(p => p.Coverage)
                .HasForeignKey(c => c.LogisticsProviderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<LogisticsBooking>(e =>
        {
            e.HasKey(b => b.Id);
            e.Property(b => b.Reference).HasMaxLength(32).IsRequired();
            e.Property(b => b.Notes).HasMaxLength(2000);
            e.Property(b => b.ResponseNotes).HasMaxLength(1000);
            // Money, so it needs an explicit precision: the default for decimal maps to the same
            // truncated column the provider's rating would otherwise get (see MarketData.Price).
            e.Property(b => b.InsuredValue).HasPrecision(18, 2);
            e.HasIndex(b => b.Reference).IsUnique();
            e.HasIndex(b => new { b.OrderId, b.LogisticsProviderId });
            e.HasIndex(b => new { b.RequestedByUserId, b.Status });
            e.HasIndex(b => new { b.LogisticsProviderId, b.Status });
            e.HasOne(b => b.Order)
                .WithMany(o => o.LogisticsBookings)
                .HasForeignKey(b => b.OrderId)
                // Cascade, like shipments: a booking has no meaning without the order it is for.
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(b => b.Provider)
                .WithMany(p => p.Bookings)
                .HasForeignKey(b => b.LogisticsProviderId)
                // Restrict: a provider's booking history is the record of the work they were
                // engaged for, and it must outlive any attempt to remove the provider.
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(b => b.RequestedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<LogisticsProviderRating>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Comment).HasMaxLength(1000);
            // One rating per booking: the booking is what earned the rating, so a second row would
            // be a re-score rather than a second opinion.
            e.HasIndex(r => r.LogisticsBookingId).IsUnique();
            e.HasIndex(r => r.LogisticsProviderId);
            e.HasOne(r => r.Booking)
                .WithOne(b => b.Rating)
                .HasForeignKey<LogisticsProviderRating>(r => r.LogisticsBookingId)
                // Cascade: a rating has no meaning without the booking it scores.
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Provider)
                .WithMany(p => p.Ratings)
                .HasForeignKey(r => r.LogisticsProviderId)
                // Restrict: a provider's reputation is part of its record and must outlive it.
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(r => r.RatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
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

        builder.Entity<AuditLog>(e =>
        {
            e.HasKey(a => a.Id);

            // Text, not an enum, deliberately: a new kind of admin action must be loggable without a
            // migration, and the values are read as strings in the log filter anyway.
            e.Property(a => a.Action).HasMaxLength(80).IsRequired();
            e.Property(a => a.EntityType).HasMaxLength(80).IsRequired();
            e.Property(a => a.Summary).HasMaxLength(400).IsRequired();
            e.Property(a => a.Details).HasMaxLength(1000);

            // Restrict, never Cascade. An audit row has to outlive the account that created it -
            // deleting the user must not erase the record of what that user did, which is the one
            // situation where the log is most needed.
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // The log is read newest-first, filtered by action and by actor.
            e.HasIndex(a => new { a.CreatedAtUtc, a.Id });
            e.HasIndex(a => a.Action);
            e.HasIndex(a => a.UserId);
        });

        builder.Entity<PlatformSetting>(e =>
        {
            // The catalogue key is the natural primary key: at most one row per setting, so a read
            // never has to decide which of several rows is "the" value.
            e.HasKey(s => s.Key);
            e.Property(s => s.Key).HasMaxLength(80).IsRequired();
            e.Property(s => s.Value).HasMaxLength(400).IsRequired();

            // Not a foreign key. The setting, and the fact that an administrator changed it, outlive
            // that account — see PlatformSetting.UpdatedByUserId.
        });

        builder.Entity<Domain.Trust.Review>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Comment).HasMaxLength(1000);
            e.HasIndex(r => new { r.TargetBusinessId, r.IsPublished });
            e.HasIndex(r => new { r.OrderId, r.ReviewerUserId }).IsUnique();
        });

        builder.Entity<Domain.Trade.Dispute>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Details).HasMaxLength(2000);
            e.Property(d => d.Resolution).HasMaxLength(1000);
            e.HasIndex(d => d.OrderId);
            e.HasIndex(d => d.Status);
        });

        builder.Entity<Domain.Trade.Invoice>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.InvoiceNumber).HasMaxLength(40).IsRequired();
            e.Property(i => i.Amount).HasPrecision(18, 2);
            e.Property(i => i.Currency).HasMaxLength(3).IsRequired();
            // One invoice per order, enforced by the database rather than only by the service's
            // pre-check, which two simultaneous requests can both pass. A second invoice would be
            // a second claim on the same money.
            e.HasIndex(i => i.OrderId).IsUnique();
            e.HasIndex(i => i.InvoiceNumber).IsUnique();
        });

        builder.Entity<Domain.Trade.Receipt>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.ReceiptNumber).HasMaxLength(40).IsRequired();
            e.Property(r => r.Amount).HasPrecision(18, 2);
            e.Property(r => r.Currency).HasMaxLength(3).IsRequired();
            // One receipt per settlement. A partial-payment order legitimately has several, so the
            // guard is per payment, not per order; filtered because the column is nullable.
            e.HasIndex(r => r.PaymentId)
                .IsUnique()
                .HasFilter("[PaymentId] IS NOT NULL");
            e.HasIndex(r => r.OrderId);
            e.HasIndex(r => r.ReceiptNumber).IsUnique();
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