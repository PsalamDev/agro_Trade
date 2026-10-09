using AgroTrade.Domain.Intelligence;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Infrastructure.Data;

public sealed partial class DbSeeder
{
    private async Task SeedMarketDataAsync(CancellationToken ct)
    {
        var hasAny = await db.MarketData.CountAsync(ct) > 0;
        if (hasAny)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var entries = new List<MarketData>
        {
            new() { Id = Guid.NewGuid(), Commodity = "Cashew", Country = "NG", Region = "Kwara", Unit = "Ton", Price = 1750m, Currency = "USD", Source = "Admin", ReportedAtUtc = now.AddDays(-30), Notes = "Local farmgate" },
            new() { Id = Guid.NewGuid(), Commodity = "Cashew", Country = "NG", Region = "Kwara", Unit = "Ton", Price = 1820m, Currency = "USD", Source = "Admin", ReportedAtUtc = now.AddDays(-14), Notes = "Uptrend" },
            new() { Id = Guid.NewGuid(), Commodity = "Cashew", Country = "VN", Unit = "Ton", Price = 2050m, Currency = "USD", Source = "ICCO", ReportedAtUtc = now.AddDays(-7), Notes = "Export benchmark" },
            new() { Id = Guid.NewGuid(), Commodity = "Cocoa", Country = "GH", Region = "Ashanti", Unit = "Ton", Price = 2420m, Currency = "USD", Source = "ICCO", ReportedAtUtc = now.AddDays(-28) },
            new() { Id = Guid.NewGuid(), Commodity = "Cocoa", Country = "CI", Unit = "Ton", Price = 2380m, Currency = "USD", Source = "ICCO", ReportedAtUtc = now.AddDays(-21) },
            new() { Id = Guid.NewGuid(), Commodity = "Maize", Country = "KE", Region = "Nairobi", Unit = "Ton", Price = 320m, Currency = "USD", Source = "FAO", ReportedAtUtc = now.AddDays(-14) },
            new() { Id = Guid.NewGuid(), Commodity = "Maize", Country = "NG", Region = "Kano", Unit = "Ton", Price = 295m, Currency = "USD", Source = "FAO", ReportedAtUtc = now.AddDays(-10) },
            new() { Id = Guid.NewGuid(), Commodity = "Rice", Country = "TH", Unit = "Ton", Price = 580m, Currency = "USD", Source = "FAO", ReportedAtUtc = now.AddDays(-7) },
            new() { Id = Guid.NewGuid(), Commodity = "Rice", Country = "NG", Unit = "Ton", Price = 620m, Currency = "USD", Source = "Admin", ReportedAtUtc = now.AddDays(-5) },
            new() { Id = Guid.NewGuid(), Commodity = "Sesame", Country = "ET", Unit = "Ton", Price = 1450m, Currency = "USD", Source = "Admin", ReportedAtUtc = now.AddDays(-14) },
        };

        db.MarketData.AddRange(entries);
        await db.SaveChangesAsync(ct);
    }
}