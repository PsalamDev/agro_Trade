using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AgroTrade.Domain.Enums;
using AgroTrade.Infrastructure.Auth;
using AgroTrade.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgroTrade.Tests.Integration;

/// <summary>
/// Consignments, tracking milestones and public guest tracking, task 2.6 (plan §02 — Shipments).
///
/// <para>
/// Two claims carry most of the weight here. First, a shipment's status <i>is</i> its latest
/// milestone — there is no stored status to drift — so a back-dated milestone must not silently
/// roll the shipment backwards. Second, the guest link is a bearer secret stored hashed and
/// answered with a deliberately reduced payload, because it gets forwarded to people who have no
/// account here.
/// </para>
/// </summary>
public sealed class ShipmentFlowTests : IClassFixture<TestingWebAppFactory>
{
    private const string Password = "StrongPass123";

    private readonly TestingWebAppFactory _factory;
    private readonly HttpClient _client;

    public ShipmentFlowTests(TestingWebAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureSchema();
        _client = factory.CreateClient();
    }

    // ---- Booking a consignment ----

    [Fact]
    public async Task SellerBooksAConsignment_AndItOpensWithItsOwnFirstMilestone()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("book");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var response = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/shipments", sellerToken,
            new { mode = (int)ShipmentMode.Sea, carrierName = "Maersk" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var shipment = await response.Content.ReadFromJsonAsync<ShipmentMirror>();
        Assert.NotNull(shipment);

        // A shipment exists with something said about it. Without event zero there would be no
        // status at all, because status is the latest milestone.
        Assert.Equal(ShipmentStatus.Booked, shipment!.Status);
        Assert.Single(shipment.Events);
        Assert.Equal(ShipmentStatus.Booked, shipment.Events[0].Status);
        Assert.Equal(orderId, shipment.OrderId);
        Assert.StartsWith("SHP-", shipment.Reference);
        Assert.True(shipment.IsSeller);
        Assert.False(shipment.IsBuyer);

        // The milestone is dated, so "booked since" is answerable.
        Assert.NotNull(shipment.StatusSinceUtc);
        Assert.Null(shipment.DepartedAtUtc);
    }

    [Fact]
    public async Task RouteDefaultsToTheOrdersSnapshottedTradeTerms()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("route");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var shipment = await BookAsync(orderId, sellerToken, new { mode = (int)ShipmentMode.Sea, carrierName = "Maersk" });

