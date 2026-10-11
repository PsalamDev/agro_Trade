using AgroTrade.Application.Admin;
using AgroTrade.Application.Auth;
using AgroTrade.Application.Common;
using AgroTrade.Application.Communication;
using AgroTrade.Application.Trade;
using AgroTrade.Infrastructure.Admin;
using AgroTrade.Infrastructure.Auth;
using AgroTrade.Infrastructure.Communication;
using AgroTrade.Infrastructure.Data;
using AgroTrade.Infrastructure.Email;
using AgroTrade.Infrastructure.Identity;
using AgroTrade.Infrastructure.Logistics;
using AgroTrade.Infrastructure.Payments;
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

        // Validated on start rather than allowed to be blank: this secret is the only thing
        // between a stranger with the webhook URL and the ability to mark invoices paid.
        services.AddOptions<PaymentOptions>()
            .Bind(configuration.GetSection(PaymentOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.SigningSecret),
                "Payments:SigningSecret must be set — webhooks cannot be verified without it.")
            .Validate(o => o.SupportedCurrencies is { Length: > 0 },
                "Payments:SupportedCurrencies must name at least one corridor.")
            .ValidateOnStart();

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

        // Trade execution (Phase 2)
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<ITradeDocumentService, TradeDocumentService>();

        // Payments (task 2.4). The gateway is the only adapter, and it is registered rather
        // than injected by name, so adding a corridor means adding an IPaymentGateway to this
        // list and nothing else. SandboxPaymentGateway is also resolved concretely for the
        // simulator; the singleton list below keeps both the interface and that concrete
        // instance pointing at the same object.
        services.AddSingleton<SandboxPaymentGateway>();
        services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<SandboxPaymentGateway>());
        services.AddSingleton<IPaymentGatewayResolver, PaymentGatewayResolver>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<ISandboxPaymentSimulator, SandboxPaymentSimulator>();

        // Shipments (task 2.6). The guest tracking read is anonymous, so this service is also the
        // one place that has to be careful about what it puts in the public payload.
        services.AddScoped<IShipmentService, ShipmentService>();
    services.AddScoped<IAdminService, AdminService>();
    // Save-less by design: the entry is flushed by the same SaveChanges as the action it records.
    services.AddScoped<IAuditLogWriter, AuditLogWriter>();
    services.AddScoped<AgroTrade.Application.Intelligence.IMarketDataService, AgroTrade.Infrastructure.Services.MarketDataService>();
    services.AddScoped<AgroTrade.Application.Trust.IReviewService, AgroTrade.Infrastructure.Services.ReviewService>();
    services.AddScoped<AgroTrade.Application.Trade.IDisputeService, AgroTrade.Infrastructure.Services.DisputeService>();
    services.AddScoped<AgroTrade.Application.Trade.IInvoiceService, AgroTrade.Infrastructure.Services.InvoiceService>();
    services.AddScoped<AgroTrade.Application.Trade.IReceiptService, AgroTrade.Infrastructure.Services.ReceiptService>();

        return services;
    }
}