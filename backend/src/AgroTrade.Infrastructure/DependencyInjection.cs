using AgroTrade.Application.Admin;
using AgroTrade.Application.Auth;
using AgroTrade.Application.Common;
using AgroTrade.Application.Communication;
using AgroTrade.Application.Trade;
using AgroTrade.Infrastructure.Admin;
using AgroTrade.Infrastructure.Auth;
using AgroTrade.Infrastructure.BackgroundJobs;
using AgroTrade.Infrastructure.Caching;
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

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .Validate(
                o => string.IsNullOrWhiteSpace(o.Smtp.Host) || !string.IsNullOrWhiteSpace(o.FromAddress),
                "Email:FromAddress must be set when Email:Smtp:Host is configured.")
            .ValidateOnStart();

        // MailKit when a server is configured, the console sink otherwise, so a developer with no
        // mail server still sees confirmation/reset messages in the log (plan D3).
        if (!string.IsNullOrWhiteSpace(configuration[$"{EmailOptions.SectionName}:Smtp:Host"]))
        {
            services.AddScoped<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.AddScoped<IEmailSender, ConsoleEmailSender>();
        }

        // Event mail (shipment, quotation, payment, admin alerts) rides the same transport and the
        // same templates, so there is one place to configure delivery.
        services.AddScoped<ITransactionalEmailService, TransactionalEmailService>();

        services.AddScoped<IAccessTokenService, JwtAccessTokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<DbSeeder>();

        // Read-through cache (Phase 5 — "Redis caching"). Redis when ConnectionStrings:Redis is set,
        // an in-process distributed memory cache otherwise; see AddCaching.
        services.AddCaching(configuration);

        // Background maintenance workers (Phase 5 — "background workers"). Each runs a scoped,
        // idempotent sweep on an interval. BackgroundJobs:Enabled=false (used by the test host)
        // stops the whole set without touching the code that owns the work.
        //
        // Registered as singletons *and* hosted, via the same instance: the host runs them, and an
        // operator (or a test) can also resolve one by name to trigger a pass immediately.
        services.AddOptions<BackgroundJobsOptions>()
            .Bind(configuration.GetSection(BackgroundJobsOptions.SectionName));
        services.AddSingleton<RfqExpiryWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<RfqExpiryWorker>());
        services.AddSingleton<RefreshTokenCleanupWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<RefreshTokenCleanupWorker>());

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

        // Logistics-provider directory + booking (Phase 4 exit criterion). A booking links a
        // directory provider to an order; see LogisticsDirectoryService.
        services.AddScoped<ILogisticsDirectoryService, LogisticsDirectoryService>();
    services.AddScoped<IAdminService, AdminService>();
    // Save-less by design: the entry is flushed by the same SaveChanges as the action it records.
    services.AddScoped<IAuditLogWriter, AuditLogWriter>();
    services.AddScoped<AgroTrade.Application.Intelligence.IMarketDataService, AgroTrade.Infrastructure.Services.MarketDataService>();
    // Pure and stateless: the destination-requirements helper answers from the two country codes
    // alone, so it never touches the database.
    services.AddSingleton<AgroTrade.Application.Intelligence.IDestinationRequirementsService, AgroTrade.Infrastructure.Services.DestinationRequirementsService>();
    services.AddScoped<AgroTrade.Application.Trust.IReviewService, AgroTrade.Infrastructure.Services.ReviewService>();
    services.AddScoped<AgroTrade.Application.Trade.IDisputeService, AgroTrade.Infrastructure.Services.DisputeService>();
    services.AddScoped<AgroTrade.Application.Trade.IInvoiceService, AgroTrade.Infrastructure.Services.InvoiceService>();
    services.AddScoped<AgroTrade.Application.Trade.IReceiptService, AgroTrade.Infrastructure.Services.ReceiptService>();

        return services;
    }
}