        // Nothing but the carrier was supplied, and the ports still came from the quotation the
        // buyer agreed to — so a consignment cannot quietly contradict the order.
        Assert.Equal("Mombasa (MBA)", shipment.OriginPort);
        Assert.Equal("Lagos (LOS)", shipment.DestinationPort);
        Assert.Equal("NG", shipment.DestinationCountry);
        Assert.Equal(Incoterms.Cif, shipment.Incoterms);
    }

    [Fact]
    public async Task SellerMayOverrideTheRouteWhenGoodsAreReRouted()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("reroute");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var shipment = await BookAsync(orderId, sellerToken, new
        {
            mode = (int)ShipmentMode.Sea,
            carrierName = "Maersk",
            originPort = "Durban (DUR)",
            originCountry = "ZA",
            destinationPort = "Apapa (Lagos)",
        });

        Assert.Equal("Durban (DUR)", shipment.OriginPort);
        Assert.Equal("ZA", shipment.OriginCountry);
        Assert.Equal("Apapa (Lagos)", shipment.DestinationPort);
    }

    [Fact]
    public async Task ASecondConsignmentOnTheSameOrderIsRefused()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("second");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        await BookAsync(orderId, sellerToken, new { mode = (int)ShipmentMode.Sea, carrierName = "Maersk" });

        var again = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/shipments", sellerToken,
            new { mode = (int)ShipmentMode.Air, carrierName = "Emirates SkyCargo" });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("SHIPMENT_EXISTS", await ProblemCodeAsync(again));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreightCannotBeBookedAgainstAnOrderThatIsNotLive(bool cancelIt)
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync(cancelIt ? "closed-cancelled" : "closed-draft");

        // A draft is never placed; a placed order is then cancelled.
        var orderId = await SeedOrderAsync(buyerToken, sellerToken, placeImmediately: cancelIt);

        if (cancelIt)
        {
            (await PostAuthenticatedAsync(
                $"/api/v1/orders/{orderId}/transitions", sellerToken,
                new { to = (int)OrderStatus.Cancelled })).EnsureSuccessStatusCode();
        }

        var response = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/shipments", sellerToken,
            new { mode = (int)ShipmentMode.Sea, carrierName = "Maersk" });

        // A draft has committed to nothing and a cancelled order has been unwound; a consignment
        // against either would be a record of something that is not happening.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ORDER_CLOSED", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task TheBuyerCannotBookTheConsignment()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("buytries");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var response = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/shipments", buyerToken,
            new { mode = (int)ShipmentMode.Sea, carrierName = "Maersk" });

        // 403, not 404: the buyer knows the order is theirs, so telling them this is the seller's
        // job is more useful than pretending otherwise.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("NOT_SELLER", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task AStrangerCannotEvenTellTheOrderExists()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("stranger");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var strangerToken = await RegisterAndLoginAsync($"sh_stranger_{Guid.NewGuid():N}");

        var response = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/shipments", strangerToken,
            new { mode = (int)ShipmentMode.Sea, carrierName = "Maersk" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ACarrierNameIsRequired()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("nocarrier");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var response = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/shipments", sellerToken,
            new { mode = (int)ShipmentMode.Sea, carrierName = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- The timeline ----

    [Fact]
    public async Task AMilestoneMovesTheShipmentToThatStatus()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("depart");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var shipment = await BookAsync(orderId, sellerToken, new { mode = (int)ShipmentMode.Sea, carrierName = "Maersk" });

        var response = await PostAuthenticatedAsync(
            $"/api/v1/shipments/{shipment.Id}/tracking-events", sellerToken,
            new { status = (int)ShipmentStatus.Departed, location = "Mombasa (MBA)" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var reloaded = await GetShipmentAsync(shipment.Id, sellerToken);
        Assert.Equal(ShipmentStatus.Departed, reloaded.Status);
        Assert.Equal(2, reloaded.Events.Count);

        // Newest first, because a timeline is read from the present backwards.
        Assert.Equal(ShipmentStatus.Departed, reloaded.Events[0].Status);
        Assert.Equal(ShipmentStatus.Booked, reloaded.Events[1].Status);

        // Departure is derived from the milestone rather than stored, so it appears now.
        Assert.NotNull(reloaded.DepartedAtUtc);
    }

    [Fact]
    public async Task TheBookingCanBeDatedSoALateEntryHasACoherentTimeline()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("lateentry");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        // Logged up after the fact: booked ten days ago, sailed eight, in transit since.
        var shipment = await BookAsync(orderId, sellerToken, new
        {
            mode = (int)ShipmentMode.Sea,
            carrierName = "Maersk",
            initialOccurredAtUtc = DateTime.UtcNow.AddDays(-10),
        });

        await AppendEventAsync(shipment.Id, sellerToken, ShipmentStatus.Departed, DateTime.UtcNow.AddDays(-8));
        await AppendEventAsync(shipment.Id, sellerToken, ShipmentStatus.InTransit, DateTime.UtcNow.AddDays(-2));

        var reloaded = await GetShipmentAsync(shipment.Id, sellerToken);

        // Without a datable booking, the booking would default to now, every correctly-dated
        // milestone would sort beneath it, and the shipment would read as still at the origin.
        Assert.Equal(ShipmentStatus.InTransit, reloaded.Status);
        Assert.Equal(3, reloaded.Events.Count);
        Assert.NotNull(reloaded.DepartedAtUtc);
    }

    [Fact]
    public async Task ABilestoneIsRejectedForABookingDatedInTheFuture()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("futurebooking");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var response = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/shipments", sellerToken,
            new
            {
                mode = (int)ShipmentMode.Sea,
                carrierName = "Maersk",
                initialOccurredAtUtc = DateTime.UtcNow.AddDays(3),
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DepartureDateIsTheEarliestDepartureNotTheLatest()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("rerouted");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var shipment = await BookAsync(orderId, sellerToken, new
        {
            mode = (int)ShipmentMode.Sea,
            carrierName = "Maersk",
            initialOccurredAtUtc = DateTime.UtcNow.AddDays(-10),
        });

        var first = DateTime.UtcNow.AddDays(-6);
        await AppendEventAsync(shipment.Id, sellerToken, ShipmentStatus.Departed, first);
        await AppendEventAsync(shipment.Id, sellerToken, ShipmentStatus.Departed, DateTime.UtcNow.AddDays(-1));

        var reloaded = await GetShipmentAsync(shipment.Id, sellerToken);

        // A re-routed consignment leaves twice. When it first left is not rewritten by the second
        // sailing, so the earliest Departed milestone is the answer. Compared as raw instants:
        // SQL Server's datetime2 carries no offset, so a Kind-sensitive comparison would be
        // reading an artefact of the column type rather than the value.
        Assert.NotNull(reloaded.DepartedAtUtc);
        Assert.Equal(first.Ticks, reloaded.DepartedAtUtc!.Value.Ticks);
    }

    [Fact]
    public async Task ABackDatedMilestoneIsRecordedButDoesNotRollTheShipmentBack()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("backdated");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var shipment = await BookAsync(orderId, sellerToken, new
        {
            mode = (int)ShipmentMode.Sea,
            carrierName = "Maersk",
            initialOccurredAtUtc = DateTime.UtcNow.AddDays(-10),
        });

        await AppendEventAsync(shipment.Id, sellerToken, ShipmentStatus.InTransit, DateTime.UtcNow.AddDays(-2));

        // The seller remembers an earlier milestone and types it in after a later one.
        var response = await PostAuthenticatedAsync(
            $"/api/v1/shipments/{shipment.Id}/tracking-events", sellerToken,
            new { status = (int)ShipmentStatus.Departed, location = "Mombasa (MBA)", occurredAtUtc = DateTime.UtcNow.AddDays(-3) });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var reloaded = await GetShipmentAsync(shipment.Id, sellerToken);

        // Both events are kept — the record of what was believed, and when — but the current
        // position is still the latest one, because that is what "latest" means.
        Assert.Equal(3, reloaded.Events.Count);
        Assert.Equal(ShipmentStatus.InTransit, reloaded.Status);
    }

    [Fact]
    public async Task StatusesAreNotForcedToAdvance()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("regress");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var shipment = await BookAsync(orderId, sellerToken, new
        {
            mode = (int)ShipmentMode.Sea,
            carrierName = "Maersk",
            initialOccurredAtUtc = DateTime.UtcNow.AddDays(-10),
        });

        await AppendEventAsync(shipment.Id, sellerToken, ShipmentStatus.Delivered, DateTime.UtcNow.AddDays(-4));
        var reloaded = await GetShipmentAsync(shipment.Id, sellerToken);
        Assert.Equal(ShipmentStatus.Delivered, reloaded.Status);

        // Delivered goods genuinely do generate a customs exception afterwards. Refusing the
        // milestone would only teach the seller to stop recording them.
        await AppendEventAsync(shipment.Id, sellerToken, ShipmentStatus.Exception, DateTime.UtcNow.AddDays(-1));
        reloaded = await GetShipmentAsync(shipment.Id, sellerToken);
        Assert.Equal(ShipmentStatus.Exception, reloaded.Status);
        Assert.Equal(3, reloaded.Events.Count);
    }

    [Fact]
    public async Task AnOffsetMilestoneIsNormalisedSoItCannotReadAsLaterThanItIs()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("offset");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var shipment = await BookAsync(orderId, sellerToken, new
        {
            mode = (int)ShipmentMode.Sea,
            carrierName = "Maersk",
            initialOccurredAtUtc = "2026-01-01T00:00:00Z",
        });

        // 12:00+02:00 is 10:00Z. A raw DateTime comparison would read the ticks as 12:00 and make
        // it the latest milestone, which it is not.
        await PostAuthenticatedAsync(
            $"/api/v1/shipments/{shipment.Id}/tracking-events", sellerToken,
            new { status = (int)ShipmentStatus.Departed, occurredAtUtc = "2026-01-02T12:00:00+02:00" });

        await AppendEventAsync(
            shipment.Id, sellerToken, ShipmentStatus.InTransit, new DateTime(2026, 1, 2, 11, 0, 0, DateTimeKind.Utc));

        var reloaded = await GetShipmentAsync(shipment.Id, sellerToken);
        Assert.Equal(ShipmentStatus.InTransit, reloaded.Status);
    }

    [Fact]
    public async Task AMilestoneCannotBeDatedInTheFuture()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("future");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var shipment = await BookAsync(orderId, sellerToken, new { mode = (int)ShipmentMode.Sea, carrierName = "Maersk" });

        var response = await PostAuthenticatedAsync(
            $"/api/v1/shipments/{shipment.Id}/tracking-events", sellerToken,
            new { status = (int)ShipmentStatus.Departed, occurredAtUtc = DateTime.UtcNow.AddDays(2) });

        // Load-bearing: the shipment's status is its latest milestone, so a future-dated one would
        // immediately become the position both the buyer and the public link report.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MilestonesAreAppendOnlyAndCannotBeAltered()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("appendonly");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var shipment = await BookAsync(orderId, sellerToken, new { mode = (int)ShipmentMode.Sea, carrierName = "Maersk" });
        var milestoneId = shipment.Events[0].Id;

        // A shipment timeline is the evidence a buyer relies on if goods arrive late. There is no
        // edit and no delete anywhere in the surface, by construction rather than by policy.
        foreach (var (method, url) in new[]
                 {
                     (HttpMethod.Put, $"/api/v1/shipments/{shipment.Id}/tracking-events/{milestoneId}"),
                     (HttpMethod.Patch, $"/api/v1/shipments/{shipment.Id}/tracking-events/{milestoneId}"),
                     (HttpMethod.Delete, $"/api/v1/shipments/{shipment.Id}/tracking-events/{milestoneId}"),
                     (HttpMethod.Delete, $"/api/v1/shipments/{shipment.Id}"),
                 })
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);
            var response = await _client.SendAsync(request);

            Assert.True(
                response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                $"{method} {url} returned {(int)response.StatusCode}.");
        }

        // And the milestone is still exactly where it was left.
        Assert.Single((await GetShipmentAsync(shipment.Id, sellerToken)).Events);
    }

    [Fact]
    public async Task TheBuyerCannotAppendAMilestone()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("buyerappend");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var shipment = await BookAsync(orderId, sellerToken, new { mode = (int)ShipmentMode.Sea, carrierName = "Maersk" });

        var response = await PostAuthenticatedAsync(
            $"/api/v1/shipments/{shipment.Id}/tracking-events", buyerToken,
            new { status = (int)ShipmentStatus.Delivered });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- Reads ----

    [Fact]
    public async Task BothPartiesSeeTheConsignmentAndItsTimeline()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("bothsee");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var shipment = await BookAsync(orderId, sellerToken, new { mode = (int)ShipmentMode.Sea, carrierName = "Maersk" });
        await AppendEventAsync(shipment.Id, sellerToken, ShipmentStatus.Departed);

        var byBuyer = await GetAuthenticatedAsync($"/api/v1/shipments/{shipment.Id}", buyerToken);
        byBuyer.EnsureSuccessStatusCode();
        var buyerView = await byBuyer.Content.ReadFromJsonAsync<ShipmentMirror>();
        Assert.NotNull(buyerView);
        Assert.True(buyerView!.IsBuyer);
        Assert.False(buyerView.IsSeller);
        Assert.Equal(2, buyerView.Events.Count);
        Assert.Equal(orderId, buyerView.OrderId);

        var listResponse = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/shipments", buyerToken);
        listResponse.EnsureSuccessStatusCode();
        Assert.Single((await listResponse.Content.ReadFromJsonAsync<List<ShipmentMirror>>())!);
    }

    [Fact]
    public async Task AStrangerSeesNoConsignmentAndNoOrder()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("strangerread");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        await BookAsync(orderId, sellerToken, new { mode = (int)ShipmentMode.Sea, carrierName = "Maersk" });

        var strangerToken = await RegisterAndLoginAsync($"sh_outside_{Guid.NewGuid():N}");

        // Addressing the order is 404, not 403: the order behind the consignment is itself
        // private, and confirming it exists is already a leak.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/shipments", strangerToken)).StatusCode);

        // The cross-order list is a different shape: it is filtered to what the caller is party
        // to, so a stranger gets an empty list rather than a refusal. Nothing is disclosed by
        // saying "none of these are yours".
        var listResponse = await GetAuthenticatedAsync("/api/v1/shipments", strangerToken);
        listResponse.EnsureSuccessStatusCode();
        Assert.Empty((await listResponse.Content.ReadFromJsonAsync<List<ShipmentMirror>>())!);
    }

    [Fact]
    public async Task ListingCanBeNarrowedToOneSideOfTheTrade()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("filter");
        var first = await SeedOrderAsync(buyerToken, sellerToken);
        var second = await SeedOrderAsync(buyerToken, sellerToken);

        await BookAsync(first, sellerToken, new { mode = (int)ShipmentMode.Sea, carrierName = "Maersk" });
        await BookAsync(second, sellerToken, new { mode = (int)ShipmentMode.Air, carrierName = "Emirates SkyCargo" });

        var sellerView = await GetAuthenticatedAsync("/api/v1/shipments?as=seller", sellerToken);
        sellerView.EnsureSuccessStatusCode();
        Assert.Equal(2, (await sellerView.Content.ReadFromJsonAsync<List<ShipmentMirror>>())!.Count);

        // The buyer's side filter is about whose order it is, not about permissions: the buyer can
        // still read both, because both are their trades.
        var buyerView = await GetAuthenticatedAsync("/api/v1/shipments?as=buyer", buyerToken);
        buyerView.EnsureSuccessStatusCode();
        Assert.Equal(2, (await buyerView.Content.ReadFromJsonAsync<List<ShipmentMirror>>())!.Count);
    }

    [Fact]
    public async Task ListingCanBeFilteredByDerivedStatus()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("statusfilter");
        var departedOrder = await SeedOrderAsync(buyerToken, sellerToken);
        var bookedOrder = await SeedOrderAsync(buyerToken, sellerToken);

        var departed = await BookAsync(departedOrder, sellerToken, new { mode = (int)ShipmentMode.Sea, carrierName = "Maersk" });
        await BookAsync(bookedOrder, sellerToken, new { mode = (int)ShipmentMode.Air, carrierName = "Emirates SkyCargo" });
        await AppendEventAsync(departed.Id, sellerToken, ShipmentStatus.Departed);

        var response = await GetAuthenticatedAsync("/api/v1/shipments?status=Departed", sellerToken);
        response.EnsureSuccessStatusCode();
        var filtered = await response.Content.ReadFromJsonAsync<List<ShipmentMirror>>();
        Assert.NotNull(filtered);
        Assert.Single(filtered!);
        Assert.Equal(departed.Id, filtered![0].Id);
    }

    [Fact]
    public async Task AnUnrecognisedStatusFilterIsRejectedRatherThanSilentlyEmpty()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("badfilter");
        await SeedOrderAsync(buyerToken, sellerToken);

        var response = await GetAuthenticatedAsync("/api/v1/shipments?status=Teleported", sellerToken);

        // A typo in a query string should be a clear 400, not an empty list that reads as
        // "no shipments match".
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Guest tracking ----

    [Fact]
    public async Task TheGuestLinkWorksWithoutAnAccountAndOmitsEverythingCommercial()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("guest");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var shipment = await BookAsync(orderId, sellerToken, new
        {
            mode = (int)ShipmentMode.Sea,
            carrierName = "Maersk",
            trackingReference = "MEDU7741203",
        });
        var token = await RotateTokenAsync(shipment.Id, sellerToken);
        await AppendEventAsync(shipment.Id, sellerToken, ShipmentStatus.Departed, null, "Mombasa (MBA)");

        // No Authorization header at all: a freight forwarder checking a container has no account.
        var response = await _client.GetAsync($"/api/v1/shipments/track/{token}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        var tracking = System.Text.Json.JsonDocument.Parse(raw).RootElement;

        // What a forwarder needs.
        Assert.Equal(ShipmentMode.Sea, (ShipmentMode)tracking.GetProperty("mode").GetInt32());
        Assert.Equal("Maersk", tracking.GetProperty("carrierName").GetString());
        Assert.Equal("MEDU7741203", tracking.GetProperty("trackingReference").GetString());
        Assert.Equal("Lagos (LOS)", tracking.GetProperty("destinationPort").GetString());
        Assert.Equal(ShipmentStatus.Departed, (ShipmentStatus)tracking.GetProperty("status").GetInt32());

        // What they must not get: the order, the money, the parties. The omissions are the design.
        foreach (var forbidden in new[] { "orderId", "orderNumber", "currency", "amount", "total", "buyer", "seller", "isBuyer", "isSeller" })
        {
            Assert.False(tracking.TryGetProperty(forbidden, out _), $"The guest payload exposed '{forbidden}'.");
        }

        Assert.DoesNotContain("12000", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Delta Foods", raw, StringComparison.OrdinalIgnoreCase);

        // The milestone timeline comes through, oldest first so the page reads as a journey.
        var events = tracking.GetProperty("events");
        Assert.Equal(2, events.GetArrayLength());
        Assert.Equal(ShipmentStatus.Booked, (ShipmentStatus)events[0].GetProperty("status").GetInt32());
        Assert.Equal(ShipmentStatus.Departed, (ShipmentStatus)events[1].GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task OnlyTheHashOfTheGuestTokenIsStored()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("hashed");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var shipment = await BookAsync(orderId, sellerToken, new { mode = (int)ShipmentMode.Sea, carrierName = "Maersk" });
        var token = await RotateTokenAsync(shipment.Id, sellerToken);

        var stored = await StoredTokenHashAsync(shipment.Id);
        Assert.NotNull(stored);
        Assert.Equal(OpaqueTokenFactory.Hash(token), stored);
        Assert.NotEqual(token, stored);

        // And the raw value is nowhere in the read surface either.
        var detail = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}/shipments", sellerToken);
        detail.EnsureSuccessStatusCode();
        Assert.DoesNotContain(token, await detail.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // So a database dump yields no working tracking link: presenting the stored hash as if it
        // were the token gets nothing.
        Assert.Equal(HttpStatusCode.NotFound, await GuestTrackStatusAsync(stored!));
    }

    [Fact]
    public async Task AnUnknownGuestTokenIsIndistinguishableFromAnInvalidOne()
    {
        var response = await _client.GetAsync($"/api/v1/shipments/track/{Guid.NewGuid():N}");

        // Every token is either a live link or random noise, so there is no "exists but not yours"
        // to leak. The blank case is refused the same way.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/v1/shipments/track/nonsense")).StatusCode);
    }

    [Fact]
    public async Task RotatingTheTokenKillsThePreviousLinkImmediately()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("rotate");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var shipment = await BookAsync(orderId, sellerToken, new { mode = (int)ShipmentMode.Sea, carrierName = "Maersk" });

        var first = await RotateTokenAsync(shipment.Id, sellerToken);
        var second = await RotateTokenAsync(shipment.Id, sellerToken);

        Assert.NotEqual(first, second);

        // A tracking URL forwarded into an inbox cannot be recalled, so rotation has to be a real
        // kill switch rather than a way to get a second copy of the same secret.
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/v1/shipments/track/{first}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/v1/shipments/track/{second}")).StatusCode);
    }

    [Fact]
    public async Task GuestTrackingCanBeLeftOffAndTheBuyerIsStillToldTheStory()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("noguest");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);

        var shipment = await BookAsync(orderId, sellerToken, new
        {
            mode = (int)ShipmentMode.Sea,
            carrierName = "Maersk",
            enableGuestTracking = false,
        });

        Assert.False(shipment.HasGuestTracking);

        // Off is a real choice: not every trade wants a public link.
        await AppendEventAsync(shipment.Id, sellerToken, ShipmentStatus.Departed);
        var byBuyer = await GetAuthenticatedAsync($"/api/v1/shipments/{shipment.Id}", buyerToken);
        byBuyer.EnsureSuccessStatusCode();
        Assert.Equal(
            ShipmentStatus.Departed,
            (await byBuyer.Content.ReadFromJsonAsync<ShipmentMirror>())!.Status);
    }

    [Fact]
    public async Task OnlyTheSellerCanRotateTheGuestToken()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("rotaterights");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var shipment = await BookAsync(orderId, sellerToken, new { mode = (int)ShipmentMode.Sea, carrierName = "Maersk" });

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await PostAuthenticatedAsync($"/api/v1/shipments/{shipment.Id}/tracking-token", buyerToken, new { })).StatusCode);
    }

    // ---- Notifications ----

    [Fact]
    public async Task TheBuyerIsToldAboutMilestonesAndTheSellerIsNot()
    {
        var (buyerToken, sellerToken) = await SeedTradeAsync("notify");
        var orderId = await SeedOrderAsync(buyerToken, sellerToken);
        var shipment = await BookAsync(orderId, sellerToken, new { mode = (int)ShipmentMode.Sea, carrierName = "Maersk" });

        var buyerBefore = await NotificationCountAsync(buyerToken);
        var sellerBefore = await NotificationCountAsync(sellerToken);

        await AppendEventAsync(shipment.Id, sellerToken, ShipmentStatus.InTransit, null, "Red Sea");

        Assert.True(
            await NotificationCountAsync(buyerToken) > buyerBefore,
            "The buyer was not told the consignment moved.");
        Assert.Equal(sellerBefore, await NotificationCountAsync(sellerToken));
    }

    // ---- Seeding ----

    private async Task<(string BuyerToken, string SellerToken)> SeedTradeAsync(string tag)
    {
        var sellerToken = await VerifyBusinessAsync(
            await CreateCompanyUserAsync($"sh_seller_{tag}_{Guid.NewGuid():N}"));

        var buyerToken = await RegisterAndLoginAsync($"sh_buyer_{tag}_{Guid.NewGuid():N}");
        return (buyerToken, sellerToken);
    }

    private async Task<Guid> SeedOrderAsync(
        string buyerToken, string sellerToken, bool placeImmediately = true)
    {
        var rfq = await PostAuthenticatedAsync("/api/v1/rfqs", buyerToken, new
        {
            title = $"shipment trade {Guid.NewGuid():N}",
            quantity = 5m,
            unit = (int)PriceUnit.PerTon,
            destinationCountry = "NG",
            destinationPortOrCity = "Lagos",
            preferredIncoterm = (int)Incoterms.Cif,
            preferredCurrency = "USD",
            expiresAt = DateTime.UtcNow.AddDays(30),
        });
        rfq.EnsureSuccessStatusCode();
        var rfqDto = await rfq.Content.ReadFromJsonAsync<RfqDetailDto>();

        var quotation = await PostAuthenticatedAsync($"/api/v1/rfqs/{rfqDto!.Id}/quotations", sellerToken, new
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
        var quotationDto = await quotation.Content.ReadFromJsonAsync<QuotationDto>();

        (await PostAuthenticatedAsync(
            $"/api/v1/quotations/{quotationDto!.Id}/accept", buyerToken, new { })).EnsureSuccessStatusCode();

        var order = await PostAuthenticatedAsync("/api/v1/orders/from-quotation", buyerToken, new
        {
            quotationId = quotationDto.Id,
            placeImmediately,
        });
        Assert.Equal(HttpStatusCode.Created, order.StatusCode);
        return (await order.Content.ReadFromJsonAsync<OrderDetailMirror>())!.Id;
    }

    private async Task<string> VerifyBusinessAsync(string token)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];

        var registered = await PostAuthenticatedAsync("/api/v1/businesses", token, new
        {
            legalName = $"Ship Supplier {tag} Ltd",
            tradingName = $"Ship Supplier {tag}",
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

        var adminToken = await AdminTokenAsync();
        (await PostAuthenticatedAsync(
            $"/api/v1/admin/businesses/{businessId}/verification/review", adminToken,
            new { approve = true })).EnsureSuccessStatusCode();

        return token;
    }

    // ---- Flow helpers ----

    private async Task<ShipmentMirror> BookAsync(Guid orderId, string sellerToken, object body)
    {
        var response = await PostAuthenticatedAsync(
            $"/api/v1/orders/{orderId}/shipments", sellerToken, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ShipmentMirror>())!;
    }

    private async Task AppendEventAsync(
        Guid shipmentId, string sellerToken, ShipmentStatus status,
        DateTime? occurredAtUtc = null, string? location = null)
    {
        var response = await PostAuthenticatedAsync(
            $"/api/v1/shipments/{shipmentId}/tracking-events", sellerToken,
            new
            {
                status = (int)status,
                occurredAtUtc = occurredAtUtc is null ? null : occurredAtUtc,
                location,
            });
        response.EnsureSuccessStatusCode();
    }

    private async Task<ShipmentMirror> GetShipmentAsync(Guid shipmentId, string token)
    {
        var response = await GetAuthenticatedAsync($"/api/v1/shipments/{shipmentId}", token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ShipmentMirror>())!;
    }

    private async Task<string> RotateTokenAsync(Guid shipmentId, string sellerToken)
    {
        var response = await PostAuthenticatedAsync(
            $"/api/v1/shipments/{shipmentId}/tracking-token", sellerToken, new { });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TrackingTokenMirror>())!.Token;
    }

    private async Task<string?> StoredTokenHashAsync(Guid shipmentId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Data.AgroTradeDbContext>();
        return await db.Shipments
            .AsNoTracking()
            .Where(s => s.Id == shipmentId)
            .Select(s => s.PublicTrackingTokenHash)
            .FirstOrDefaultAsync();
    }

    private async Task<HttpStatusCode> GuestTrackStatusAsync(string token) =>
        (await _client.GetAsync($"/api/v1/shipments/track/{token}")).StatusCode;

    private async Task<string> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() ?? "" : "";
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
        var user = new ApplicationUser
        {
            UserName = $"{prefix}@ship.tests.agrotrade.local",
            Email = $"{prefix}@ship.tests.agrotrade.local",
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
            email = $"{prefix}@ship.tests.agrotrade.local",
            password = Password,
            displayName = prefix,
            homeCountry = "NG",
            defaultCurrency = "USD",
            roles = new[] { RoleNames.Importer },
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        return (await register.Content.ReadFromJsonAsync<AuthResponseDto>())!.AccessToken;
    }

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!.AccessToken;
    }

    private Task<string> AdminTokenAsync() =>
        LoginAsync(TestingWebAppFactory.AdminEmail, TestingWebAppFactory.AdminPassword);

    private async Task<Guid> SupplierBusinessIdAsync(string token)
    {
        var response = await GetAuthenticatedAsync("/api/v1/businesses/mine", token);
        response.EnsureSuccessStatusCode();
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var first = root.ValueKind == System.Text.Json.JsonValueKind.Array ? root[0] : root.GetProperty("items")[0];
        return first.GetProperty("id").GetGuid();
    }

    // ---- DTO mirrors ----

    public sealed class ShipmentMirror
    {
        public Guid Id { get; init; }
        public Guid OrderId { get; init; }
        public string OrderNumber { get; init; } = "";
        public string Reference { get; init; } = "";
        public ShipmentStatus Status { get; init; }
        public DateTime? StatusSinceUtc { get; init; }
        public ShipmentMode Mode { get; init; }
        public string CarrierName { get; init; } = "";
        public string? TrackingReference { get; init; }
        public string? OriginCountry { get; init; }
        public string? OriginPort { get; init; }
        public string? DestinationCountry { get; init; }
        public string? DestinationPort { get; init; }
        public Incoterms? Incoterms { get; init; }
        public DateTime? DepartedAtUtc { get; init; }
        public DateTime? EstimatedArrivalUtc { get; init; }
        public bool HasGuestTracking { get; init; }
        public bool IsSeller { get; init; }
        public bool IsBuyer { get; init; }
        public List<ShipmentTrackingEventMirror> Events { get; init; } = [];
    }

    public sealed class ShipmentTrackingEventMirror
    {
        public Guid Id { get; init; }
        public Guid ShipmentId { get; init; }
        public ShipmentStatus Status { get; init; }
        public string? Location { get; init; }
        public string? Description { get; init; }
        public DateTime OccurredAtUtc { get; init; }
        public DateTime RecordedAtUtc { get; init; }
        public string? RecordedByDisplayName { get; init; }
    }

    public sealed class TrackingTokenMirror
    {
        public Guid ShipmentId { get; init; }
        public string Token { get; init; } = "";
        public string TrackingPath { get; init; } = "";
    }

    public sealed class OrderDetailMirror
    {
        public Guid Id { get; init; }
        public string OrderNumber { get; init; } = "";
    }

    public sealed class QuotationDto
    {
        public Guid Id { get; init; }
    }

    public sealed class RfqDetailDto
    {
        public Guid Id { get; init; }
    }

    public sealed class AuthResponseDto
    {
        public string AccessToken { get; init; } = "";
    }
}