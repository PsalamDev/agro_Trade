using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AgroTrade.Domain.Enums;
using AgroTrade.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgroTrade.Tests.Integration;

/// <summary>
/// Domestic buy-now / cart (roadmap Phase 4 — Slice 1). A buyer builds a cart of listings
/// flagged <c>SellsDomestically</c> and checks out into one order per seller, with a delivery
/// address the seller's own country has to match, a summed delivery fee, and stock held across
/// the whole cart atomically.
///
/// <para>
/// The load-bearing claims: (1) a cart is re-validated at checkout, never trusted — a listing
/// taken down, de-domesticised or drained of stock after it was added refuses the whole
/// checkout instead of silently dropping money; (2) checkout is all-or-nothing across sellers —
/// every seller's order lands or none does; (3) a successful checkout converts the cart, which
/// then cannot be placed twice.
/// </para>
/// </summary>
public sealed class DomesticCartFlowTests : IClassFixture<TestingWebAppFactory>
{
    private const string Password = "StrongPass123";

    private readonly TestingWebAppFactory _factory;
    private readonly HttpClient _client;

    public DomesticCartFlowTests(TestingWebAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureSchema();
        _client = factory.CreateClient();
    }

    // ---- Cart lifecycle ----

    [Fact]
    public async Task AddToCartStartsACartAndIncrementsTheLine()
    {
        var (buyerToken, _, productId) = await SeedDomesticTradeAsync("add");
        await AddAsync(buyerToken, productId, quantity: 1);

        var cart = await MineAsync(buyerToken);
        var line = Assert.Single(cart.Items);
        Assert.Equal(productId, line.ProductId);
        Assert.Equal(1m, line.Quantity);
        Assert.Equal(120m, line.Price);
        Assert.Equal(120m, line.LineTotal);
        Assert.Equal(15m, line.DeliveryFee);
        Assert.True(line.IsAvailable);
        Assert.Equal(1, cart.TotalQuantity);

        // Adding the same listing again increments rather than duplicating the line.
        await AddAsync(buyerToken, productId, quantity: 3);
        var grown = await MineAsync(buyerToken);
        var grownLine = Assert.Single(grown.Items);
        Assert.Equal(4m, grownLine.Quantity);
        Assert.Equal(480m, grownLine.LineTotal);
        Assert.Equal(4, grown.TotalQuantity);
    }

    [Fact]
    public async Task SetQuantityAndRemoveKeepTheCartConsistent()
    {
        var (buyerToken, sellerToken, productId) = await SeedDomesticTradeAsync("edit");
        var (secondId, _) = await CreateDomesticProductAsync(sellerToken, "Cart Edits Second", price: 40m, deliveryFee: 5m);

        await AddAsync(buyerToken, productId, quantity: 2);
        await AddAsync(buyerToken, secondId, quantity: 1);

        var cart = await PutCartAsync(buyerToken, productId, quantity: 5);
        var lines = cart.Items.OrderBy(i => i.ProductName).ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal(5m, lines.Single(l => l.ProductId == productId).Quantity);
        Assert.Equal(40m, lines.Single(l => l.ProductId == secondId).Price);

        var afterRemove = await DeleteCartAsync(buyerToken, secondId);
        Assert.Single(afterRemove.Items);
        Assert.Equal(productId, afterRemove.Items[0].ProductId);
        Assert.Equal(5, afterRemove.TotalQuantity);

        var emptied = await DeleteCartAsync(buyerToken, productId);
        Assert.Empty(emptied.Items);
        Assert.Equal(0, emptied.TotalQuantity);

        // Removing something already gone stays idempotent: no error, still an empty cart.
        var again = await DeleteCartAsync(buyerToken, productId);
        Assert.Empty(again.Items);
    }

