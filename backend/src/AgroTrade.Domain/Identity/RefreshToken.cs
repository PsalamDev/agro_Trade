namespace AgroTrade.Domain.Identity;

/// <summary>
/// Opaque refresh token (hashed at rest) for JWT rotation with reuse detection.
/// A refined replacement for Identity's UserTokens storage — carries expiry/revoke state.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>SHA-256 hash of the raw token. Only the hash is persisted.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>Hash of the successor token after rotation (null until replaced).</summary>
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>
    /// Optimistic concurrency token. Rotation reads the row, then writes it revoked with a
    /// successor; two simultaneous refreshes would otherwise both pass the "not revoked" check and
    /// both mint a live successor. The database-generated version makes the second write lose and
    /// roll back, so one rotation yields exactly one successor.
    /// </summary>
    public byte[]? RowVersion { get; set; }
}