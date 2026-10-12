using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace AgroTrade.Tests.Integration;

/// <summary>
/// The public category tree is served through the cache (see <c>CatalogService</c>). An
/// administrator who writes a category must see it immediately, not after a TTL — this drives that
/// path end to end: read (fills the cache), write (must evict it), read again (must reflect it).
/// </summary>
/// <remarks>
/// The category is created and then removed again. The test database is shared and persistent, and
/// other fixtures pick the alphabetically-first leaf category for their own fixtures, so leaving a
/// new first-sorting category behind would change what they test.
/// </remarks>
public sealed class CategoryCacheFlowTests : IClassFixture<TestingWebAppFactory>
{
    private readonly HttpClient _client;

    public CategoryCacheFlowTests(TestingWebAppFactory factory)
    {
        factory.EnsureSchema();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CategoryTree_IsServedToAnonymousCallers()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/v1/categories")).StatusCode);
    }

    [Fact]
    public async Task CategoryTree_ReflectsAnAdministratorWrite_Immediately()
    {
        var admin = await LoginAsync(TestingWebAppFactory.AdminEmail, TestingWebAppFactory.AdminPassword);
        var marker = Guid.NewGuid().ToString("N");
        var slug = $"cache-{marker}";
        // Deliberately short: other fixtures cap RFQ titles at 80 characters.
        var name = $"cx{marker[..8]}";
        var createdId = (Guid?)null;

        try
        {
            // A first read caches the current tree.
            Assert.DoesNotContain(slug, await RootSlugsAsync());

            var request = AuthedRequest("/api/v1/admin/categories", admin, HttpMethod.Post,
                JsonContent.Create(new { name, slug, sortOrder = 0 }));
            var created = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            // The write must have evicted the cached tree, so the new category shows up at once.
            createdId = (await created.Content.ReadFromJsonAsync<CategoryMirror>())!.Id;
            Assert.Contains(slug, await RootSlugsAsync());
        }
        finally
        {
            if (createdId is { } id)
            {
                var delete = AuthedRequest($"/api/v1/admin/categories/{id}", admin, HttpMethod.Delete, content: null);
                (await _client.SendAsync(delete)).EnsureSuccessStatusCode();
            }
        }

        // Removing it is another write, and it too must be reflected without waiting for the TTL.
        Assert.DoesNotContain(slug, await RootSlugsAsync());
    }

    private async Task<HashSet<string>> RootSlugsAsync()
    {
        var response = await _client.GetAsync("/api/v1/categories");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.EnumerateArray()
            .Select(node => node.GetProperty("slug").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static HttpRequestMessage AuthedRequest(string url, string token, HttpMethod method, HttpContent? content)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private sealed class CategoryMirror
    {
        public Guid Id { get; init; }
    }
}
