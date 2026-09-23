using AgroTrade.Domain.Common;
using AgroTrade.Domain.Identity;
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