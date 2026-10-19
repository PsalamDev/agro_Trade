using System.Security.Cryptography;
using System.Text;
using AgroTrade.Application.Admin;
using AgroTrade.Application.Auth;
using AgroTrade.Application.Common;
using AgroTrade.Domain.Enums;
using AgroTrade.Domain.Identity;
using AgroTrade.Infrastructure.Data;
using AgroTrade.Infrastructure.Email;
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

    /// <summary>How long an emailed password-reset code stays valid.</summary>
    public int ResetCodeLifetimeMinutes { get; set; } = 10;

    /// <summary>Wrong guesses allowed against a code before it is retired.</summary>
    public int ResetCodeMaxAttempts { get; set; } = 5;
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

    /// <summary>
    /// Prefix of a buyer's account number, e.g. <c>BUY-ID-AG-000001</c>.
    /// </summary>
    /// <remarks>
    /// Flat rather than year-scoped: an order or RFQ number is a document reference that resets
    /// each January, but a buyer's is a permanent account number, so the counter never restarts.
    /// </remarks>
    private const string BuyerIdPrefix = "BUY-ID-AG-";

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        // Two platform settings gate registration: whether it is open at all, and which role a
        // registrant who names none receives. Both are read here rather than in a validator because
        // they are runtime state, not properties of the request, and a validator cannot see the
        // database. The catalogue supplies the default when no row has been written.
        var open = PlatformSettingsCatalog.ReadBoolean(
            await ReadSettingAsync(PlatformSettingsCatalog.RegistrationOpenKey, ct), fallback: true);
        if (!open)
        {
            throw ApiException.Forbidden(
                "REGISTRATION_CLOSED",
                "Registration is temporarily closed. Please try again later.");
        }

        // Single-supplier model: registration is buyers only. Supplier-side roles are
        // filtered out, and a registrant who names none gets the configured default role — Buyer
        // unless an administrator has chosen otherwise.
        var fallbackRole = PlatformSettingsCatalog.ReadRole(
            await ReadSettingAsync(PlatformSettingsCatalog.RegistrationDefaultRoleKey, ct), RoleNames.Buyer);

        var roles = request.Roles.Count > 0 ? request.Roles : [fallbackRole];
        roles = roles
            .Where(r => RoleNames.Registrable.Contains(r))
            .Distinct()
            .ToList();

        if (roles.Count == 0)
        {
            roles = [fallbackRole];
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

        // Every registrable role is buyer-side, so in practice this is always taken; the check keeps
        // the rule explicit if a supplier role is ever added to registration.
        if (roles.Any(r => RoleNames.BuyerRoles.Contains(r)))
        {
            // The account number is MAX+1 over the ones already issued — a read-then-write that is
            // racy on its own, so two simultaneous registrations could be handed the same number and
            // one would fail on the unique index. SequenceAllocator holds a range lock over the
            // BuyerId index *across the insert*, so the number and the row carrying it commit or roll
            // back together; the insert is Identity's create, passed as the allocator's save.
            await SequenceAllocator.AllocateAsync(
                db,
                table: "AspNetUsers",
                column: "BuyerId",
                prefix: BuyerIdPrefix,
                assign: number => user.BuyerId = number,
                save: async token =>
                {
                    var created = await userManager.CreateAsync(user, request.Password);
                    if (!created.Succeeded)
                    {
                        throw ApiException.BadRequest("REGISTRATION_FAILED", FormatIdentityErrors(created));
                    }
                },
                ct);
        }
        else
        {
            var created = await userManager.CreateAsync(user, request.Password);
            if (!created.Succeeded)
            {
                throw ApiException.BadRequest("REGISTRATION_FAILED", FormatIdentityErrors(created));
            }
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
            var confirmUrl = $"{_auth.AppBaseUrl}/confirm-email?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}";
            var emailBody = EmailTemplates.ConfirmEmail(user.DisplayName, confirmUrl, token);
            await emailSender.SendAsync(new EmailMessage(
                To: user.Email!,
                Subject: emailBody.Subject,
                HtmlBody: emailBody.Html,
                PlainTextBody: emailBody.Text));
        }

        return await IssueTokenPairAsync(user);
    }

    /// <summary>Reads one platform setting's stored value, or null when no row exists.</summary>
    /// <remarks>
    /// A single-key read rather than loading the whole table: registration is on a hot path and only
    /// cares about the two keys it names. The catalogue turns the null into the documented default,
    /// so "no row" and "row removed" behave identically.
    /// </remarks>
    private async Task<string?> ReadSettingAsync(string key, CancellationToken ct) =>
        await db.PlatformSettings.AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

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

        // Stage the successor and the retirement of the presented token, then commit the pair in
        // one SaveChanges. RefreshToken carries a rowversion, so if a second request rotated the
        // same token between this read and that write, the UPDATE matches no row and the whole
        // save — successor insert included — rolls back. One rotation therefore yields exactly one
        // live successor; previously both requests could mint one.
        var pair = await CreateTokenPairAsync(user, persist: false, ct);
        stored.RevokedAtUtc = DateTime.UtcNow;
        stored.ReplacedByTokenHash = pair.RefreshTokenHash;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two refreshes raced on one token. The other won and committed its successor; this
            // one did not. Presenting a rotated token twice is exactly the replay signal the
            // family revoke exists for, so drop the successor this request staged and revoke the
            // family rather than leave two live tokens behind a single rotation.
            db.ChangeTracker.Clear();
            await RevokeAllForUserAsync(stored.UserId, ct);
            throw ApiException.Unauthorized(
                "REFRESH_TOKEN_REUSED",
                "Refresh token was already used; all sessions revoked.");
        }

        return pair.Response;
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = OpaqueTokenFactory.Hash(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is not null && stored.RevokedAtUtc is null)
        {
            stored.RevokedAtUtc = DateTime.UtcNow;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // A concurrent refresh or logout already retired this token, which is the state
                // logging out wanted anyway. Nothing left to do.
                db.ChangeTracker.Clear();
            }
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

        var now = DateTime.UtcNow;

        // A newer request supersedes an older one. Retiring the outstanding codes keeps exactly one
        // live at a time, so a code the user was sent earlier cannot still be redeemed after they
        // ask for a fresh one.
        await db.PasswordResetCodes
            .Where(c => c.UserId == user.Id && c.ConsumedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAtUtc, now), ct);

        var code = ResetCodeFactory.Create();
        db.PasswordResetCodes.Add(new PasswordResetCode
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CodeHash = ResetCodeFactory.Hash(code),
            ExpiresAtUtc = now.AddMinutes(_auth.ResetCodeLifetimeMinutes),
            CreatedAtUtc = now,
        });
        await db.SaveChangesAsync(ct);

        var resetUrl = $"{_auth.AppBaseUrl}/reset-password?email={Uri.EscapeDataString(user.Email!)}&code={Uri.EscapeDataString(code)}";
        var resetBody = EmailTemplates.PasswordResetCode(user.DisplayName, code, resetUrl, _auth.ResetCodeLifetimeMinutes);
        await emailSender.SendAsync(new EmailMessage(
            To: user.Email!,
            Subject: resetBody.Subject,
            HtmlBody: resetBody.Html,
            PlainTextBody: resetBody.Text), ct);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim().ToLowerInvariant());
        var now = DateTime.UtcNow;

        // One message covers every failure — unknown account, missing/expired code, wrong code — so
        // this endpoint cannot be used to learn which addresses have accounts.
        var invalid = ApiException.BadRequest(
            "INVALID_RESET_CODE", "That code is invalid or has expired. Request a new one.");

        if (user is null)
        {
            throw invalid;
        }

        var record = await db.PasswordResetCodes
            .Where(c => c.UserId == user.Id && c.ConsumedAtUtc == null && c.ExpiresAtUtc > now)
            .OrderByDescending(c => c.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (record is null)
        {
            throw invalid;
        }

        if (record.Attempts >= _auth.ResetCodeMaxAttempts)
        {
            record.ConsumedAtUtc = now;
            await db.SaveChangesAsync(ct);
            throw ApiException.BadRequest("INVALID_RESET_CODE", "Too many incorrect attempts. Request a new code.");
        }

        if (!ResetCodeFactory.Verify(request.Code, record.CodeHash))
        {
            record.Attempts++;
            await db.SaveChangesAsync(ct);
            throw invalid;
        }

        // The code is the factor we just verified. The Identity token is generated here only
        // because ResetPasswordAsync insists on one, and is spent immediately.
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
        {
            throw ApiException.BadRequest("RESET_PASSWORD_FAILED", FormatIdentityErrors(result));
        }

        record.ConsumedAtUtc = now;
        await db.SaveChangesAsync(ct);
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

    /// <summary>
    /// Revokes every live refresh token for a user.
    /// </summary>
    /// <remarks>
    /// A set-based UPDATE rather than a load-and-mutate loop. This runs on the reuse path, where
    /// the rows being revoked are exactly the ones a racing request is touching; a tracked save
    /// would throw a concurrency error of its own and mask the reuse it is meant to record. The
    /// direct update is idempotent and cannot lose that race.
    /// </remarks>
    private async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, now), ct);
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
        BuyerId = user.BuyerId,
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


}