    [Fact]
    public async Task SettingQuantityOnAnAbsentLineIs404()
    {
        var (buyerToken, _, _) = await SeedDomesticTradeAsync("absent");
        var response = await PutAuthenticatedAsync(
            $"/api/v1/carts/items/{Guid.NewGuid()}", buyerToken, new { quantity = 1m });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("CART_ITEM_NOT_FOUND", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ZeroQuantityIsRefusedByValidation()
    {
        var (buyerToken, _, productId) = await SeedDomesticTradeAsync("zero");
        var add = await PostAuthenticatedAsync("/api/v1/carts/items", buyerToken, new
        {
            productId,
            quantity = 0m,
        });
        Assert.Equal(HttpStatusCode.BadRequest, add.StatusCode);

        await AddAsync(buyerToken, productId, quantity: 1);
        var set = await PutAuthenticatedAsync(
            $"/api/v1/carts/items/{productId}", buyerToken, new { quantity = -2m });
        Assert.Equal(HttpStatusCode.BadRequest, set.StatusCode);
        Assert.Contains("Cart quantity must be at least 1", await set.Content.ReadAsStringAsync());
    }

    // ---- What can go in a cart ----

    [Fact]
    public async Task AddingAnExportOnlyListingIsRefused()
    {
        var (buyerToken, sellerToken, _) = await SeedDomesticTradeAsync("export");
        var exportId = await SeedProductAsync(sellerToken, "Cart Refuses Export", sellsDomestically: false);

        var response = await PostAuthenticatedAsync("/api/v1/carts/items", buyerToken, new
        {
            productId = exportId,
            quantity = 1m,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("PRODUCT_NOT_DOMESTIC", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AddingMoreThanAvailableIsRefused()
    {
        var (buyerToken, _, productId) = await SeedDomesticTradeAsync("over");
        var response = await PostAuthenticatedAsync("/api/v1/carts/items", buyerToken, new
        {
            productId,
            quantity = 101m, // seeded available = 100
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("QUANTITY_EXCEEDS_AVAILABLE", await response.Content.ReadAsStringAsync());
        Assert.Empty((await MineAsync(buyerToken)).Items);
    }

    [Fact]
    public async Task AddingAnUnknownProductIs404()
    {
        var (buyerToken, _, _) = await SeedDomesticTradeAsync("unknown");
        var response = await PostAuthenticatedAsync("/api/v1/carts/items", buyerToken, new
        {
            productId = Guid.NewGuid(),
            quantity = 1m,
        });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("PRODUCT_NOT_FOUND", await response.Content.ReadAsStringAsync());
    }

    // ---- Checkout ----

    [Fact]
    public async Task CheckoutFansTheCartOutIntoOneOrderPerSellerWithDeliveryFees()
    {
        var sellerA = await VerifyBusinessAsync(await CreateCompanyUserAsync($"cart_a_{Guid.NewGuid():N}"));
        var sellerB = await VerifyBusinessAsync(await CreateCompanyUserAsync($"cart_b_{Guid.NewGuid():N}"));
        var buyerToken = await RegisterAndLoginAsync($"cart_buyer_{Guid.NewGuid():N}");

        var (a1, _) = await CreateDomesticProductAsync(sellerA, "A Grain", price: 100m, deliveryFee: 10m);
        var (a2, _) = await CreateDomesticProductAsync(sellerA, "A Pulse", price: 200m, deliveryFee: 25m);
        var (b1, _) = await CreateDomesticProductAsync(sellerB, "B Rice", price: 50m, deliveryFee: 20m);

        await AddAsync(buyerToken, a1, quantity: 3); // 300 + 10
        await AddAsync(buyerToken, a2, quantity: 1); // 200 + 25
        await AddAsync(buyerToken, b1, quantity: 2); // 100 + 20

        var result = await CheckoutAsync(buyerToken, country: "NG");
        Assert.Equal(2, result.Orders.Count);

        // One order per seller, numbers allocated in one gap-free block, buyer's totals carry
        // the summed delivery fee. Keyed on line count: seller A sold two lines, seller B one.
        var orderA = Assert.Single(result.Orders, o => o.ItemCount == 2);
        Assert.Equal(500m, orderA.Subtotal);
        Assert.Equal(35m, orderA.DeliveryFee);
        Assert.Equal(535m, orderA.Total);
        Assert.Equal(2, orderA.ItemCount);

        var orderB = Assert.Single(result.Orders, o => o.ItemCount == 1);
        Assert.Equal(100m, orderB.Subtotal);
        Assert.Equal(20m, orderB.DeliveryFee);
        Assert.Equal(120m, orderB.Total);
        Assert.Equal(1, orderB.ItemCount);

        Assert.NotEqual(orderA.OrderNumber, orderB.OrderNumber);
        Assert.StartsWith($"AG-{DateTime.UtcNow.Year}-", orderA.OrderNumber);
        Assert.StartsWith($"AG-{DateTime.UtcNow.Year}-", orderB.OrderNumber);

        // The orders on disk carry the terms a doorstep delivery actually has: DAP, road,
        // destination = the seller's own country, and no export-machinery incoterms or ports.
        var detailA = await GetOrderAsync(orderA.OrderId, buyerToken);
        Assert.Equal(OrderStatus.Placed, detailA.Status);
        Assert.Equal(Incoterms.Dap, detailA.Incoterms);
        Assert.Equal(ShipmentMode.Road, detailA.TransportMode);
        Assert.Equal("NG", detailA.DestinationCountry);
        Assert.Equal(PaymentTerms.TtAdvance, detailA.PaymentTerms);
        Assert.Equal(535m, detailA.Total);
        Assert.Equal(35m, detailA.DeliveryFee);
        Assert.True(detailA.ExpectedDeliveryUtc > DateTime.UtcNow);

        // The cart became orders: it must not be placeable a second time.
        Assert.Empty((await MineAsync(buyerToken)).Items);
        var placedTwice = await PostAuthenticatedAsync("/api/v1/carts/checkout", buyerToken, new
        {
            destination = new { line1 = "1 Ikorodu Road", city = "Lagos", country = "NG" },
        });
        Assert.Equal(HttpStatusCode.BadRequest, placedTwice.StatusCode);
        Assert.Contains("CART_EMPTY", await placedTwice.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CheckoutReservesStockForEverySellerAtomically()
    {
        var (buyerToken, _, productId) = await SeedDomesticTradeAsync("reserve");
        await AddAsync(buyerToken, productId, quantity: 4);

        var before = await GetProductAsync(productId);
        Assert.Equal(100m, before.AvailableQuantity);

        var result = await CheckoutAsync(buyerToken, country: "NG");
        var order = Assert.Single(result.Orders);
        Assert.Equal(1, order.ItemCount); // one line, quantity 4 — ItemCount counts lines

        // The reservation lived in the same change set as the order: availability drops by
        // exactly what was bought and the order number exists together with it.
        var after = await GetProductAsync(productId);
        Assert.Equal(96m, after.AvailableQuantity);
        Assert.NotEmpty(order.OrderNumber);
    }

    [Fact]
    public async Task CheckoutRefusesWhenStockRunsDryAfterAdding()
    {
        var (buyerToken, sellerToken, productId) = await SeedDomesticTradeAsync("drained");
        await AddAsync(buyerToken, productId, quantity: 5);

        // The seller lowers the level to 2 before the buyer pays — the cart is a promise, and
        // the checkout must re-check rather than let a reservation be taken it cannot honour.
        (await PutAuthenticatedAsync(
            $"/api/v1/products/{productId}/stock", sellerToken, new { quantityOnHand = 2m })).EnsureSuccessStatusCode();

        var response = await PostAuthenticatedAsync("/api/v1/carts/checkout", buyerToken, CheckoutBody("NG"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("INSUFFICIENT_STOCK", await response.Content.ReadAsStringAsync());

        // Nothing was reserved and the cart survived intact for the buyer to fix.
        var stock = await GetAuthenticatedAsync($"/api/v1/products/{productId}/stock", buyerToken);
        var inventory = (await stock.Content.ReadFromJsonAsync<InventoryMirror>())!;
        Assert.Equal(2m, inventory.QuantityOnHand);
        Assert.Equal(0m, inventory.QuantityReserved);
        Assert.Equal(5m, Assert.Single((await MineAsync(buyerToken)).Items).Quantity);
    }

    [Fact]
    public async Task CheckoutRefusesWhenAListingIsNoLongerDomestic()
    {
        var (buyerToken, sellerToken, productId) = await SeedDomesticTradeAsync("dedomestic");
        await AddAsync(buyerToken, productId, quantity: 2);

        // The seller turns domestic selling off between the add and the checkout.
        (await PutAuthenticatedAsync($"/api/v1/products/{productId}", sellerToken, new
        {
            sellsDomestically = false,
        })).EnsureSuccessStatusCode();

        var response = await PostAuthenticatedAsync("/api/v1/carts/checkout", buyerToken, CheckoutBody("NG"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("PRODUCT_NOT_DOMESTIC", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CheckoutRefusesADeliveryAddressOutsideTheSellersCountry()
    {
        var (buyerToken, _, productId) = await SeedDomesticTradeAsync("border");
        await AddAsync(buyerToken, productId, quantity: 1);

        // "Domestic" is fixed by the seller's origin country: a Ghanaian address against a
        // Nigerian kitchen is an export, and exports are a different order entirely.
        var response = await PostAuthenticatedAsync("/api/v1/carts/checkout", buyerToken, CheckoutBody("GH"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("DELIVERY_COUNTRY_MISMATCH", await response.Content.ReadAsStringAsync());
        Assert.Single((await MineAsync(buyerToken)).Items);
    }

    [Fact]
    public async Task CheckoutRefusesTheSellersOwnCart()
    {
        // A user holding both a seller role and a buyer role cannot buy from themselves:
        // a self-sale would consume their own stock and issue a notification to themselves.
        var token = await CreateCompanyUserAsync($"self_sale_{Guid.NewGuid():N}", extraRoles: [RoleNames.Importer]);
        var sellerToken = await VerifyBusinessAsync(token);
        var (productId, _) = await CreateDomesticProductAsync(sellerToken, "Self Sale Grain", price: 10m, deliveryFee: 2m);

        await AddAsync(token, productId, quantity: 1);
        var response = await PostAuthenticatedAsync("/api/v1/carts/checkout", token, CheckoutBody("NG"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ORDER_SELF_SALE", await response.Content.ReadAsStringAsync());
        Assert.Single((await MineAsync(token)).Items);
    }

    [Fact]
    public async Task CheckoutRefusesAnEmptyCart()
    {
        var (buyerToken, _, _) = await SeedDomesticTradeAsync("empty");
        var response = await PostAuthenticatedAsync("/api/v1/carts/checkout", buyerToken, CheckoutBody("NG"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("CART_EMPTY", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CheckoutRequiresAValidDeliveryAddress()
    {
        var (buyerToken, _, productId) = await SeedDomesticTradeAsync("address");
        await AddAsync(buyerToken, productId, quantity: 1);

        var response = await PostAuthenticatedAsync("/api/v1/carts/checkout", buyerToken, new
        {
            destination = new { line1 = "", city = "", country = "NG" },
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Destination.Line1", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SellersGetNotifiedOfDomesticOrders()
    {
        var (buyerToken, sellerToken, productId) = await SeedDomesticTradeAsync("notify");
        await AddAsync(buyerToken, productId, quantity: 1);
        await CheckoutAsync(buyerToken, country: "NG");

        var notifications = await GetAuthenticatedAsync("/api/v1/me/notifications?pageSize=100", sellerToken);
        notifications.EnsureSuccessStatusCode();
        using var doc = System.Text.Json.JsonDocument.Parse(await notifications.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var items = root.ValueKind == System.Text.Json.JsonValueKind.Array
            ? root.EnumerateArray().ToArray()
            : root.GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(items, i =>
            i.GetProperty("type").GetInt32() == (int)NotificationType.Order
            && i.GetProperty("title").GetString()!.StartsWith("New order AG-2026-"));
    }

    // ---- Authorization ----

    [Fact]
    public async Task CartEndpointsNeedABuyerRole()
    {
        // Anonymous readers are not even recognised as a buyer.
        var anonymous = await _client.GetAsync("/api/v1/carts/mine");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        // A Company-only account is a seller, not a buyer: the cart is a buyer surface.
        var sellerToken = await CreateCompanyUserAsync($"guard_seller_{Guid.NewGuid():N}");
        var forbidden = await GetAuthenticatedAsync("/api/v1/carts/mine", sellerToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    // ---- Seeding ----

    /// <summary>One verified seller with one domestic listing, plus a buyer with a token.</summary>
    private async Task<(string BuyerToken, string SellerToken, Guid ProductId)> SeedDomesticTradeAsync(string tag)
    {
        var sellerToken = await VerifyBusinessAsync(
            await CreateCompanyUserAsync($"cart_{tag}_{Guid.NewGuid():N}"));
        var (productId, _) = await CreateDomesticProductAsync(sellerToken, $"Cart {tag} Grain", price: 120m, deliveryFee: 15m);
        var buyerToken = await RegisterAndLoginAsync($"cart_{tag}_buyer_{Guid.NewGuid():N}");
        return (buyerToken, sellerToken, productId);
    }

    private async Task<string> CreateCompanyUserAsync(string prefix, string[]? extraRoles = null)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{prefix}@cart.tests.agrotrade.local";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
        };
        var created = await users.CreateAsync(user, Password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, RoleNames.Company);
        foreach (var role in extraRoles ?? [])
        {
            await users.AddToRoleAsync(user, role);
        }
        return await LoginAsync(email, Password);
    }

    private async Task<string> RegisterAndLoginAsync(string prefix)
    {
        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = $"{prefix}@cart.tests.agrotrade.local",
            password = Password,
            displayName = prefix,
            homeCountry = "NG",
            defaultCurrency = "USD",
            roles = new[] { RoleNames.Importer },
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        return (await register.Content.ReadFromJsonAsync<AuthResponseMirror>())!.AccessToken;
    }

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponseMirror>())!.AccessToken;
    }

    private async Task<string> VerifyBusinessAsync(string token)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var registered = await PostAuthenticatedAsync("/api/v1/businesses", token, new
        {
            legalName = $"Cart Supplier {tag} Ltd",
            tradingName = $"Cart Seller {tag}",
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

    private async Task<Guid> SupplierBusinessIdAsync(string token)
    {
        var response = await GetAuthenticatedAsync("/api/v1/businesses/mine", token);
        response.EnsureSuccessStatusCode();
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var first = root.ValueKind == System.Text.Json.JsonValueKind.Array ? root[0] : root.GetProperty("items")[0];
        return first.GetProperty("id").GetGuid();
    }

    /// <summary>Creates a domestic listing under a verified seller and returns (id, name).</summary>
    private Task<(Guid Id, string Name)> CreateDomesticProductAsync(
        string sellerToken, string name, decimal price, decimal deliveryFee, decimal available = 100m) =>
        CreateProductAsync(sellerToken, name, price, available, sellsDomestically: true, deliveryFee);

    /// <summary>Creates a product under a verified seller; <paramref name="sellsDomestically"/> defaults to true.</summary>
    private async Task<(Guid Id, string Name)> CreateProductAsync(
        string sellerToken, string name, decimal price, decimal available = 100m,
        bool sellsDomestically = true, decimal deliveryFee = 0m)
    {
        var (id, fullName) = await SeedProductAsync(sellerToken, name, sellsDomestically, price, available, deliveryFee);
        return (id, fullName);
    }

    /// <summary>Creates a product under a verified seller with the given flags and returns its id.</summary>
    private async Task<Guid> SeedProductAsync(string sellerToken, string name, bool sellsDomestically)
    {
        var (id, _) = await SeedProductAsync(sellerToken, name, sellsDomestically, price: 100m, available: 100m, deliveryFee: 0m);
        return id;
    }

    private async Task<(Guid Id, string Name)> SeedProductAsync(
        string sellerToken, string name, bool sellsDomestically, decimal price, decimal available, decimal deliveryFee)
    {
        var category = await PickCategoryAsync();
        var fullName = $"{name} {Guid.NewGuid():N}";
        var response = await PostAuthenticatedAsync("/api/v1/products", sellerToken, new
        {
            name = fullName,
            categoryId = category.Id,
            originCountry = "NG",
            portOfLoading = "Lagos (LOS)",
            price,
            priceUnit = (int)PriceUnit.PerTon,
            currency = "USD",
            availableQuantity = available,
            minimumOrderQuantity = 1m,
            leadTimeDays = 5,
            sellsDomestically,
            domesticDeliveryFee = deliveryFee,
            listingStatus = (int)ListingStatus.Active,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<ProductMirror>())!.Id;
        return (id, fullName);
    }

    /// <summary>The shared test database seeds categories through the DbContext (see AdminDashboardFlowTests).</summary>
    private async Task<CategoryMirror> PickCategoryAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Data.AgroTradeDbContext>();

        if (!await db.Categories.AnyAsync())
        {
            foreach (var name in new[] { "Cocoa", "Rubber", "Grains", "Coffee" })
            {
                db.Categories.Add(new Domain.Trade.Category
                {
                    Name = name,
                    Slug = name.ToLowerInvariant(),
                    IsActive = true,
                });
            }
            await db.SaveChangesAsync();
        }

        var all = await db.Categories.AsNoTracking().ToListAsync();
        var parentIds = all.Where(c => c.ParentId.HasValue).Select(c => c.ParentId!.Value).ToHashSet();
        var leaf = all.First(c => !parentIds.Contains(c.Id));
        return new CategoryMirror { Id = leaf.Id };
    }

    private static object CheckoutBody(string country) => new
    {
        destination = new
        {
            line1 = "1 Ikorodu Road",
            line2 = "Mushin",
            city = "Lagos",
            state = "Lagos",
            country,
            postalCode = "100212",
        },
        notes = "Deliver weekday mornings.",
    };

    // ---- Shopping helpers ----

    private async Task AddAsync(string token, Guid productId, decimal quantity)
    {
        var response = await PostAuthenticatedAsync("/api/v1/carts/items", token, new { productId, quantity });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<CartMirror> MineAsync(string token)
    {
        var response = await GetAuthenticatedAsync("/api/v1/carts/mine", token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CartMirror>() ?? new CartMirror();
    }

    private async Task<CartMirror> PutCartAsync(string token, Guid productId, decimal quantity)
    {
        var response = await PutAuthenticatedAsync($"/api/v1/carts/items/{productId}", token, new { quantity });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CartMirror>())!;
    }

    private async Task<CartMirror> DeleteCartAsync(string token, Guid productId)
    {
        var response = await DeleteAuthenticatedAsync($"/api/v1/carts/items/{productId}", token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CartMirror>())!;
    }

    private async Task<CheckoutResultMirror> CheckoutAsync(string token, string country)
    {
        var response = await PostAuthenticatedAsync("/api/v1/carts/checkout", token, CheckoutBody(country));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CheckoutResultMirror>())!;
    }

    private async Task<OrderDetailMirror> GetOrderAsync(Guid orderId, string token)
    {
        var response = await GetAuthenticatedAsync($"/api/v1/orders/{orderId}", token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderDetailMirror>())!;
    }

    private async Task<ProductMirror> GetProductAsync(Guid productId)
    {
        var response = await _client.GetAsync($"/api/v1/products/{productId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductMirror>())!;
    }

    private Task<string> AdminTokenAsync() =>
        LoginAsync(TestingWebAppFactory.AdminEmail, TestingWebAppFactory.AdminPassword);

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

    private Task<HttpResponseMessage> DeleteAuthenticatedAsync(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private static ByteArrayContent PdfPart()
    {
        var part = new ByteArrayContent("%PDF-1.4 registration"u8.ToArray());
        part.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return part;
    }

    // ---- DTO mirrors ----

    public sealed class AuthResponseMirror
    {
        public string AccessToken { get; init; } = "";
    }

    public sealed class ProductMirror
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = "";
        public decimal AvailableQuantity { get; init; }
    }

    public sealed class CategoryMirror
    {
        public Guid Id { get; init; }
    }

    public sealed class CartMirror
    {
        public List<CartItemMirror> Items { get; init; } = [];
        public int TotalQuantity { get; init; }
    }

    public sealed class CartItemMirror
    {
        public Guid ProductId { get; init; }
        public string ProductName { get; init; } = "";
        public decimal Price { get; init; }
        public int PriceUnit { get; init; }
        public string Currency { get; init; } = "";
        public decimal Quantity { get; init; }
        public decimal LineTotal { get; init; }
        public decimal DeliveryFee { get; init; }
        public decimal AvailableQuantity { get; init; }
        public Guid SellerBusinessId { get; init; }
        public string SellerTradingName { get; init; } = "";
        public string? PrimaryImageUrl { get; init; }
        public bool IsAvailable { get; init; }
    }

    public sealed class CheckoutResultMirror
    {
        public List<CheckoutOrderMirror> Orders { get; init; } = [];
    }

    public sealed class CheckoutOrderMirror
    {
        public Guid OrderId { get; init; }
        public string OrderNumber { get; init; } = "";
        public Guid SellerBusinessId { get; init; }
        public string SellerTradingName { get; init; } = "";
        public string Currency { get; init; } = "";
        public decimal Subtotal { get; init; }
        public decimal DeliveryFee { get; init; }
        public decimal Total { get; init; }
        public int ItemCount { get; init; }
        public DateTime? ExpectedDeliveryUtc { get; init; }
    }

    public sealed class OrderDetailMirror
    {
        public Guid Id { get; init; }
        public string OrderNumber { get; init; } = "";
        public OrderStatus Status { get; init; }
        public Incoterms Incoterms { get; init; }
        public ShipmentMode? TransportMode { get; init; }
        public string? DestinationCountry { get; init; }
        public PaymentTerms PaymentTerms { get; init; }
        public decimal Subtotal { get; init; }
        public decimal DeliveryFee { get; init; }
        public decimal Total { get; init; }
        public DateTime? ExpectedDeliveryUtc { get; init; }
    }

    public sealed class InventoryMirror
    {
        public decimal QuantityOnHand { get; init; }
        public decimal QuantityReserved { get; init; }
    }
}