using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

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
            roles = new[] { "Supplier", "Exporter" },
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var registered = await register.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(registered);
        Assert.NotEmpty(registered.AccessToken);
        Assert.NotEmpty(registered.RefreshToken);
        Assert.Equal(["Supplier", "Exporter"], registered.User.Roles);
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