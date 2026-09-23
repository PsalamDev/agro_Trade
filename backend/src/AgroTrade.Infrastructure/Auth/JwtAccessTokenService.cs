using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AgroTrade.Infrastructure.Auth;

/// <summary>
/// Issues signed JWT access tokens. HMAC-SHA256 (symmetric) — the secret never
/// leaves configuration and is overridden from user-secrets in dev.
/// </summary>
public interface IAccessTokenService
{
    string CreateAccessToken(Guid userId, string email, string displayName, IEnumerable<string> roles);
}

public sealed class JwtAccessTokenService(IOptions<JwtOptions> options) : IAccessTokenService
{
    private readonly JwtOptions _options = options.Value;

    public string CreateAccessToken(Guid userId, string email, string displayName, IEnumerable<string> roles)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Name, displayName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>
/// Raw refresh-token material + its SHA-256 hash. Only the hash is persisted.
/// </summary>
public static class RefreshTokenFactory
{
    public static (string RawToken, string Hash) Create()
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        var raw = Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        var hash = Convert.ToHexString(Sha256(raw));
        return (raw, hash);
    }

    public static string Hash(string rawToken) => Convert.ToHexString(Sha256(rawToken));

    private static byte[] Sha256(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}