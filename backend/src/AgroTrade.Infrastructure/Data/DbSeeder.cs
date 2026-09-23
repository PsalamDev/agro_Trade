using AgroTrade.Domain.Enums;
using AgroTrade.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgroTrade.Infrastructure.Data;

/// <summary>
/// Seeds roles (spec §6) and the root administrator (plan §02). Admin credentials
/// come from configuration and must never be committed.
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
    }

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
}