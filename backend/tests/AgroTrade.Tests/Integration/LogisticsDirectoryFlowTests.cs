using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AgroTrade.Domain.Enums;
using AgroTrade.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AgroTrade.Tests.Integration;

/// <summary>
/// The logistics-provider directory and bookings that link a provider to an order (Phase 4 exit
/// criterion: an LSP can be found and linked to orders).
///
/// <para>
/// Three claims carry the weight. First, the directory is a front door: it is readable without an
/// account, and it must not leak the contact details that make a provider worth spamming. Second,
/// a booking belongs to the order, so only a party to that order may raise one — a stranger gets a
/// 404, because the order's existence is itself private. Third, the provider is the only one who
/// can answer a request, and the requester the only one who can withdraw or close it.
/// </para>
/// </summary>
public sealed class LogisticsDirectoryFlowTests : IClassFixture<TestingWebAppFactory>
{
    private const string Password = "StrongPass123";

    private readonly TestingWebAppFactory _factory;
    private readonly HttpClient _client;

    public LogisticsDirectoryFlowTests(TestingWebAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureSchema();
        _client = factory.CreateClient();
    }

    // ---- The directory ----

    [Fact]
    public async Task DirectoryIsPublicAndListsActiveProvidersWithoutContactDetails()
    {
        var lspToken = await CreateLspUserAsync($"lsp_dir_{Tag()}");
        var name = $"Directory Freight {Guid.NewGuid():N}";
        await UpsertProviderAsync(lspToken, name,
            coverage: ["NG"],
            modes: [ShipmentMode.Road, ShipmentMode.Sea],
            services: [LogisticsService.Warehousing],
            contactEmail: "ops@directory.example");

        // Anonymous search. The directory is a front door; requiring an account to see who is
        // listed would defeat the point of listing them.
        var response = await _client.GetAsync($"/api/v1/logistics/providers?q={Uri.EscapeDataString(name)}");
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync();
        var page = System.Text.Json.JsonSerializer.Deserialize<PagedMirror<ProviderCardMirror>>(
            raw, JsonOptions)!;

        var card = Assert.Single(page.Items);
        Assert.Equal(name, card.CompanyName);
        Assert.Contains(ShipmentMode.Sea, card.Modes);
        Assert.Contains(LogisticsService.Warehousing, card.Services);
        Assert.Contains("NG", card.Coverage);
        Assert.False(card.IsVerified);

        // The card shape must not carry contact details — that is what having an account buys.
        Assert.DoesNotContain("contactEmail", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("contactPhone", raw, StringComparison.OrdinalIgnoreCase);

        // Detail is authenticated, so an anonymous reader is turned away before anything leaks.
        var anonDetail = await _client.GetAsync($"/api/v1/logistics/providers/{card.Id}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonDetail.StatusCode);
    }

    [Fact]
    public async Task DirectoryFiltersByModeAndRefusesAnUnknownOne()
    {
        var lspToken = await CreateLspUserAsync($"lsp_filter_{Tag()}");
        var name = $"Filter Freight {Guid.NewGuid():N}";
        await UpsertProviderAsync(lspToken, name,
            coverage: ["NG"], modes: [ShipmentMode.Sea], services: []);

        var sea = await _client.GetAsync(
            $"/api/v1/logistics/providers?q={Uri.EscapeDataString(name)}&mode={(int)ShipmentMode.Sea}");
        var seaPage = await sea.Content.ReadFromJsonAsync<PagedMirror<ProviderCardMirror>>(JsonOptions);
        Assert.Single(seaPage!.Items);

        // The provider does not handle air, so the same search finds nothing.
        var air = await _client.GetAsync(
            $"/api/v1/logistics/providers?q={Uri.EscapeDataString(name)}&mode={(int)ShipmentMode.Air}");
        var airPage = await air.Content.ReadFromJsonAsync<PagedMirror<ProviderCardMirror>>(JsonOptions);
        Assert.Empty(airPage!.Items);

        // An unrecognised filter is a clear 400, not a silently empty result.
        var bad = await _client.GetAsync("/api/v1/logistics/providers?mode=99");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("INVALID_FILTER", await ProblemCodeAsync(bad));
    }

    // ---- The provider profile ----

    [Fact]
    public async Task ProviderProfileRoundTripsAndCoverageIsReplaced()
    {
        var lspToken = await CreateLspUserAsync($"lsp_profile_{Tag()}");
        var name = $"Roundtrip Freight {Guid.NewGuid():N}";

        var created = await UpsertProviderAsync(lspToken, name,
            coverage: ["NG", "GH"],
            modes: [ShipmentMode.Road],
            services: [LogisticsService.Trucking],
            contactEmail: "hello@roundtrip.example");
        Assert.True(created.IsMine);
        Assert.True(created.IsActive);

        var mine = await GetAuthenticatedAsync("/api/v1/logistics/providers/mine", lspToken);
        mine.EnsureSuccessStatusCode();
        var detail = (await mine.Content.ReadFromJsonAsync<ProviderDetailMirror>(JsonOptions))!;
        Assert.Equal(name, detail.CompanyName);
        Assert.Contains("GH", detail.Coverage);
        Assert.Equal("hello@roundtrip.example", detail.ContactEmail);

        // A replace, not a merge: the coverage the client sends is the coverage that ends up stored.
        var updated = await UpsertProviderAsync(lspToken, name,
            coverage: ["NG"], modes: [ShipmentMode.Road, ShipmentMode.Air],
            services: [LogisticsService.Trucking, LogisticsService.LastMile]);
        Assert.Equal(["NG"], updated.Coverage);
        Assert.Contains(ShipmentMode.Air, updated.Modes);
        Assert.Contains(LogisticsService.LastMile, updated.Services);
    }

    [Fact]
    public async Task ProviderProfileRequiresTheLogisticsRole()
    {
        // A buyer has an account but is not a logistics provider, so the profile is off limits by
        // role rather than by a check inside the service.
        var buyerToken = await RegisterAndLoginAsync($"lsp_roles_{Tag()}");

        var response = await PutAuthenticatedAsync("/api/v1/logistics/providers/mine", buyerToken, new
        {
            companyName = "Not A Provider",
            homeCountry = "NG",
            modes = new[] { (int)ShipmentMode.Road },
            services = Array.Empty<int>(),
            coverage = new[] { "NG" },
            isActive = true,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnInactiveProviderIsHiddenAndCannotBeBooked()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync($"lsp_inactive_{Tag()}");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var lspToken = await CreateLspUserAsync($"lsp_inactive_owner_{Tag()}");
        var name = $"Withdrawn Freight {Guid.NewGuid():N}";
        var provider = await UpsertProviderAsync(lspToken, name,
            coverage: ["NG"], modes: [ShipmentMode.Road], services: []);

        // The owner withdraws the profile.
        provider = await UpsertProviderAsync(lspToken, name,
            coverage: ["NG"], modes: [ShipmentMode.Road], services: [], isActive: false);
        Assert.False(provider.IsActive);

        var search = await _client.GetAsync($"/api/v1/logistics/providers?q={Uri.EscapeDataString(name)}");
        var page = await search.Content.ReadFromJsonAsync<PagedMirror<ProviderCardMirror>>(JsonOptions);
        Assert.Empty(page!.Items);

        // The owner can still read it — that is how they would re-activate it.
        var mine = await GetAuthenticatedAsync("/api/v1/logistics/providers/mine", lspToken);
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);

        // But a stranger's detail read is not found, and neither is a booking against it.
        var detail = await GetAuthenticatedAsync($"/api/v1/logistics/providers/{provider.Id}", buyerToken);
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        Assert.Equal("PROVIDER_NOT_FOUND", await ProblemCodeAsync(detail));

        var booking = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/logistics-bookings", buyerToken,
            new { providerId = provider.Id, mode = (int)ShipmentMode.Road });
        Assert.Equal(HttpStatusCode.NotFound, booking.StatusCode);
        Assert.Equal("PROVIDER_NOT_FOUND", await ProblemCodeAsync(booking));
    }

    // ---- Booking an order ----

    [Fact]
    public async Task BuyerBooksAProviderAndTheProviderAccepts()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync($"lsp_book_{Tag()}");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var lspToken = await CreateLspUserAsync($"lsp_book_provider_{Tag()}");
        var provider = await UpsertProviderAsync(lspToken, $"Booking Freight {Guid.NewGuid():N}",
            coverage: ["NG"], modes: [ShipmentMode.Sea], services: [LogisticsService.FreightForwarding]);

        var booking = await RequestBookingAsync(orderId, buyerToken, provider.Id, ShipmentMode.Sea, "Collect Thursday");
        Assert.Equal(orderId, booking.OrderId);
        Assert.Equal(provider.Id, booking.ProviderId);
        Assert.Equal(LogisticsBookingStatus.Requested, booking.Status);
        Assert.True(booking.IsRequester);
        Assert.False(booking.IsProvider);
        Assert.StartsWith("LSP-", booking.Reference);
        Assert.Equal("Collect Thursday", booking.Notes);

        // The provider is told, and only the provider. A fresh user, so exactly one notification.
        Assert.Equal(1, await NotificationCountAsync(lspToken));

        // The provider sees the request in their own list, flagged the other way around.
        var mine = await GetAuthenticatedAsync("/api/v1/logistics/bookings", lspToken);
        mine.EnsureSuccessStatusCode();
        var minePage = (await mine.Content.ReadFromJsonAsync<PagedMirror<BookingMirror>>(JsonOptions))!;
        var requested = Assert.Single(minePage.Items);
        Assert.True(requested.IsProvider);
        Assert.False(requested.IsRequester);
        Assert.Equal(orderId, requested.OrderId);

        // The provider accepts.
        var respond = await PostAuthenticatedAsync(
            $"/api/v1/logistics/bookings/{booking.Id}/respond", lspToken,
            new { accept = true, notes = "Truck confirmed for Thursday." });
        respond.EnsureSuccessStatusCode();
        var accepted = (await respond.Content.ReadFromJsonAsync<BookingMirror>(JsonOptions))!;
        Assert.Equal(LogisticsBookingStatus.Accepted, accepted.Status);
        Assert.Equal("Truck confirmed for Thursday.", accepted.ResponseNotes);
        Assert.NotNull(accepted.DecidedAtUtc);

        // And the order now carries the link, which is the exit criterion made concrete.
        var forOrder = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/logistics-bookings", buyerToken);
        forOrder.EnsureSuccessStatusCode();
        var bookings = (await forOrder.Content.ReadFromJsonAsync<List<BookingMirror>>(JsonOptions))!;
        var onOrder = Assert.Single(bookings);
        Assert.Equal(booking.Id, onOrder.Id);
        Assert.Equal(LogisticsBookingStatus.Accepted, onOrder.Status);
        Assert.Equal(accepted.ProviderName, onOrder.ProviderName);
    }

    [Fact]
    public async Task TheSellerCanAlsoBookTheSameOrder()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync($"lsp_seller_{Tag()}");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var lspToken = await CreateLspUserAsync($"lsp_seller_provider_{Tag()}");
        var provider = await UpsertProviderAsync(lspToken, $"Seller Freight {Guid.NewGuid():N}",
            coverage: ["NG"], modes: [ShipmentMode.Road], services: []);

        // Either party to the order may arrange carriage, not only the buyer.
        var booking = await RequestBookingAsync(orderId, sellerToken, provider.Id, ShipmentMode.Road);
        Assert.Equal(LogisticsBookingStatus.Requested, booking.Status);
    }

