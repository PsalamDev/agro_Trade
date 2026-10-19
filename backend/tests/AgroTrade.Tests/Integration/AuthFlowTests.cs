using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgroTrade.Application.Common;
using AgroTrade.Domain.Identity;
using AgroTrade.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgroTrade.Tests.Integration;

/// <summary>
/// End-to-end auth coverage against a real SQL Server database (AgroTrade_Tests):
/// register, login, token rotation + reuse detection, profile, admin role.
/// </summary>
public sealed class AuthFlowTests : IClassFixture<TestingWebAppFactory>
{
    private readonly TestingWebAppFactory _factory;
    private readonly HttpClient _client;

    public AuthFlowTests(TestingWebAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureSchema();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task FullLifecycle_Register_Login_Refresh_Reuse_Logout()
    {
        var email = $"trader_{Guid.NewGuid():N}@tests.agrotrade.local";

        // Register
        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "StrongPass123",
            displayName = "Mama Farms",
            homeCountry = "KE",
            defaultCurrency = "KES",
            roles = new[] { "Buyer" }, // buyer role only
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var registered = await register.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(registered);
        Assert.NotEmpty(registered.AccessToken);
        Assert.NotEmpty(registered.RefreshToken);
        Assert.Equal(["Buyer"], registered.User.Roles);
        Assert.True(registered.User.IsEmailVerified); // AutoConfirmEmail in test config

        // Duplicate registration is rejected
        var duplicate = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "StrongPass123",
            displayName = "Mama Farms",
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        var duplicateProblem = await duplicate.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Equal("REGISTRATION_FAILED", duplicateProblem?.Code);

        // Login
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "StrongPass123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loggedIn = await login.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(loggedIn);

        // /auth/me + /me with the access token
        using var authorized = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        authorized.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loggedIn.AccessToken);
        var me = await _client.SendAsync(authorized);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var meDto = await me.Content.ReadFromJsonAsync<AuthUserDto>();
        Assert.Equal(email, meDto?.Email);
        Assert.Equal("Mama Farms", meDto?.DisplayName);
        Assert.Equal("KES", meDto?.DefaultCurrency);

        // Wrong password
        var badLogin = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "WrongPass1" });
        Assert.Equal(HttpStatusCode.Unauthorized, badLogin.StatusCode);

        // Refresh rotation
        var refresh = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = loggedIn.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var rotated = await refresh.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(rotated);
        Assert.NotEqual(loggedIn.AccessToken, rotated.AccessToken);
        Assert.NotEqual(loggedIn.RefreshToken, rotated.RefreshToken);

        // Replay of the old refresh token → reuse detected, whole family revoked
        var replay = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = loggedIn.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        var replayProblem = await replay.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Equal("REFRESH_TOKEN_REUSED", replayProblem?.Code);

        // The successor token was revoked by the family revoke too
        var familyRevoked = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = rotated.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, familyRevoked.StatusCode);

        // Logout with a fresh pair
        var relogin = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "StrongPass123" });
        var fresh = await relogin.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(fresh);
        using var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout")
        {
            Content = JsonContent.Create(new { refreshToken = fresh.RefreshToken }),
        };
        logoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fresh.AccessToken);
        var logout = await _client.SendAsync(logoutReq);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var afterLogout = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = fresh.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task BuyerId_IsGeneratedAtRegistration_AndStableAcrossLoginAndMe()
    {
        var email = $"buyerid_{Guid.NewGuid():N}@tests.agrotrade.local";
        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "StrongPass123",
            displayName = "Account Number Buyer",
            homeCountry = "NG",
            defaultCurrency = "USD",
            roles = new[] { "Buyer" },
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var registered = await register.Content.ReadFromJsonAsync<AuthResponseDto>();
        var buyerId = registered!.User.BuyerId;
        Assert.NotNull(buyerId);
        Assert.Matches(@"^BUY-ID-AG-\d{6}$", buyerId!);

        // The number is an account identifier, so it is the same on every later response rather
        // than a per-session value.
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "StrongPass123" });
        var loggedIn = await login.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.Equal(buyerId, loggedIn!.User.BuyerId);

        using var authorized = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        authorized.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loggedIn.AccessToken);
        var me = await _client.SendAsync(authorized);
        var meDto = await me.Content.ReadFromJsonAsync<AuthUserDto>();
        Assert.Equal(buyerId, meDto?.BuyerId);
    }

    [Fact]
    public async Task ConcurrentRegistrations_ReceiveDistinctBuyerIds()
    {
        // Two registrations racing for the same number must not both be handed it: the allocator
        // serialises the read-then-write, so the loser waits and re-reads rather than computing the
        // same maximum.
        var first = RegisterBuyerAsync($"raceid_a_{Guid.NewGuid():N}@tests.agrotrade.local");
        var second = RegisterBuyerAsync($"raceid_b_{Guid.NewGuid():N}@tests.agrotrade.local");
        var results = await Task.WhenAll(first, second);

        Assert.All(results, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var ids = results.Select(r => r.BuyerId).ToList();
        Assert.All(ids, id => Assert.Matches(@"^BUY-ID-AG-\d{6}$", id!));
        Assert.Equal(2, ids.Distinct().Count());
    }

    private async Task<(HttpStatusCode StatusCode, string? BuyerId)> RegisterBuyerAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "StrongPass123",
            displayName = "Concurrent Buyer",
            homeCountry = "NG",
            defaultCurrency = "USD",
            roles = new[] { "Buyer" },
        });
        var dto = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        return (response.StatusCode, dto?.User.BuyerId);
    }

    [Fact]
    public async Task ConcurrentRotations_CannotBothMintALiveSuccessor()
    {
        var email = $"race_{Guid.NewGuid():N}@tests.agrotrade.local";
        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "StrongPass123",
            displayName = "Race User",
            homeCountry = "NG",
            defaultCurrency = "USD",
            roles = new[] { "Buyer" },
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var auth = await register.Content.ReadFromJsonAsync<AuthResponseDto>();
        var userId = auth!.User.Id;

        using var scopeA = _factory.Services.CreateScope();
        using var scopeB = _factory.Services.CreateScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<AgroTradeDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<AgroTradeDbContext>();

        // Both requests read the same token at its original version, exactly as two simultaneous
        // refreshes would. Either may win the write; only one may rotate it.
        var tokenA = await dbA.RefreshTokens.SingleAsync(t => t.UserId == userId);
        var tokenB = await dbB.RefreshTokens.SingleAsync(t => t.UserId == userId);

        tokenA.RevokedAtUtc = DateTime.UtcNow;
        tokenA.ReplacedByTokenHash = $"successor-a-{Guid.NewGuid():N}";
        dbA.RefreshTokens.Add(NewToken(userId, tokenA.ReplacedByTokenHash!));
        await dbA.SaveChangesAsync();

        tokenB.RevokedAtUtc = DateTime.UtcNow;
        tokenB.ReplacedByTokenHash = $"successor-b-{Guid.NewGuid():N}";
        dbB.RefreshTokens.Add(NewToken(userId, tokenB.ReplacedByTokenHash!));

        // The stale second rotation loses; its successor insert rolls back with its failed update.
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync());

        using var verifyScope = _factory.Services.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<AgroTradeDbContext>();
        var live = await verify.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ToListAsync();

        Assert.Single(live);
    }

    private static RefreshToken NewToken(Guid userId, string hash) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TokenHash = hash,
        ExpiresAtUtc = DateTime.UtcNow.AddDays(30),
        CreatedAtUtc = DateTime.UtcNow,
    };

    [Fact]
    public async Task ValidationFailure_ReturnsProblemDetailsWithFieldErrors()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = "not-an-email",
            password = "weak",
            displayName = "",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Equal("VALIDATION_ERROR", problem?.Code);
        Assert.NotNull(problem?.FieldErrors);
        Assert.True(problem!.FieldErrors!.Count > 0);
    }

    [Fact]
    public async Task Administrator_LogsIn_WithAdministratorRole()
    {
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = TestingWebAppFactory.AdminEmail,
            password = TestingWebAppFactory.AdminPassword,
        });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var dto = await login.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.Contains("Administrator", dto!.User.Roles);
    }

    [Fact]
    public async Task PasswordReset_EmailedCode_ChangesPassword_AndIsSingleUse()
    {
        var email = $"reset_{Guid.NewGuid():N}@tests.agrotrade.local";
        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "StrongPass123",
            displayName = "Reset User",
            homeCountry = "NG",
            defaultCurrency = "USD",
            roles = new[] { "Buyer" },
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var recorder = _factory.Services.GetRequiredService<RecordingEmailSender>();

        // Ask for a code. The response is a bare 204 whether or not the address exists.
        var forgot = await _client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.NoContent, forgot.StatusCode);

        var message = recorder.LastTo(email);
        Assert.NotNull(message);
        var code = ExtractResetCode(message!);
        Assert.Matches(@"^\d{6}$", code);

        // The same email also carries a one-click link with the code, so a user can skip typing it.
        Assert.Contains("reset-password", message!.PlainTextBody!);
        Assert.Contains($"code={code}", message!.PlainTextBody!);

        // A wrong code is refused and leaves the password untouched.
        var wrongCode = code == "000000" ? "111111" : "000000";
        var wrong = await _client.PostAsJsonAsync("/api/v1/auth/reset-password",
            new { email, code = wrongCode, newPassword = "NewStrong123" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        var wrongProblem = await wrong.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Equal("INVALID_RESET_CODE", wrongProblem?.Code);

        // The real code resets the password.
        var reset = await _client.PostAsJsonAsync("/api/v1/auth/reset-password",
            new { email, code, newPassword = "NewStrong123" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var oldLogin = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "StrongPass123" });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        var newLogin = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "NewStrong123" });
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);

        // The code is single-use: replaying it after a successful reset is refused.
        var reuse = await _client.PostAsJsonAsync("/api/v1/auth/reset-password",
            new { email, code, newPassword = "Another123" });
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
    }

    [Fact]
    public async Task PasswordReset_VerifyStep_ConfirmsCodeWithoutSpendingIt()
    {
        var email = $"verify_{Guid.NewGuid():N}@tests.agrotrade.local";
        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "StrongPass123",
            displayName = "Verify User",
            homeCountry = "NG",
            defaultCurrency = "USD",
            roles = new[] { "Buyer" },
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var recorder = _factory.Services.GetRequiredService<RecordingEmailSender>();
        await _client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email });
        var code = ExtractResetCode(recorder.LastTo(email)!);

        // A wrong code fails verification the same way a wrong reset does (no enumeration).
        var wrongCode = code == "000000" ? "111111" : "000000";
        var wrong = await _client.PostAsJsonAsync("/api/v1/auth/verify-reset-code",
            new { email, code = wrongCode });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        var wrongProblem = await wrong.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Equal("INVALID_RESET_CODE", wrongProblem?.Code);

        // The real code verifies...
        var verify = await _client.PostAsJsonAsync("/api/v1/auth/verify-reset-code", new { email, code });
        Assert.Equal(HttpStatusCode.NoContent, verify.StatusCode);

        // ...and verifying is idempotent: it does not spend the code the reset still needs.
        var verifyAgain = await _client.PostAsJsonAsync("/api/v1/auth/verify-reset-code", new { email, code });
        Assert.Equal(HttpStatusCode.NoContent, verifyAgain.StatusCode);

        var reset = await _client.PostAsJsonAsync("/api/v1/auth/reset-password",
            new { email, code, newPassword = "NewStrong123" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var newLogin = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "NewStrong123" });
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);

        // Once the reset spends it, the code no longer verifies.
        var afterReset = await _client.PostAsJsonAsync("/api/v1/auth/verify-reset-code", new { email, code });
        Assert.Equal(HttpStatusCode.BadRequest, afterReset.StatusCode);
    }

    [Fact]
    public async Task PasswordReset_UnknownEmail_ReturnsNoContent_ToAvoidEnumeration()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/forgot-password",
            new { email = $"nobody_{Guid.NewGuid():N}@tests.agrotrade.local" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static string ExtractResetCode(EmailMessage message)
    {
        // The plain-text twin is the stable place to read the code; the HTML wraps it in markup.
        var body = message.PlainTextBody ?? message.HtmlBody;
        var match = Regex.Match(body, @"Your code:\s*(\d{6})");
        Assert.True(match.Success, "The reset email did not contain a 6-digit code.");
        return match.Groups[1].Value;
    }

    // ---- DTO mirrors (system-text-json friendly) ----

    public sealed class AuthResponseDto
    {
        public string AccessToken { get; init; } = "";
        public string RefreshToken { get; init; } = "";
        public AuthUserDto User { get; init; } = new();
    }

    public sealed class AuthUserDto
    {
        public Guid Id { get; init; }
        public string Email { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string? HomeCountry { get; init; }
        public string DefaultCurrency { get; init; } = "";
        public string? BuyerId { get; init; }
        public bool IsEmailVerified { get; init; }
        public bool IsActive { get; init; }
        public List<string> Roles { get; init; } = [];
    }

    public sealed class ProblemDetailsDto
    {
        public string? Code { get; init; }
        public Dictionary<string, string[]>? FieldErrors { get; init; }
    }
}