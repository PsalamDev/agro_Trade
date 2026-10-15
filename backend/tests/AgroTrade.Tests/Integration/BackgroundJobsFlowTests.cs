using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AgroTrade.Domain.Enums;
using AgroTrade.Domain.Identity;
using AgroTrade.Infrastructure.BackgroundJobs;
using AgroTrade.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgroTrade.Tests.Integration;

/// <summary>
/// The recurring maintenance workers: the overdue-RFQ sweep and the refresh-token cleanup.
///
/// <para>
/// The point of the RFQ worker is that a deadline becomes real even with no traffic. The RFQ
/// service's lazy sweep only fires when someone reads a list or a detail, so without a worker an
/// expired RFQ keeps advertising itself and its buyer is never told. The point of the token worker
/// is the opposite discipline: it deletes only rows that can no longer authenticate, because a
/// revoked row is still the evidence that a replay happened. These tests drive each worker's pass
/// directly instead of waiting on its timer — the timer itself is covered by the unit tests.
/// </para>
/// </summary>
public sealed class BackgroundJobsFlowTests : IClassFixture<TestingWebAppFactory>
{
    private const string Password = "StrongPass123";

    private readonly TestingWebAppFactory _factory;
    private readonly HttpClient _client;

    public BackgroundJobsFlowTests(TestingWebAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureSchema();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task RfqExpiryWorker_ExpiresOverdueRfq_ButLeavesALiveOneOpen()
    {
        var (token, _) = await RegisterBuyerAsync($"rfq_worker_{Guid.NewGuid():N}");
        var overdueId = await CreateRfqAsync(token);
        var liveId = await CreateRfqAsync(token);

        // The create endpoint refuses a deadline that has already passed, so age the row afterwards.
        await UseDbAsync(async db =>
        {
            var overdue = await db.TradeRequests.SingleAsync(r => r.Id == overdueId);
            overdue.ExpiresAt = DateTime.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        });

        var swept = await _factory.Services.GetRequiredService<RfqExpiryWorker>()
            .RunOnceAsync(CancellationToken.None);

        Assert.True(swept >= 1, "the worker should have swept at least the RFQ it was given.");

        await UseDbAsync(async db =>
        {
            Assert.Equal(RfqStatus.Expired, await StatusAsync(db, overdueId));
            Assert.Equal(RfqStatus.Open, await StatusAsync(db, liveId));

            // ...and the buyer was told, on the same link the UI deep-links to.
            var buyerId = await db.TradeRequests.AsNoTracking()
                .Where(r => r.Id == overdueId)
                .Select(r => r.BuyerUserId)
                .SingleAsync();
            Assert.True(await db.Notifications.AnyAsync(n =>
                n.UserId == buyerId && n.Type == NotificationType.Rfq && n.Link == $"/rfqs/{overdueId}"));
        });
    }

    [Fact]
    public async Task RefreshTokenCleanupWorker_DeletesExpiredTokens_ButKeepsLiveAndRevokedOnes()
    {
        var (_, userId) = await RegisterBuyerAsync($"token_worker_{Guid.NewGuid():N}");
        var expiredId = Guid.NewGuid();
        var liveId = Guid.NewGuid();
        var revokedButLiveId = Guid.NewGuid();

        await UseDbAsync(async db =>
        {
            db.RefreshTokens.AddRange(
                Token(expiredId, userId, expiresAtUtc: DateTime.UtcNow.AddDays(-1), revokedAtUtc: null),
                Token(liveId, userId, expiresAtUtc: DateTime.UtcNow.AddDays(1), revokedAtUtc: null),
                Token(revokedButLiveId, userId, expiresAtUtc: DateTime.UtcNow.AddDays(1), revokedAtUtc: DateTime.UtcNow));
            await db.SaveChangesAsync();
        });

        var deleted = await _factory.Services.GetRequiredService<RefreshTokenCleanupWorker>()
            .RunOnceAsync(CancellationToken.None);

        Assert.True(deleted >= 1);

        await UseDbAsync(async db =>
        {
            Assert.False(await db.RefreshTokens.AnyAsync(t => t.Id == expiredId));
            Assert.True(await db.RefreshTokens.AnyAsync(t => t.Id == liveId));
            // Revoked but not expired: kept so a replay is still linkable to its family.
            Assert.True(await db.RefreshTokens.AnyAsync(t => t.Id == revokedButLiveId));
        });
    }

    private static Task<RfqStatus> StatusAsync(AgroTradeDbContext db, Guid id) =>
        db.TradeRequests.AsNoTracking().Where(r => r.Id == id).Select(r => r.Status).SingleAsync();

    private static RefreshToken Token(Guid id, Guid userId, DateTime expiresAtUtc, DateTime? revokedAtUtc) => new()
    {
        Id = id,
        UserId = userId,
        TokenHash = id.ToString("N"),
        ExpiresAtUtc = expiresAtUtc,
        CreatedAtUtc = DateTime.UtcNow,
        RevokedAtUtc = revokedAtUtc,
    };

    private async Task<(string AccessToken, Guid UserId)> RegisterBuyerAsync(string prefix)
    {
        var email = $"{prefix}@jobs.tests.agrotrade.local";
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = Password,
            displayName = email,
            homeCountry = "NG",
            defaultCurrency = "USD",
            roles = new[] { RoleNames.Buyer },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var token = (await response.Content.ReadFromJsonAsync<AuthEnvelope>())!.AccessToken;

        Guid userId = default;
        await UseDbAsync(async db =>
            userId = await db.Users.AsNoTracking().Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
        return (token, userId);
    }

    private async Task<Guid> CreateRfqAsync(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/rfqs")
        {
            Content = JsonContent.Create(new
            {
                title = $"worker sweep {Guid.NewGuid():N}",
                quantity = 5m,
                unit = (int)PriceUnit.PerTon,
                destinationCountry = "NG",
                destinationPortOrCity = "Lagos",
                preferredIncoterm = (int)Incoterms.Cif,
                preferredCurrency = "USD",
                expiresAt = DateTime.UtcNow.AddDays(30),
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdMirror>())!.Id;
    }

    private async Task UseDbAsync(Func<AgroTradeDbContext, Task> work)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgroTradeDbContext>();
        await work(db);
    }

    private sealed class AuthEnvelope
    {
        public string AccessToken { get; init; } = string.Empty;
    }

    private sealed class IdMirror
    {
        public Guid Id { get; init; }
    }
}
