using System.Security.Cryptography;
using System.Text;

namespace AgroTrade.Infrastructure.Auth;

/// <summary>
/// Creates and verifies the short numeric code used by the password-reset flow.
/// </summary>
/// <remarks>
/// The code is deliberately low-entropy — six digits fit in a mail a user will read and retype — so
/// it is not a bearer token and must not be treated as one. Its safety comes from the pairing in
/// <c>AuthService</c>: a short expiry, one live code per user, and a cap on wrong guesses. Only the
/// SHA-256 hash is persisted, matching how refresh tokens are stored.
/// </remarks>
public static class ResetCodeFactory
{
    public const int Length = 6;

    /// <summary>A uniformly random <see cref="Length"/>-digit code, zero-padded.</summary>
    public static string Create() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    /// <summary>Constant-time comparison of a presented code against a stored hash.</summary>
    public static bool Verify(string code, string expectedHash) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Hash(code)),
            Encoding.UTF8.GetBytes(expectedHash));
}
