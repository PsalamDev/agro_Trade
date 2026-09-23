using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AgroTrade.Domain.Enums;
using AgroTrade.Infrastructure.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AgroTrade.Tests.Unit;

public sealed class JwtAccessTokenServiceTests
{
    private static readonly JwtOptions Options = new()
    {
        Issuer = "AgroTrade.Test",
        Audience = "AgroTradeApi.Test",
        Secret = "Test-Secret-Key-0123456789abcdef-ABCDEFGHIJKLMNOPQRSTUVWXYZ",
        AccessTokenMinutes = 30,
        RefreshTokenDays = 30,
    };

    [Fact]
    public void CreateAccessToken_IncludesExpectedClaims()
    {
        var service = new JwtAccessTokenService(Microsoft.Extensions.Options.Options.Create(Options));
        var userId = Guid.NewGuid();

        var token = service.CreateAccessToken(userId, "trader@example.com", "Mama Farms", [RoleNames.Supplier, RoleNames.Exporter]);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        Assert.Equal(Options.Issuer, jwt.Issuer);
        Assert.Equal(Options.Audience, jwt.Audiences.Single());
        Assert.Equal(userId.ToString(), jwt.Subject);
        Assert.Equal("trader@example.com", GetClaim(jwt, JwtRegisteredClaimNames.Email));
        var roles = jwt.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToArray();
        Assert.Equal([RoleNames.Supplier, RoleNames.Exporter], roles);
        Assert.True(jwt.ValidTo > DateTime.UtcNow.AddMinutes(20));
        Assert.NotNull(jwt.Id); // jti present
    }

    [Fact]
    public void RefreshTokenFactory_ProducesUniqueUrlSafeTokens()
    {
        var (raw1, hash1) = RefreshTokenFactory.Create();
        var (raw2, hash2) = RefreshTokenFactory.Create();

        Assert.NotEqual(raw1, raw2);
        Assert.NotEqual(hash1, hash2);
        Assert.DoesNotContain(Path.GetInvalidFileNameChars(), raw1);
        Assert.Equal(RefreshTokenFactory.Hash(raw1), hash1);
        Assert.NotEqual(raw1, hash1);
    }

    private static string GetClaim(JwtSecurityToken jwt, string type) =>
        jwt.Claims.FirstOrDefault(c => c.Type == type)?.Value ?? throw new KeyNotFoundException(type);
}