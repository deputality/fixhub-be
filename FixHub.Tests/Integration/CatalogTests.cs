using System.Net;
using FixHub.Api.Common;
using FixHub.Api.DTOs;
using Xunit;

namespace FixHub.Tests.Integration;

[Collection("api")]
public class CatalogTests : IntegrationTestBase
{
    public CatalogTests(ApiFixture fixture) : base(fixture)
    {
    }

    private async Task<PagedResult<ServiceDto>> GetCatalogAsync(string query)
    {
        var response = await Client.CallAsync(HttpMethod.Get, $"/api/catalog?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<PagedResult<ServiceDto>>();
    }

    [Fact]
    public async Task Hidden_service_leaves_catalog_and_returns_after_show()
    {
        var categoryId = await CreateCategoryAsync();
        var (performer, _) = await RegisterAsync("Performer");
        var title = UniqueTitle();
        var service = await CreateServiceAsync(performer, categoryId, 40, title);

        var search = $"search={Uri.EscapeDataString(title)}&pageSize=50";

        // гость видит активную услугу
        var visible = await GetCatalogAsync(search);
        Assert.Equal(service.Id, Assert.Single(visible.Items).Id);

        // скрываем
        var hide = await Client.CallAsync(HttpMethod.Patch,
            $"/api/performer/services/{service.Id}/hide", token: performer);
        Assert.Equal(HttpStatusCode.OK, hide.StatusCode);
        Assert.False((await hide.ReadAsync<ServiceDto>()).IsActive);

        Assert.Equal(0, (await GetCatalogAsync(search)).TotalCount);

        var card = await Client.CallAsync(HttpMethod.Get, $"/api/catalog/{service.Id}");
        Assert.Equal(HttpStatusCode.NotFound, card.StatusCode);

        // в кабинете исполнителя скрытая услуга остаётся
        var mine = await Client.CallAsync(HttpMethod.Get, "/api/performer/services", token: performer);
        var list = await mine.ReadAsync<List<ServiceDto>>();
        Assert.Contains(list, s => s.Id == service.Id && !s.IsActive);

        // показываем обратно
        var show = await Client.CallAsync(HttpMethod.Patch,
            $"/api/performer/services/{service.Id}/show", token: performer);
        Assert.Equal(HttpStatusCode.OK, show.StatusCode);

        Assert.Equal(service.Id, Assert.Single((await GetCatalogAsync(search)).Items).Id);
    }

    [Fact]
    public async Task Catalog_filters_sorts_and_paginates()
    {
        var categoryId = await CreateCategoryAsync();
        var (performer, _) = await RegisterAsync("Performer");
        await CreateServiceAsync(performer, categoryId, 10);
        await CreateServiceAsync(performer, categoryId, 20);
        await CreateServiceAsync(performer, categoryId, 30);

        var byCategory = $"categoryId={categoryId}";

        var desc = await GetCatalogAsync($"{byCategory}&sort=PriceDesc&pageSize=50");
        Assert.Equal(3, desc.TotalCount);
        Assert.Equal(new[] { 30m, 20m, 10m }, desc.Items.Select(i => i.Price));

        var asc = await GetCatalogAsync($"{byCategory}&sort=PriceAsc&pageSize=50");
        Assert.Equal(new[] { 10m, 20m, 30m }, asc.Items.Select(i => i.Price));

        var range = await GetCatalogAsync($"{byCategory}&minPrice=15&maxPrice=25");
        Assert.Equal(20m, Assert.Single(range.Items).Price);

        var page2 = await GetCatalogAsync($"{byCategory}&sort=PriceAsc&pageSize=2&page=2");
        Assert.Equal(30m, Assert.Single(page2.Items).Price);
        Assert.Equal(3, page2.TotalCount);
        Assert.Equal(2, page2.TotalPages);
    }

    [Fact]
    public async Task Inverted_price_range_returns_400()
    {
        var response = await Client.CallAsync(HttpMethod.Get, "/api/catalog?minPrice=100&maxPrice=10");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(400, (await response.ReadAsync<ApiError>()).Status);
    }

    [Fact]
    public async Task Services_of_blocked_performer_disappear_from_catalog()
    {
        var admin = await LoginAdminAsync();
        var categoryId = await CreateCategoryAsync();
        var (performer, performerId) = await RegisterAsync("Performer");
        var title = UniqueTitle();
        var service = await CreateServiceAsync(performer, categoryId, 40, title);
        var search = $"search={Uri.EscapeDataString(title)}&pageSize=50";

        Assert.Equal(1, (await GetCatalogAsync(search)).TotalCount);

        var block = await Client.CallAsync(HttpMethod.Post,
            $"/api/admin/users/{performerId}/block", token: admin);
        Assert.Equal(HttpStatusCode.OK, block.StatusCode);

        Assert.Equal(0, (await GetCatalogAsync(search)).TotalCount);
        var card = await Client.CallAsync(HttpMethod.Get, $"/api/catalog/{service.Id}");
        Assert.Equal(HttpStatusCode.NotFound, card.StatusCode);

        var unblock = await Client.CallAsync(HttpMethod.Post,
            $"/api/admin/users/{performerId}/unblock", token: admin);
        Assert.Equal(HttpStatusCode.OK, unblock.StatusCode);

        Assert.Equal(1, (await GetCatalogAsync(search)).TotalCount);
    }

    [Fact]
    public async Task Performer_cannot_modify_foreign_service()
    {
        var categoryId = await CreateCategoryAsync();
        var (owner, _) = await RegisterAsync("Performer");
        var (stranger, _) = await RegisterAsync("Performer");
        var title = UniqueTitle();
        var service = await CreateServiceAsync(owner, categoryId, 40, title);

        var update = await Client.CallAsync(HttpMethod.Put, $"/api/performer/services/{service.Id}", new
        {
            categoryId,
            title = "Hacked",
            description = "Hacked",
            price = 1,
            unit = "Hour"
        }, stranger);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);

        var hide = await Client.CallAsync(HttpMethod.Patch,
            $"/api/performer/services/{service.Id}/hide", token: stranger);
        Assert.Equal(HttpStatusCode.NotFound, hide.StatusCode);

        // услуга осталась прежней и активной
        var card = await Client.CallAsync(HttpMethod.Get, $"/api/catalog/{service.Id}");
        Assert.Equal(HttpStatusCode.OK, card.StatusCode);
        var current = await card.ReadAsync<ServiceDto>();
        Assert.Equal(title, current.Title);
        Assert.Equal(40m, current.Price);
    }
}