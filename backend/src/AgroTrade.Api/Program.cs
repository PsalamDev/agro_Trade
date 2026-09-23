using AgroTrade.Api.Middleware;
using AgroTrade.Application;
using AgroTrade.Infrastructure;
using AgroTrade.Infrastructure.Data;
using AgroTrade.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Dev-only secrets (Jwt:Secret, ConnectionStrings:Default, Seed:*). Never committed.
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

builder.Host.UseSerilog((context, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/agro-trade-.log", rollingInterval: RollingInterval.Day));

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        // Uniform error contract: all validation/binding failures flow through
        // GlobalExceptionHandler as ProblemDetails (plan §03), not the default
        // ModelState shape.
        options.SuppressModelStateInvalidFilter = true;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AgroTrade API",
        Version = "v1",
        Description = "Agro-Allied Global Trade Platform — cross-border import/export marketplace.",
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT access token from /api/v1/auth/login.",
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
            },
            Array.Empty<string>()
        },
    });
});

// ---- Authentication: JWT with token validation hooks ----
var jwt = builder.Configuration.GetSection("Jwt");
var secret = jwt["Secret"];
if (string.IsNullOrWhiteSpace(secret))
{
    throw new InvalidOperationException(
        "Jwt:Secret is missing or empty. In development set it with: " +
        "dotnet user-secrets set \"Jwt:Secret\" \"...\" --project src/AgroTrade.Api");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwt["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role,
        };
        options.Events = new JwtBearerEvents
        {
            // Enforce account state per request (suspend flag, email confirmation).
            OnTokenValidated = async context =>
            {
                var userManager = context.HttpContext.RequestServices
                    .GetRequiredService<UserManager<ApplicationUser>>();
                var subject = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? context.Principal?.FindFirstValue("sub");
                var user = subject is null ? null : await userManager.FindByIdAsync(subject);
                if (user is null)
                {
                    context.Fail("User no longer exists.");
                    return;
                }
                if (!user.IsActive)
                {
                    context.Fail("Account is suspended.");
                    return;
                }
                if (!user.EmailConfirmed)
                {
                    context.Fail("Email is not confirmed.");
                }
            },
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.AddPolicy(Policies.Administrator, policy => policy.RequireRole(AgroTrade.Domain.Enums.RoleNames.Administrator));
});

// ---- Cross-cutting ----
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AgroTradeDbContext>("database");

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var authLimit = builder.Configuration.GetValue("RateLimit:AuthPermitLimit", 20);
    var authWindowSeconds = builder.Configuration.GetValue("RateLimit:AuthWindowSeconds", 60);
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authLimit,
                Window = TimeSpan.FromSeconds(authWindowSeconds),
                QueueLimit = 0,
            }));
});

var app = builder.Build();

// Startup data maintenance (migrations + roles/admin seed) in Development only.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<DbSeeder>();
    await seeder.SeedAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
// Liveness: the process is up. Readiness (with DB + other checks): container probes.
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => true,
});

app.Run();

/// <summary>Authorization policy names (plan §04.4). Expanded in later phases.</summary>
public static partial class Policies
{
    public const string Administrator = "Administrator";
}

/// <summary>Factory entry point for WebApplicationFactory integration tests.</summary>
public partial class Program;