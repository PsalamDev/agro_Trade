using System.Net;
using System.Net.Http.Json;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Tests.Integration;

/// <summary>
/// The destination-requirements helper (docs/02-data-model.md — Phase 4): a curated, pure answer to
/// "what documents will this route need?" from an origin/destination pair. Served anonymously, like
/// the price series it sits beside.
/// </summary>
public sealed class DestinationRequirementsFlowTests : IClassFixture<TestingWebAppFactory>
{
    private readonly HttpClient _client;

    public DestinationRequirementsFlowTests(TestingWebAppFactory factory)
    {
        factory.EnsureSchema();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ACorridorWithAnAgriculturalOriginAndEuropeanDestinationListsTheCoreSet()
    {
        var response = await _client.GetAsync(
            "/api/v1/marketdata/destination-requirements?origin=NG&destination=GB");
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<RequirementsMirror>(JsonOptions))!;

        Assert.Equal("NG", result.Origin);
        Assert.Equal("GB", result.Destination);
        Assert.False(result.Domestic);

        var byDocument = result.Documents.ToDictionary(d => d.Document);

        // The three documents every cross-border consignment needs.
        Assert.True(byDocument[DocumentType.CommercialInvoice].Mandatory);
        Assert.True(byDocument[DocumentType.PackingList].Mandatory);
        Assert.True(byDocument[DocumentType.CertificateOfOrigin].Mandatory);

        // Produce leaving an agricultural origin needs plant-health certification at origin.
        Assert.True(byDocument[DocumentType.PhytosanitaryCertificate].Mandatory);

        // A European border adds the inspection suggestion and a corridor note.
        Assert.Contains(DocumentType.InspectionCertificate, byDocument.Keys);
        Assert.NotNull(result.Notes);
    }

    [Fact]
    public async Task AGulfDestinationRequiresAnImportPermit()
    {
        var response = await _client.GetAsync(
            "/api/v1/marketdata/destination-requirements?origin=KE&destination=AE");
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<RequirementsMirror>(JsonOptions))!;

        var permit = Assert.Single(result.Documents, d => d.Document == DocumentType.ImportPermit);
        Assert.True(permit.Mandatory);
        Assert.False(result.Domestic);
    }

    [Fact]
    public async Task ADomesticRouteNeedsNoCustomsDocuments()
    {
        // Case-insensitively the same country: not an export, so nothing to clear.
        var response = await _client.GetAsync(
            "/api/v1/marketdata/destination-requirements?origin=NG&destination=ng");
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<RequirementsMirror>(JsonOptions))!;

        Assert.True(result.Domestic);
        Assert.Empty(result.Documents);
    }

    [Fact]
    public async Task ACodeThatIsNotACountryIsRefused()
    {
        var response = await _client.GetAsync(
            "/api/v1/marketdata/destination-requirements?origin=NG&destination=Z");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("INVALID_COUNTRY", doc.RootElement.GetProperty("code").GetString());
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    public sealed class RequirementsMirror
    {
        public string Origin { get; init; } = "";
        public string Destination { get; init; } = "";
        public bool Domestic { get; init; }
        public List<RequirementMirror> Documents { get; init; } = [];
        public string? Notes { get; init; }
    }

    public sealed class RequirementMirror
    {
        public DocumentType Document { get; init; }
        public bool Mandatory { get; init; }
        public string Reason { get; init; } = "";
    }
}
