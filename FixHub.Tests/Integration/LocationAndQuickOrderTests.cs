using System.Net;
using FixHub.Api.Common;
using FixHub.Api.DTOs;
using Xunit;

namespace FixHub.Tests.Integration;

[Collection("api")]
public class LocationAndQuickOrderTests : IntegrationTestBase
{
    public LocationAndQuickOrderTests(ApiFixture fixture) : base(fixture)
    {
    }

    private async Task<PagedResult<ServiceDto>> GetCatalogAsync(string query)
    {
        var response = await Client.CallAsync(HttpMethod.Get, $"/api/catalog?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<PagedResult<ServiceDto>>();
    }

    private Task<HttpResponseMessage> SetLocationAsync(
        string performerToken, string? city, string? area, double? lat, double? lng) =>
        Client.CallAsync(HttpMethod.Put, "/api/profile", new
        {
            fullName = "Test Performer",
            city,
            serviceArea = area,
            latitude = lat,
            longitude = lng
        }, performerToken);

    // ---------- Поиск по городу и району ----------

    [Fact]
    public async Task Catalog_filters_by_city_and_service_area_case_insensitively()
    {
        var categoryId = await CreateCategoryAsync();
        var city = $"City{Guid.NewGuid():N}"[..14];

        var (inCity, _) = await RegisterAsync("Performer");
        var (inArea, _) = await RegisterAsync("Performer");
        var (elsewhere, _) = await RegisterAsync("Performer");

        Assert.Equal(HttpStatusCode.OK, (await SetLocationAsync(inCity, city, null, null, null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetLocationAsync(inArea, "Other", $"district {city}", null, null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetLocationAsync(elsewhere, "Elsewhere", null, null, null)).StatusCode);

        var a = await CreateServiceAsync(inCity, categoryId);
        var b = await CreateServiceAsync(inArea, categoryId);
        await CreateServiceAsync(elsewhere, categoryId);

        var found = await GetCatalogAsync($"categoryId={categoryId}&location={Uri.EscapeDataString(city.ToUpperInvariant())}");

        Assert.Equal(new[] { a.Id, b.Id }.OrderBy(x => x), found.Items.Select(i => i.Id).OrderBy(x => x));
        Assert.Contains(found.Items, i => i.PerformerCity == city);
    }

    [Fact]
    public async Task Locations_endpoint_lists_cities_of_visible_services()
    {
        var categoryId = await CreateCategoryAsync();
        var city = $"Town{Guid.NewGuid():N}"[..12];
        var (performer, _) = await RegisterAsync("Performer");
        await SetLocationAsync(performer, city, null, null, null);
        await CreateServiceAsync(performer, categoryId);

        var response = await Client.CallAsync(HttpMethod.Get, "/api/catalog/locations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(city, await response.ReadAsync<List<string>>());
    }

    // ---------- Геопоиск ----------

    [Fact]
    public async Task Catalog_sorts_by_distance_and_filters_by_radius()
    {
        var categoryId = await CreateCategoryAsync();
        var (near, _) = await RegisterAsync("Performer");
        var (far, _) = await RegisterAsync("Performer");
        var (noPoint, _) = await RegisterAsync("Performer");

        // пользователь в центре Минска; «рядом» примерно 2 км, «далеко» около 17 км
        Assert.Equal(HttpStatusCode.OK, (await SetLocationAsync(near, "Минск", null, 53.91, 27.56)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetLocationAsync(far, "Минск", null, 54.05, 27.56)).StatusCode);
        await SetLocationAsync(noPoint, "Минск", null, null, null);

        var nearService = await CreateServiceAsync(near, categoryId);
        var farService = await CreateServiceAsync(far, categoryId);
        var noPointService = await CreateServiceAsync(noPoint, categoryId);

        var sorted = await GetCatalogAsync($"categoryId={categoryId}&lat=53.90&lng=27.56&sort=Distance");
        Assert.Equal(
            new[] { nearService.Id, farService.Id, noPointService.Id },
            sorted.Items.Select(i => i.Id));

        Assert.InRange(sorted.Items[0].DistanceKm!.Value, 0.5, 2.5);
        Assert.InRange(sorted.Items[1].DistanceKm!.Value, 14, 18);
        Assert.Null(sorted.Items[2].DistanceKm);

        // радиус 5 км оставляет только ближнего; мастер без точки не попадает
        var radius = await GetCatalogAsync($"categoryId={categoryId}&lat=53.90&lng=27.56&radiusKm=5");
        Assert.Equal(nearService.Id, Assert.Single(radius.Items).Id);

        // без точки пользователя расстояние не считается
        var plain = await GetCatalogAsync($"categoryId={categoryId}");
        Assert.All(plain.Items, i => Assert.Null(i.DistanceKm));
    }

    [Theory]
    [InlineData("sort=Distance")]
    [InlineData("radiusKm=10")]
    [InlineData("lat=53.9")]
    [InlineData("lng=27.5")]
    public async Task Distance_query_without_full_point_returns_400(string query)
    {
        var response = await Client.CallAsync(HttpMethod.Get, $"/api/catalog?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(400, (await response.ReadAsync<ApiError>()).Status);
    }

    [Fact]
    public async Task Profile_rounds_coordinates_and_requires_both()
    {
        var (performer, performerId) = await RegisterAsync("Performer");

        var saved = await SetLocationAsync(performer, "Минск", null, 53.904321, 27.561234);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var profile = await saved.ReadAsync<ProfileDto>();
        Assert.Equal(53.90, profile.Latitude);
        Assert.Equal(27.56, profile.Longitude);

        // публичная страница мастера отдаёт ту же округлённую точку
        var page = await (await Client.CallAsync(HttpMethod.Get, $"/api/performers/{performerId}"))
            .ReadAsync<PerformerPublicDto>();
        Assert.Equal(53.90, page.Latitude);
        Assert.Equal(27.56, page.Longitude);

        var half = await SetLocationAsync(performer, "Минск", null, 53.9, null);
        Assert.Equal(HttpStatusCode.BadRequest, half.StatusCode);

        var outOfRange = await SetLocationAsync(performer, "Минск", null, 123, 27.5);
        Assert.Equal(HttpStatusCode.BadRequest, outOfRange.StatusCode);

        // можно убрать точку совсем
        var cleared = await (await SetLocationAsync(performer, "Минск", null, null, null)).ReadAsync<ProfileDto>();
        Assert.Null(cleared.Latitude);
        Assert.Null(cleared.Longitude);
    }

    // ---------- Заказ без корзины ----------

    private Task<HttpResponseMessage> QuickOrderAsync(string? token, int serviceId) =>
        Client.CallAsync(HttpMethod.Post, "/api/orders/quick", new
        {
            serviceId,
            quantity = 2,
            address = "Минск, ул. Тестовая, 1",
            contactName = "Test Client",
            contactPhone = "+375291234567",
            comment = "  Позвоните заранее  "
        }, token);

    [Fact]
    public async Task Quick_order_creates_order_without_touching_cart()
    {
        var categoryId = await CreateCategoryAsync();
        var (performerToken, performerId) = await RegisterAsync("Performer");
        var service = await CreateServiceAsync(performerToken, categoryId, price: 40);
        var other = await CreateServiceAsync(performerToken, categoryId, price: 10);
        var (clientToken, _) = await RegisterAsync("Client");

        // в корзине уже лежит другая услуга: быстрый заказ не должен её оформить
        Assert.Equal(HttpStatusCode.OK, (await AddToCartAsync(clientToken, other.Id)).StatusCode);

        var response = await QuickOrderAsync(clientToken, service.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var order = await response.ReadAsync<OrderDto>();
        Assert.Equal("New", order.Status);
        Assert.Equal(performerId, order.PerformerId);
        Assert.Equal(80m, order.TotalAmount);
        var item = Assert.Single(order.Items);
        Assert.Equal(service.Id, item.ServiceId);
        Assert.Equal(40m, item.UnitPrice);
        Assert.Equal(2, item.Quantity);
        Assert.Equal("Позвоните заранее", item.Comment);

        var cart = await (await Client.CallAsync(HttpMethod.Get, "/api/cart", token: clientToken))
            .ReadAsync<CartDto>();
        Assert.Equal(other.Id, Assert.Single(cart.Items).ServiceId);

        // заказ виден мастеру, там же есть первая запись истории
        var incoming = await (await Client.CallAsync(HttpMethod.Get, "/api/performer/orders", token: performerToken))
            .ReadAsync<List<OrderDto>>();
        Assert.Contains(incoming, o => o.Id == order.Id);

        var full = await (await Client.CallAsync(HttpMethod.Get, $"/api/orders/{order.Id}", token: clientToken))
            .ReadAsync<OrderDto>();
        Assert.Single(full.History);
    }

    [Fact]
    public async Task Quick_order_is_rejected_for_invalid_cases()
    {
        var categoryId = await CreateCategoryAsync();
        var (performerToken, _) = await RegisterAsync("Performer");
        var service = await CreateServiceAsync(performerToken, categoryId);
        var (clientToken, _) = await RegisterAsync("Client");

        // гость
        Assert.Equal(HttpStatusCode.Unauthorized, (await QuickOrderAsync(null, service.Id)).StatusCode);

        // мастер не может заказывать
        Assert.Equal(HttpStatusCode.Forbidden, (await QuickOrderAsync(performerToken, service.Id)).StatusCode);

        // несуществующая услуга
        Assert.Equal(HttpStatusCode.NotFound, (await QuickOrderAsync(clientToken, 999_999)).StatusCode);

        // дата в прошлом
        var past = await Client.CallAsync(HttpMethod.Post, "/api/orders/quick", new
        {
            serviceId = service.Id,
            desiredDate = DateTime.UtcNow.AddDays(-10).ToString("yyyy-MM-dd"),
            address = "Минск",
            contactName = "Test",
            contactPhone = "+375291234567"
        }, clientToken);
        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);

        // без адреса
        var noAddress = await Client.CallAsync(HttpMethod.Post, "/api/orders/quick", new
        {
            serviceId = service.Id,
            contactName = "Test",
            contactPhone = "+375291234567"
        }, clientToken);
        Assert.Equal(HttpStatusCode.BadRequest, noAddress.StatusCode);

        // скрытая услуга недоступна для заказа
        var hide = await Client.CallAsync(HttpMethod.Patch,
            $"/api/performer/services/{service.Id}/hide", token: performerToken);
        Assert.Equal(HttpStatusCode.OK, hide.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await QuickOrderAsync(clientToken, service.Id)).StatusCode);
    }

    // ---------- Страницы для превью ссылок ----------

    [Fact]
    public async Task Share_page_for_service_has_open_graph_tags_and_escapes_html()
    {
        var categoryId = await CreateCategoryAsync();
        var (performerToken, _) = await RegisterAsync("Performer");
        var service = await CreateServiceAsync(performerToken, categoryId, 55, "Чистка <b>\"кондиционера\"</b>");

        var response = await Client.CallAsync(HttpMethod.Get, $"/share/services/{service.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/html", response.Content.Headers.ContentType!.MediaType);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("property=\"og:title\"", html);
        Assert.Contains("property=\"og:image\"", html);
        Assert.Contains($"/services/{service.Id}", html);
        Assert.Contains("от 55 BYN/час", html);

        // пользовательский текст не должен ломать разметку
        Assert.DoesNotContain("<b>", html);
        Assert.Contains("&lt;b&gt;", html);
    }

    [Fact]
    public async Task Share_page_for_performer_has_city_and_link()
    {
        var (performerToken, performerId) = await RegisterAsync("Performer");
        await SetLocationAsync(performerToken, "Гомель", null, null, null);

        var ok = await Client.CallAsync(HttpMethod.Get, $"/share/performers/{performerId}");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var html = await ok.Content.ReadAsStringAsync();
        Assert.Contains("Гомель", html);
        Assert.Contains($"/performers/{performerId}", html);
    }
}
