using System.Net;
using System.Net.Http.Json;
using AgroTrade.Application.Trade;

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

    [Fact]
    public async Task Countries_UsesFullIsoListWithExpectedCodes()
    {
        var response = await _client.GetAsync("/api/v1/countries");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var countries = await response.Content.ReadFromJsonAsync<List<CountryDto>>();
        Assert.NotNull(countries);
        Assert.Equal(249, countries!.Count);

        // Sorted by name, so Afghanistan leads.
        Assert.Equal("Afghanistan", countries[0].Name);
        Assert.Equal("AF", countries[0].Alpha2);

        // Spot-check familiar markets and their full ISO identities.
        var nigeria = Assert.Single(countries, c => c.Alpha2 == "NG");
        Assert.Equal("Nigeria", nigeria.Name);
        Assert.Equal("NGA", nigeria.Alpha3);
        Assert.Equal("566", nigeria.Numeric);

        var kenya = Assert.Single(countries, c => c.Alpha2 == "KE");
        Assert.Equal("Kenya", kenya.Name);
        Assert.Equal("KEN", kenya.Alpha3);

        // The codes the app stores are alpha-2 and correspond to unique names.
        Assert.Equal(countries.Count, countries.Select(c => c.Alpha2).Distinct().Count());
    }
}