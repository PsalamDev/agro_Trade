namespace AgroTrade.Domain.Identity;

/// <summary>
/// A short numeric code emailed to a user who has asked to reset their password. The raw code
/// exists only in that email; only its SHA-256 hash is persisted, so a database dump yields no
/// working codes.
/// </summary>
/// <remarks>
/// A code is single-use and short-lived, and each request retires the user's previous code so at
/// most one is ever live. <see cref="Attempts"/> counts wrong guesses: without it, a six-digit code
/// is only a million possibilities and a script could walk them inside the validity window.
/// </remarks>
public sealed class PasswordResetCode
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>SHA-256 hash of the raw code. Only the hash is persisted.</summary>
    public string CodeHash { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Set when the code is used successfully or retired by a newer request.</summary>
    public DateTime? ConsumedAtUtc { get; set; }

    /// <summary>Wrong guesses so far. The code is refused once the attempt cap is reached.</summary>
    public int Attempts { get; set; }
}
