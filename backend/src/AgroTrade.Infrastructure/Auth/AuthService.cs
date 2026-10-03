using System.Security.Cryptography;
using System.Text;
using AgroTrade.Application.Auth;
using AgroTrade.Application.Common;
using AgroTrade.Domain.Enums;
using AgroTrade.Domain.Identity;
using AgroTrade.Infrastructure.Data;
using AgroTrade.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgroTrade.Infrastructure.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Dev convenience — confirm email addresses automatically at registration.</summary>
    public bool AutoConfirmEmail { get; set; } = true;

    /// <summary>Base URL used to build links sent by email (confirm token, reset).</summary>
    public string AppBaseUrl { get; set; } = "http://localhost:5173";
}

/// <summary>
/// Identity-backed implementation of the auth workflow (plan §04.4): register with
/// multi-role assignment, login, refresh-token rotation with reuse detection, logout,
/// email confirm, and password reset flows.
/// </summary>
public sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    AgroTradeDbContext db,
    IAccessTokenService accessTokens,
    IOptions<JwtOptions> jwtOptions,
    IOptions<AuthOptions> authOptions,
    IEmailSender emailSender) : IAuthService
{
    private readonly JwtOptions _jwt = jwtOptions.Value;
    private readonly AuthOptions _auth = authOptions.Value;

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        // Single-supplier model: registration is buyers/importers only. Supplier-side
        // roles are filtered out and non-buyer registrations default to Buyer.
        var roles = request.Roles.Count > 0 ? request.Roles : [RoleNames.Buyer];
        roles = roles
            .Where(r => RoleNames.Registrable.Contains(r))
            .Distinct()
            .ToList();

        if (roles.Count == 0)
        {
            roles = [RoleNames.Buyer];
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            HomeCountry = request.HomeCountry?.ToUpperInvariant(),
            DefaultCurrency = request.DefaultCurrency.ToUpperInvariant(),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            throw ApiException.BadRequest("REGISTRATION_FAILED", FormatIdentityErrors(result));
        }

        var roleResult = await userManager.AddToRolesAsync(user, roles);
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            throw ApiException.BadRequest("REGISTRATION_FAILED", FormatIdentityErrors(roleResult));
        }

        if (_auth.AutoConfirmEmail)
        {
            user.EmailConfirmed = true;
            await userManager.UpdateAsync(user);
        }
        else
        {
            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
            await SendTemplatedEmailAsync(
                user,
                "Confirm your AgroTrade account",
                $"Your confirmation code: <strong>{System.Net.WebUtility.HtmlEncode(token)}</strong><br/>" +
                $"Or open: {_auth.AppBaseUrl}/confirm-email?token={Uri.EscapeDataString(token)}");
        }

        return await IssueTokenPairAsync(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await userManager.FindByEmailAsync(email)
            ?? throw ApiException.Unauthorized();

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            throw ApiException.Unauthorized();
        }

        EnsureActive(user);
        return await IssueTokenPairAsync(user);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
    {
        var presentedHash = OpaqueTokenFactory.Hash(request.RefreshToken);
        var stored = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == presentedHash, ct)
            ?? throw ApiException.Unauthorized("INVALID_REFRESH_TOKEN", "Refresh token is invalid.");

        if (stored.RevokedAtUtc is not null)
        {
            // Reuse detection: a rotated token was replayed — revoke the whole family.
            await RevokeAllForUserAsync(stored.UserId, ct);
            throw ApiException.Unauthorized("REFRESH_TOKEN_REUSED", "Refresh token was already used; all sessions revoked.");
        }

        if (stored.ExpiresAtUtc < DateTime.UtcNow)
        {
            throw ApiException.Unauthorized("REFRESH_TOKEN_EXPIRED", "Refresh token has expired.");
        }

        var user = await userManager.FindByIdAsync(stored.UserId.ToString())
            ?? throw ApiException.Unauthorized();

        EnsureActive(user);

        var pair = await CreateTokenPairAsync(user, persist: true, ct);
        stored.RevokedAtUtc = DateTime.UtcNow;
        stored.ReplacedByTokenHash = pair.RefreshTokenHash;
        await db.SaveChangesAsync(ct);

        return pair.Response;
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = OpaqueTokenFactory.Hash(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is not null && stored.RevokedAtUtc is null)
        {
            stored.RevokedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim().ToLowerInvariant())
            ?? throw ApiException.NotFound("USER_NOT_FOUND", "No account exists for that email.");
        var result = await userManager.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
        {
            throw ApiException.BadRequest("INVALID_CONFIRMATION_TOKEN", "The confirmation token is invalid or expired.");
        }
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim().ToLowerInvariant());
        if (user is null || !(await userManager.IsEmailConfirmedAsync(user)))
        {
            // Deliberately identical response to avoid account enumeration.
            return;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        await SendTemplatedEmailAsync(
            user,
            "Reset your AgroTrade password",
            $"Your password reset code: <strong>{System.Net.WebUtility.HtmlEncode(token)}</strong><br/>" +
            $"Or open: {_auth.AppBaseUrl}/reset-password?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}");
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim().ToLowerInvariant())
            ?? throw ApiException.NotFound("USER_NOT_FOUND", "No account exists for that email.");
        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            throw ApiException.BadRequest("INVALID_RESET_TOKEN", FormatIdentityErrors(result));
        }
    }

    public async Task<AuthUserDto> GetCurrentUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await LoadUserAsync(userId)
            ?? throw ApiException.NotFound("USER_NOT_FOUND", "User not found.");
        return await ToDtoAsync(user);
    }

    public async Task<AuthUserDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        var user = await LoadUserAsync(userId)
            ?? throw ApiException.NotFound("USER_NOT_FOUND", "User not found.");

        if (!string.IsNullOrWhiteSpace(request.DisplayName)) user.DisplayName = request.DisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(request.HomeCountry)) user.HomeCountry = request.HomeCountry.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(request.DefaultCurrency))
        {
            user.DefaultCurrency = request.DefaultCurrency.Trim().ToUpperInvariant();
        }

        user.UpdatedAtUtc = DateTime.UtcNow;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            throw ApiException.BadRequest("PROFILE_UPDATE_FAILED", FormatIdentityErrors(result));
        }

        return await ToDtoAsync(user);
    }

    // ---- internals ----

    private async Task<AuthResponse> IssueTokenPairAsync(ApplicationUser user)
    {
        var pair = await CreateTokenPairAsync(user, persist: true, CancellationToken.None);
        return pair.Response;
    }

    private async Task<(AuthResponse Response, string RefreshTokenHash)> CreateTokenPairAsync(
        ApplicationUser user, bool persist, CancellationToken ct)
    {
        var roles = (await userManager.GetRolesAsync(user)).ToList();
        var access = accessTokens.CreateAccessToken(user.Id, user.Email!, user.DisplayName, roles);

        var (raw, hash) = OpaqueTokenFactory.Create();
        var refresh = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = hash,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(_jwt.RefreshTokenDays),
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.RefreshTokens.Add(refresh);
        if (persist)
        {
            await db.SaveChangesAsync(ct);
        }

        var response = new AuthResponse
        {
            AccessToken = access,
            RefreshToken = raw,
            ExpiresInSeconds = _jwt.AccessTokenMinutes * 60,
            User = await ToDtoAsync(user),
        };
        return (response, hash);
    }

    private async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct)
    {
        var active = db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAtUtc == null);
        await active.ForEachAsync(t => t.RevokedAtUtc = DateTime.UtcNow, ct);
        await db.SaveChangesAsync(ct);
    }

    private Task<ApplicationUser?> LoadUserAsync(Guid userId) =>
        userManager.FindByIdAsync(userId.ToString());

    private async Task<AuthUserDto> ToDtoAsync(ApplicationUser user) => new()
    {
        Id = user.Id,
        Email = user.Email ?? string.Empty,
        DisplayName = user.DisplayName,
        HomeCountry = user.HomeCountry,
        DefaultCurrency = user.DefaultCurrency,
        IsEmailVerified = user.EmailConfirmed,
        IsActive = user.IsActive,
        Roles = (await userManager.GetRolesAsync(user)).ToList(),
    };

    private static void EnsureActive(ApplicationUser user)
    {
        if (!user.IsActive)
        {
            throw ApiException.Forbidden("ACCOUNT_SUSPENDED", "This account has been suspended. Contact support.");
        }
    }

    private static string FormatIdentityErrors(IdentityResult result) =>
        string.Join(" ", result.Errors.Select(e => e.Description));

    private async Task SendTemplatedEmailAsync(ApplicationUser user, string subject, string body)
    {
        await emailSender.SendAsync(new EmailMessage(
            To: user.Email!,
            Subject: subject,
            HtmlBody: body,
            PlainTextBody: body));
    }
}