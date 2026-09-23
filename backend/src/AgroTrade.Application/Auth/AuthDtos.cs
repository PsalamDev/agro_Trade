using System.ComponentModel.DataAnnotations;

namespace AgroTrade.Application.Auth;

/// <summary>Profile + role summary returned with auth operations and /auth/me.</summary>
public sealed class AuthUserDto
{
    public Guid Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? HomeCountry { get; init; }
    public string DefaultCurrency { get; init; } = "NGN";
    public bool IsEmailVerified { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
}

public sealed class AuthResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public int ExpiresInSeconds { get; init; }
    public AuthUserDto User { get; init; } = new();
}

public sealed class RegisterRequest
{
    /// <summary>Email is the login identifier.</summary>
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, MinLength(8), MaxLength(128)]
    public string Password { get; init; } = string.Empty;

    [Required, MaxLength(150)]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>ISO-3166 alpha-2, e.g. "NG".</summary>
    [StringLength(2, MinimumLength = 2)]
    public string? HomeCountry { get; init; }

    /// <summary>ISO-4217, default "NGN".</summary>
    [StringLength(3, MinimumLength = 3)]
    public string DefaultCurrency { get; init; } = "NGN";

    /// <summary>Optional roles (see RoleNames.Registrable). "Buyer" if omitted.</summary>
    public List<string> Roles { get; init; } = [];
}

public sealed class LoginRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;
}

public sealed class RefreshRequest
{
    [Required]
    public string RefreshToken { get; init; } = string.Empty;
}

public sealed class ConfirmEmailRequest
{
    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Token { get; init; } = string.Empty;
}

public sealed class ForgotPasswordRequest
{
    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Token { get; init; } = string.Empty;

    [Required, MinLength(8), MaxLength(128)]
    public string NewPassword { get; init; } = string.Empty;
}

public sealed class UpdateProfileRequest
{
    [MaxLength(150)]
    public string? DisplayName { get; init; }

    [StringLength(2, MinimumLength = 2)]
    public string? HomeCountry { get; init; }

    [StringLength(3, MinimumLength = 3)]
    public string? DefaultCurrency { get; init; }
}