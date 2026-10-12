using AgroTrade.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Tests.Unit;

/// <summary>
/// Every database timestamp must be read back as UTC (risk R15).
/// </summary>
/// <remarks>
/// SQL Server's <c>datetime2</c> carries no offset, so EF materialises these as
/// <see cref="DateTimeKind.Unspecified"/> and the API serialises them without a trailing <c>Z</c>.
/// A client then reads a bare date-time as *local* time and shifts it by its own offset. These tests
/// pin the model convention that prevents that. The model is built directly, so no database is
/// contacted.
/// </remarks>
public sealed class UtcTimestampConventionTests
{
    [Fact]
    public void EveryDateTimeProperty_IsReadFromTheDatabaseAsUtc()
    {
        using var db = NewContext();

        var properties = db.Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?))
            .ToList();

        Assert.NotEmpty(properties);
        foreach (var property in properties)
        {
            var converter = property.GetValueConverter();
            Assert.True(converter is not null, $"'{property.Name}' has no UTC value converter.");

            var stored = DateTime.SpecifyKind(new DateTime(2026, 1, 2, 3, 4, 5), DateTimeKind.Unspecified);
            var read = (DateTime)converter!.ConvertFromProvider(stored)!;
            Assert.Equal(DateTimeKind.Utc, read.Kind);
        }
    }

    [Fact]
    public void ANullTimestamp_StaysNull()
    {
        using var db = NewContext();

        var nullable = db.Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .First(p => p.ClrType == typeof(DateTime?));

        Assert.Null(nullable.GetValueConverter()!.ConvertFromProvider(null));
    }

    private static AgroTradeDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AgroTradeDbContext>()
            // The model is built without a connection, so the target is never contacted.
            .UseSqlServer("Server=localhost;Database=AgroTrade_ModelOnly;Trusted_Connection=True;TrustServerCertificate=True;")
            .Options);
}
