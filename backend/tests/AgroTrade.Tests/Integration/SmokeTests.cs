using System.Net;

namespace AgroTrade.Tests.Integration;

/// <summary>
/// Database-free checks: liveness, unauthenticated protection, CORS headers.
/// </summary>
public sealed class SmokeTests : IClassFixture<TestingWebAppFactory>
{
    private readonly HttpClient _client;

    public SmokeTests(TestingWebAppFactory factory)
    {
        // Note: EnsureSchema intentionally NOT called — these tests must not need SQL Server.
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthLogin_WithMissingBody_Returns400()
    {
        var response = await _client.PostAsync(
            "/api/v1/auth/login",
            new StringContent(string.Empty, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}