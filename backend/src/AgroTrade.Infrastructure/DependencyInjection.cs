using AgroTrade.Application.Auth;
using AgroTrade.Application.Common;
using AgroTrade.Application.Communication;
using AgroTrade.Application.Trade;
using AgroTrade.Infrastructure.Auth;
using AgroTrade.Infrastructure.Communication;
using AgroTrade.Infrastructure.Data;
using AgroTrade.Infrastructure.Email;
using AgroTrade.Infrastructure.Identity;
using AgroTrade.Infrastructure.Storage;
using AgroTrade.Infrastructure.Trade;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgroTrade.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Connection string 'Default' is missing. Set it with: " +
                "dotnet user-secrets set \"ConnectionStrings:Default\" \"Server=localhost;Database=AgroTrade;Trusted_Connection=True;TrustServerCertificate=True;\" --project src/AgroTrade.Api");

        services.AddDbContext<AgroTradeDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(AgroTradeDbContext).Assembly.FullName);
                sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });
        });

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.Tokens.EmailConfirmationTokenProvider = TokenOptions.DefaultEmailProvider;
                options.Tokens.PasswordResetTokenProvider = TokenOptions.DefaultProvider;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AgroTradeDbContext>()
            .AddTokenProvider<DataProtectorTokenProvider<ApplicationUser>>(TokenOptions.DefaultProvider)
            .AddTokenProvider<EmailTokenProvider<ApplicationUser>>(TokenOptions.DefaultEmailProvider);

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => o.Secret.Length >= 32, "Jwt:Secret must be at least 32 characters.")
            .ValidateOnStart();

        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName));

        services.AddScoped<IAccessTokenService, JwtAccessTokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IEmailSender, ConsoleEmailSender>();
        services.AddScoped<DbSeeder>();

        // Trade & communication (Phase 1)
        services.AddScoped<IFileStorage, LocalFileStorage>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IBusinessService, BusinessService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IRfqService, RfqService>();
        services.AddScoped<ICommunicationService, CommunicationService>();

        return services;
    }
}