    [Fact]
    public async Task AStrangerCannotBookAnOrderTheyAreNotPartOf()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync($"lsp_stranger_{Tag()}");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var strangerToken = await RegisterAndLoginAsync($"lsp_stranger_user_{Tag()}");

        var lspToken = await CreateLspUserAsync($"lsp_stranger_provider_{Tag()}");
        var provider = await UpsertProviderAsync(lspToken, $"Stranger Freight {Guid.NewGuid():N}",
            coverage: ["NG"], modes: [ShipmentMode.Road], services: []);

        var response = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/logistics-bookings", strangerToken,
            new { providerId = provider.Id, mode = (int)ShipmentMode.Road });

        // 404, not 403: the order's existence is itself private.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("ORDER_NOT_FOUND", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task ASecondOpenRequestToTheSameProviderIsRefused()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync($"lsp_dup_{Tag()}");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var lspToken = await CreateLspUserAsync($"lsp_dup_provider_{Tag()}");
        var provider = await UpsertProviderAsync(lspToken, $"Duplicate Freight {Guid.NewGuid():N}",
            coverage: ["NG"], modes: [ShipmentMode.Sea], services: []);

        await RequestBookingAsync(orderId, buyerToken, provider.Id, ShipmentMode.Sea);

        var second = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/logistics-bookings", buyerToken,
            new { providerId = provider.Id, mode = (int)ShipmentMode.Sea });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("BOOKING_EXISTS", await ProblemCodeAsync(second));
    }

    [Fact]
    public async Task WithdrawingRequiresTheRequesterAndClosesTheRequest()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync($"lsp_cancel_{Tag()}");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var lspToken = await CreateLspUserAsync($"lsp_cancel_provider_{Tag()}");
        var provider = await UpsertProviderAsync(lspToken, $"Cancel Freight {Guid.NewGuid():N}",
            coverage: ["NG"], modes: [ShipmentMode.Sea], services: []);
        var booking = await RequestBookingAsync(orderId, buyerToken, provider.Id, ShipmentMode.Sea);

        // The provider cannot withdraw a request that was put to them.
        var byProvider = await PostAuthenticatedAsync(
            $"/api/v1/logistics/bookings/{booking.Id}/cancel", lspToken, new { });
        Assert.Equal(HttpStatusCode.Forbidden, byProvider.StatusCode);
        Assert.Equal("NOT_REQUESTER", await ProblemCodeAsync(byProvider));

        // The requester can.
        var byRequester = await PostAuthenticatedAsync(
            $"/api/v1/logistics/bookings/{booking.Id}/cancel", buyerToken, new { });
        byRequester.EnsureSuccessStatusCode();
        var cancelled = (await byRequester.Content.ReadFromJsonAsync<BookingMirror>(JsonOptions))!;
        Assert.Equal(LogisticsBookingStatus.Cancelled, cancelled.Status);

        // And once withdrawn, the provider can no longer answer it.
        var respond = await PostAuthenticatedAsync(
            $"/api/v1/logistics/bookings/{booking.Id}/respond", lspToken, new { accept = true });
        Assert.Equal(HttpStatusCode.Conflict, respond.StatusCode);
        Assert.Equal("BOOKING_CLOSED", await ProblemCodeAsync(respond));
    }

    [Fact]
    public async Task ADeclinedBookingFreesTheProviderForANewRequest()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync($"lsp_decline_{Tag()}");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var lspToken = await CreateLspUserAsync($"lsp_decline_provider_{Tag()}");
        var provider = await UpsertProviderAsync(lspToken, $"Decline Freight {Guid.NewGuid():N}",
            coverage: ["NG"], modes: [ShipmentMode.Sea], services: []);
        var first = await RequestBookingAsync(orderId, buyerToken, provider.Id, ShipmentMode.Sea);

        var decline = await PostAuthenticatedAsync(
            $"/api/v1/logistics/bookings/{first.Id}/respond", lspToken, new { accept = false });
        decline.EnsureSuccessStatusCode();
        var declined = (await decline.Content.ReadFromJsonAsync<BookingMirror>(JsonOptions))!;
        Assert.Equal(LogisticsBookingStatus.Declined, declined.Status);

        // A declined request is closed, so it no longer blocks the same provider.
        var second = await RequestBookingAsync(orderId, buyerToken, provider.Id, ShipmentMode.Sea);
        Assert.Equal(LogisticsBookingStatus.Requested, second.Status);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task CompletionRequiresAnAcceptedBooking()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync($"lsp_complete_{Tag()}");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var lspToken = await CreateLspUserAsync($"lsp_complete_provider_{Tag()}");
        var provider = await UpsertProviderAsync(lspToken, $"Complete Freight {Guid.NewGuid():N}",
            coverage: ["NG"], modes: [ShipmentMode.Road], services: []);
        var booking = await RequestBookingAsync(orderId, buyerToken, provider.Id, ShipmentMode.Road);

        // An open request cannot be closed out.
        var tooEarly = await PostAuthenticatedAsync(
            $"/api/v1/logistics/bookings/{booking.Id}/complete", buyerToken, new { });
        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);
        Assert.Equal("BOOKING_NOT_ACCEPTED", await ProblemCodeAsync(tooEarly));

        await PostAuthenticatedAsync(
            $"/api/v1/logistics/bookings/{booking.Id}/respond", lspToken, new { accept = true });

        var done = await PostAuthenticatedAsync(
            $"/api/v1/logistics/bookings/{booking.Id}/complete", buyerToken, new { });
        done.EnsureSuccessStatusCode();
        var completed = (await done.Content.ReadFromJsonAsync<BookingMirror>(JsonOptions))!;
        Assert.Equal(LogisticsBookingStatus.Completed, completed.Status);
    }

    // ---- Ratings ----

    [Fact]
    public async Task TheRequesterRatesACompletedBookingAndItMovesTheProviderAverage()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync($"lsp_rate_{Tag()}");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var lspToken = await CreateLspUserAsync($"lsp_rate_provider_{Tag()}");
        var provider = await UpsertProviderAsync(lspToken, $"Rated Freight {Guid.NewGuid():N}",
            coverage: ["NG"], modes: [ShipmentMode.Road], services: []);

        // A directory-fresh provider starts unrated.
        Assert.Equal(0, provider.RatingCount);

        var booking = await RequestBookingAsync(orderId, buyerToken, provider.Id, ShipmentMode.Road);
        await PostAuthenticatedAsync(
            $"/api/v1/logistics/bookings/{booking.Id}/respond", lspToken, new { accept = true });
        (await PostAuthenticatedAsync(
            $"/api/v1/logistics/bookings/{booking.Id}/complete", buyerToken, new { })).EnsureSuccessStatusCode();

        var rate = await PostAuthenticatedAsync(
            $"/api/v1/logistics/bookings/{booking.Id}/rating", buyerToken,
            new { rating = 5, comment = "On time and careful." });
        Assert.Equal(HttpStatusCode.Created, rate.StatusCode);
        var rating = (await rate.Content.ReadFromJsonAsync<RatingMirror>(JsonOptions))!;
        Assert.Equal(5, rating.Rating);
        Assert.True(rating.IsMine);
        Assert.Equal("On time and careful.", rating.Comment);
        Assert.Equal(provider.Id, rating.ProviderId);

        // The booking now carries the caller's own rating, so the card can show it without a fetch.
        var bookingAfter = await GetAuthenticatedAsync($"/api/v1/logistics/bookings/{booking.Id}", buyerToken);
        var reflected = (await bookingAfter.Content.ReadFromJsonAsync<BookingMirror>(JsonOptions))!;
        Assert.Equal((byte?)5, reflected.MyRating);

        // And the provider's headline moved with it.
        var detail = await GetAuthenticatedAsync($"/api/v1/logistics/providers/{provider.Id}", buyerToken);
        var moved = (await detail.Content.ReadFromJsonAsync<ProviderDetailMirror>(JsonOptions))!;
        Assert.Equal(5.00m, moved.Rating);
        Assert.Equal(1, moved.RatingCount);
    }

    [Fact]
    public async Task ARatingNeedsACompletedBookingTheRequesterAndANovelty()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync($"lsp_rate_rules_{Tag()}");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var lspToken = await CreateLspUserAsync($"lsp_rate_rules_provider_{Tag()}");
        var provider = await UpsertProviderAsync(lspToken, $"Rules Freight {Guid.NewGuid():N}",
            coverage: ["NG"], modes: [ShipmentMode.Road], services: []);

        var booking = await RequestBookingAsync(orderId, buyerToken, provider.Id, ShipmentMode.Road);

        // Not before the work is done: an open or accepted booking is a promise, not a delivery.
        var tooEarly = await PostAuthenticatedAsync(
            $"/api/v1/logistics/bookings/{booking.Id}/rating", buyerToken, new { rating = 4 });
        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);
        Assert.Equal("BOOKING_NOT_COMPLETED", await ProblemCodeAsync(tooEarly));

        await PostAuthenticatedAsync(
            $"/api/v1/logistics/bookings/{booking.Id}/respond", lspToken, new { accept = true });
        await PostAuthenticatedAsync(
            $"/api/v1/logistics/bookings/{booking.Id}/complete", buyerToken, new { });

        // The provider cannot rate the leg it carried.
        var self = await PostAuthenticatedAsync(
            $"/api/v1/logistics/bookings/{booking.Id}/rating", lspToken, new { rating = 5 });
        Assert.Equal(HttpStatusCode.Forbidden, self.StatusCode);
        Assert.Equal("NOT_REQUESTER", await ProblemCodeAsync(self));

        var first = await PostAuthenticatedAsync(
            $"/api/v1/logistics/bookings/{booking.Id}/rating", buyerToken, new { rating = 4 });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // One per booking: a second attempt is a conflict, not a re-score.
        var second = await PostAuthenticatedAsync(
            $"/api/v1/logistics/bookings/{booking.Id}/rating", buyerToken, new { rating = 1 });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("RATING_EXISTS", await ProblemCodeAsync(second));
    }

    [Fact]
    public async Task ARatingOutsideOneToFiveIsRefused()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync($"lsp_rate_range_{Tag()}");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var lspToken = await CreateLspUserAsync($"lsp_rate_range_provider_{Tag()}");
        var provider = await UpsertProviderAsync(lspToken, $"Range Freight {Guid.NewGuid():N}",
            coverage: ["NG"], modes: [ShipmentMode.Road], services: []);

        var booking = await RequestBookingAsync(orderId, buyerToken, provider.Id, ShipmentMode.Road);
        await PostAuthenticatedAsync(
            $"/api/v1/logistics/bookings/{booking.Id}/respond", lspToken, new { accept = true });
        await PostAuthenticatedAsync(
            $"/api/v1/logistics/bookings/{booking.Id}/complete", buyerToken, new { });

        // 0 is what a missing score deserialises to, so it is refused rather than read as a star.
        foreach (var bad in new[] { 0, 6 })
        {
            var response = await PostAuthenticatedAsync(
                $"/api/v1/logistics/bookings/{booking.Id}/rating", buyerToken, new { rating = bad });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task ProviderRatingsAreListedAndHiddenWithTheProfile()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync($"lsp_rate_list_{Tag()}");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var lspToken = await CreateLspUserAsync($"lsp_rate_list_provider_{Tag()}");
        var name = $"Listed Freight {Guid.NewGuid():N}";
        var provider = await UpsertProviderAsync(lspToken, name,
            coverage: ["NG"], modes: [ShipmentMode.Road], services: []);

        var booking = await RequestBookingAsync(orderId, buyerToken, provider.Id, ShipmentMode.Road);
        await PostAuthenticatedAsync(
            $"/api/v1/logistics/bookings/{booking.Id}/respond", lspToken, new { accept = true });
        await PostAuthenticatedAsync(
            $"/api/v1/logistics/bookings/{booking.Id}/complete", buyerToken, new { });
        await PostAuthenticatedAsync(
            $"/api/v1/logistics/bookings/{booking.Id}/rating", buyerToken,
            new { rating = 3, comment = "Fine." });

        var list = await GetAuthenticatedAsync($"/api/v1/logistics/providers/{provider.Id}/ratings", buyerToken);
        list.EnsureSuccessStatusCode();
        var page = (await list.Content.ReadFromJsonAsync<PagedMirror<RatingMirror>>(JsonOptions))!;
        var row = Assert.Single(page.Items);
        Assert.Equal(3, row.Rating);
        Assert.Equal("Fine.", row.Comment);
        Assert.True(row.IsMine);

        // The owner reads the same history, but nothing is flagged as theirs.
        var owner = await GetAuthenticatedAsync($"/api/v1/logistics/providers/{provider.Id}/ratings", lspToken);
        var ownerPage = (await owner.Content.ReadFromJsonAsync<PagedMirror<RatingMirror>>(JsonOptions))!;
        Assert.False(Assert.Single(ownerPage.Items).IsMine);

        // A withdrawn provider's ratings vanish for strangers, exactly like the profile itself.
        await UpsertProviderAsync(lspToken, name,
            coverage: ["NG"], modes: [ShipmentMode.Road], services: [], isActive: false);
        var stranger = await RegisterAndLoginAsync($"lsp_rate_stranger_{Tag()}");
        var hidden = await GetAuthenticatedAsync($"/api/v1/logistics/providers/{provider.Id}/ratings", stranger);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal("PROVIDER_NOT_FOUND", await ProblemCodeAsync(hidden));

        // The owner still reads their own history.
        var stillMine = await GetAuthenticatedAsync($"/api/v1/logistics/providers/{provider.Id}/ratings", lspToken);
        Assert.Equal(HttpStatusCode.OK, stillMine.StatusCode);
    }

    // ---- Seed helpers ----

    private async Task<(string BuyerToken, string SellerToken)> SeedTradeAsync(string tag)
    {
        var sellerToken = await VerifyBusinessAsync(
            await CreateCompanyUserAsync($"lsp_seller_{tag}_{Guid.NewGuid():N}"));

        var buyerToken = await RegisterAndLoginAsync($"lsp_buyer_{tag}_{Guid.NewGuid():N}");
        return (buyerToken, sellerToken);
    }

    private async Task<Guid> SeedOrderAsync(string buyerToken, string sellerToken)
    {
        var rfq = await PostAuthenticatedAsync("/api/v1/rfqs", buyerToken, new
        {
            title = $"logistics trade {Guid.NewGuid():N}",
            quantity = 5m,
            unit = (int)PriceUnit.PerTon,
            destinationCountry = "NG",
            destinationPortOrCity = "Lagos",
            preferredIncoterm = (int)Incoterms.Cif,
            preferredCurrency = "USD",
            expiresAt = DateTime.UtcNow.AddDays(30),
        });
        rfq.EnsureSuccessStatusCode();
        var rfqDto = (await rfq.Content.ReadFromJsonAsync<IdMirror>(JsonOptions))!;

        var quotation = await PostAuthenticatedAsync($"/api/v1/rfqs/{rfqDto.Id}/quotations", sellerToken, new
        {
            currency = "USD",
            incoterms = (int)Incoterms.Cif,
            portOfLoading = "Mombasa (MBA)",
            portOfDischarge = "Lagos (LOS)",
            leadTimeDays = 14,
            paymentTerms = (int)PaymentTerms.TtPartialDeposit,
            total = 12000m,
        });
        quotation.EnsureSuccessStatusCode();
        var quotationDto = (await quotation.Content.ReadFromJsonAsync<IdMirror>(JsonOptions))!;

        (await PostAuthenticatedAsync(
            $"/api/v1/quotations/{quotationDto.Id}/accept", buyerToken, new { })).EnsureSuccessStatusCode();

        var order = await PostAuthenticatedAsync("/api/v1/orders/from-quotation", buyerToken, new
        {
            quotationId = quotationDto.Id,
            placeImmediately = true,
        });
        Assert.Equal(HttpStatusCode.Created, order.StatusCode);
        return (await order.Content.ReadFromJsonAsync<IdMirror>(JsonOptions))!.Id;
    }

    private async Task<string> VerifyBusinessAsync(string token)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];

        var registered = await PostAuthenticatedAsync("/api/v1/businesses", token, new
        {
            legalName = $"Lsp Supplier {tag} Ltd",
            tradingName = $"Lsp Supplier {tag}",
            country = "NG",
            businessType = (int)BusinessType.LimitedCompany,
        });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        var businessId = await SupplierBusinessIdAsync(token);

        var upload = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/businesses/{businessId}/documents")
        {
            Content = new MultipartFormDataContent
            {
                { new StringContent(((int)DocumentType.BusinessRegistration).ToString()), "docType" },
                { PdfPart(), "file", "certificate.pdf" },
            },
        };
        upload.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await _client.SendAsync(upload)).EnsureSuccessStatusCode();

        var submit = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/businesses/{businessId}/verification-submissions");
        submit.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await _client.SendAsync(submit)).EnsureSuccessStatusCode();

        var adminToken = await LoginAsync(TestingWebAppFactory.AdminEmail, TestingWebAppFactory.AdminPassword);
        (await PostAuthenticatedAsync(
            $"/api/v1/admin/businesses/{businessId}/verification/review", adminToken,
            new { approve = true })).EnsureSuccessStatusCode();

        return token;
    }

    // ---- Directory / booking helpers ----

    private async Task<ProviderDetailMirror> UpsertProviderAsync(
        string lspToken,
        string companyName,
        string[] coverage,
        ShipmentMode[] modes,
        LogisticsService[] services,
        string? contactEmail = null,
        bool isActive = true)
    {
        var response = await PutAuthenticatedAsync("/api/v1/logistics/providers/mine", lspToken, new
        {
            companyName,
            description = (string?)null,
            homeCountry = "NG",
            contactEmail,
            contactPhone = (string?)null,
            website = (string?)null,
            modes = modes.Select(m => (int)m).ToArray(),
            services = services.Select(s => (int)s).ToArray(),
            coverage,
            isActive,
        });

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProviderDetailMirror>(JsonOptions))!;
    }

    private async Task<BookingMirror> RequestBookingAsync(
        Guid orderId, string token, Guid providerId, ShipmentMode mode, string? notes = null)
    {
        var response = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/logistics-bookings", token,
            new { providerId, mode = (int)mode, notes });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BookingMirror>(JsonOptions))!;
    }

    private async Task<string> CreateLspUserAsync(string prefix)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{prefix}@lsp.tests.agrotrade.local";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = prefix,
            EmailConfirmed = true,
        };
        var created = await users.CreateAsync(user, Password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, RoleNames.LogisticsProvider);
        return await LoginAsync(user.Email!, Password);
    }

    private async Task<int> NotificationCountAsync(string token)
    {
        var response = await GetAuthenticatedAsync("/api/v1/me/notifications?pageSize=100", token);
        if (!response.IsSuccessStatusCode)
        {
            return 0;
        }

        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        return root.ValueKind == System.Text.Json.JsonValueKind.Array
            ? root.GetArrayLength()
            : root.GetProperty("items").GetArrayLength();
    }

    private async Task<string> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() ?? "" : "";
    }

    // ---- HTTP helpers ----

    private Task<HttpResponseMessage> GetAuthenticatedAsync(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PostAuthenticatedAsync(string url, string token, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PutAuthenticatedAsync(string url, string token, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private static ByteArrayContent PdfPart()
    {
        var part = new ByteArrayContent("%PDF-1.4 registration"u8.ToArray());
        part.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return part;
    }

    private async Task<string> CreateCompanyUserAsync(string prefix)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{prefix}@lsp.tests.agrotrade.local";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = prefix,
            EmailConfirmed = true,
        };
        var created = await users.CreateAsync(user, Password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, RoleNames.Company);
        return await LoginAsync(user.Email!, Password);
    }

    private async Task<string> RegisterAndLoginAsync(string prefix)
    {
        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = $"{prefix}@lsp.tests.agrotrade.local",
            password = Password,
            displayName = prefix,
            homeCountry = "NG",
            defaultCurrency = "USD",
            roles = new[] { RoleNames.Importer },
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        return (await register.Content.ReadFromJsonAsync<AuthResponseMirror>(JsonOptions))!.AccessToken;
    }

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponseMirror>(JsonOptions))!.AccessToken;
    }

    private async Task<Guid> SupplierBusinessIdAsync(string token)
    {
        var response = await GetAuthenticatedAsync("/api/v1/businesses/mine", token);
        response.EnsureSuccessStatusCode();
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var first = root.ValueKind == System.Text.Json.JsonValueKind.Array ? root[0] : root.GetProperty("items")[0];
        return first.GetProperty("id").GetGuid();
    }

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    // ---- DTO mirrors ----

    public sealed class PagedMirror<T>
    {
        public List<T> Items { get; init; } = [];
        public int Total { get; init; }
    }

    public sealed class ProviderCardMirror
    {
        public Guid Id { get; init; }
        public string CompanyName { get; init; } = "";
        public string HomeCountry { get; init; } = "";
        public List<ShipmentMode> Modes { get; init; } = [];
        public List<LogisticsService> Services { get; init; } = [];
        public List<string> Coverage { get; init; } = [];
        public bool IsVerified { get; init; }
    }

    public sealed class ProviderDetailMirror
    {
        public Guid Id { get; init; }
        public string CompanyName { get; init; } = "";
        public string HomeCountry { get; init; } = "";
        public List<ShipmentMode> Modes { get; init; } = [];
        public List<LogisticsService> Services { get; init; } = [];
        public List<string> Coverage { get; init; } = [];
        public bool IsVerified { get; init; }
        public decimal Rating { get; init; }
        public int RatingCount { get; init; }
        public string? ContactEmail { get; init; }
        public bool IsActive { get; init; }
        public bool IsMine { get; init; }
    }

    public sealed class BookingMirror
    {
        public Guid Id { get; init; }
        public string Reference { get; init; } = "";
        public Guid OrderId { get; init; }
        public string OrderNumber { get; init; } = "";
        public Guid ProviderId { get; init; }
        public string ProviderName { get; init; } = "";
        public ShipmentMode Mode { get; init; }
        public LogisticsBookingStatus Status { get; init; }
        public string? Notes { get; init; }
        public string? ResponseNotes { get; init; }
        public DateTime? DecidedAtUtc { get; init; }
        public bool IsRequester { get; init; }
        public bool IsProvider { get; init; }
        public byte? MyRating { get; init; }
    }

    public sealed class RatingMirror
    {
        public Guid Id { get; init; }
        public Guid BookingId { get; init; }
        public Guid ProviderId { get; init; }
        public string ProviderName { get; init; } = "";
        public Guid RatedByUserId { get; init; }
        public string RatedByDisplayName { get; init; } = "";
        public byte Rating { get; init; }
        public string? Comment { get; init; }
        public bool IsMine { get; init; }
        public DateTime? CreatedAtUtc { get; init; }
    }

    public sealed class IdMirror
    {
        public Guid Id { get; init; }
    }

    public sealed class AuthResponseMirror
    {
        public string AccessToken { get; init; } = "";
    }
}
