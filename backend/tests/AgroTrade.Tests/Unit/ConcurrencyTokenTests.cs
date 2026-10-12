using AgroTrade.Domain.Identity;
using AgroTrade.Domain.Trade;
using AgroTrade.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Tests.Unit;

/// <summary>
/// Pins the two aggregates whose read-then-write races became correctness bugs: refresh-token
/// rotation (a token could yield two live successors) and payment state (a late webhook could
/// overwrite a settlement).
/// </summary>
/// <remarks>
/// The model is built directly, so no database is contacted. The rowversion behaviour itself is
/// exercised against SQL Server in the integration suite.
/// </remarks>
public sealed class ConcurrencyTokenTests
{
    [Theory]
    [InlineData(typeof(RefreshToken))]
    [InlineData(typeof(Payment))]
    public void RowVersion_IsAConcurrencyToken(Type entityType)
    {
        using var db = NewContext();

        var property = db.Model.FindEntityType(entityType)!.FindProperty(nameof(RefreshToken.RowVersion));

        Assert.NotNull(property);
        Assert.True(property!.IsConcurrencyToken, $"'{entityType.Name}.RowVersion' is not a concurrency token.");
        Assert.Equal(typeof(byte[]), property.ClrType);
    }

    private static AgroTradeDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AgroTradeDbContext>()
            // The model is built without a connection, so the target is never contacted.
            .UseSqlServer("Server=localhost;Database=AgroTrade_ModelOnly;Trusted_Connection=True;TrustServerCertificate=True;")
            .Options);
}
