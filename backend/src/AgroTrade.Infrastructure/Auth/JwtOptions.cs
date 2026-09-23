namespace AgroTrade.Infrastructure.Auth;

/// <summary>JWT issuer/audience/signing + token lifetimes. Bound from the "Jwt" config section.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;

    /// <summary>Signing key (>= 32 bytes, HMAC-SHA256). Never committed — use user-secrets / vault.</summary>
    public string Secret { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 30;
}