using AgroTrade.Domain.Enums;
using AgroTrade.Infrastructure.Data;
using AgroTrade.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgroTrade.Tests.Integration;

/// <summary>
/// API host for integration tests. Opt-in schema provisioning (EnsureSchema) so
/// smoke tests never touch a database while auth-flow tests use local SQL Server.
///
/// Config overrides are applied as environment variables: for minimal-hosting apps,
/// WebApplicationFactory's ConfigureAppConfiguration is merged at lower precedence
/// than appsettings.json and is invisible to Program's top-level reads, whereas env
/// vars are a first-class config source that wins over appsettings.
/// </summary>
public sealed class TestingWebAppFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Point AGROTRADE_TEST_CONNECTION at a disposable SQL Server (CI) or use the
    /// local Trusted_Connection default (dev box).
    /// </summary>
    private static string TestConnection =>
        Environment.GetEnvironmentVariable("AGROTRADE_TEST_CONNECTION")
        ?? "Server=localhost;Database=AgroTrade_Tests;Trusted_Connection=True;TrustServerCertificate=True;";

    public const string AdminEmail = "admin@tests.agrotrade.local";
    public const string AdminPassword = "Admin@12345";

    static TestingWebAppFactory()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", TestConnection);
        Environment.SetEnvironmentVariable("Jwt__Secret", "Test-Secret-Key-0123456789abcdef-ABCDEFGHIJKLMNOPQRSTUVWXYZ");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "AgroTrade.Test");
        Environment.SetEnvironmentVariable("Jwt__Audience", "AgroTradeApi.Test");
        Environment.SetEnvironmentVariable("Auth__AutoConfirmEmail", "true");
        // TestServer has no RemoteIpAddress — everyone shares one rate-limit partition.
        Environment.SetEnvironmentVariable("RateLimit__AuthPermitLimit", "100000");
        // Webhook signatures are HMAC'd with this, so it has to be a known value or the tests
        // cannot produce a body the API will accept. Payments:SigningSecret is validated on
        // start, so it cannot simply be left empty here.
        Environment.SetEnvironmentVariable("Payments__SigningSecret", "test-webhook-signing-secret-0123456789");
        Environment.SetEnvironmentVariable("Serilog__MinimumLevel__Default", "Warning");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }

    /// <summary>
    /// Applies migrations and seeds roles + administrator into the test database.
    /// Idempotent — safe to call once per factory.
    /// </summary>
    public void EnsureSchema()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgroTradeDbContext>();
        db.Database.Migrate();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in RoleNames.All)
        {
            if (!roleManager.RoleExistsAsync(role).GetAwaiter().GetResult())
            {
                roleManager.CreateAsync(new IdentityRole<Guid> { Name = role, NormalizedName = role.ToUpperInvariant() })
                    .GetAwaiter().GetResult();
            }
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (userManager.FindByEmailAsync(AdminEmail).GetAwaiter().GetResult() is null)
        {
            var admin = new ApplicationUser
            {
                UserName = AdminEmail,
                Email = AdminEmail,
                DisplayName = "Test Administrator",
                HomeCountry = "NG",
                DefaultCurrency = "NGN",
                IsActive = true,
                EmailConfirmed = true,
            };
            var created = userManager.CreateAsync(admin, AdminPassword).GetAwaiter().GetResult();
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to seed test admin: {string.Join("; ", created.Errors.Select(e => e.Description))}");
            }
            userManager.AddToRoleAsync(admin, RoleNames.Administrator).GetAwaiter().GetResult();
        }
    }
}