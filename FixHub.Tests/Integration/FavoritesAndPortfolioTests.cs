using System.Net;
using FixHub.Api.DTOs;
using Xunit;

namespace FixHub.Tests.Integration;

[Collection("api")]
public class FavoritesAndPortfolioTests : IntegrationTestBase
{
    public FavoritesAndPortfolioTests(ApiFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Client_can_add_and_remove_favorite_service_idempotently()
    {
        var categoryId = await CreateCategoryAsync();
        var (performer, _) = await RegisterAsync("Performer");
        var service = await CreateServiceAsync(performer, categoryId);
        var (client, _) = await RegisterAsync("Client");

        var url = $"/api/favorites/services/{service.Id}";

        Assert.Equal(HttpStatusCode.NoContent, (await Client.CallAsync(HttpMethod.Post, url, token: client)).StatusCode);
        // повторное добавление не создаёт дубликат и не падает
        Assert.Equal(HttpStatusCode.NoContent, (await Client.CallAsync(HttpMethod.Post, url, token: client)).StatusCode);

        var favorites = await (await Client.CallAsync(HttpMethod.Get, "/api/favorites", token: client))
            .ReadAsync<FavoritesDto>();
        Assert.Equal(service.Id, Assert.Single(favorites.Services).Id);

        var ids = await (await Client.CallAsync(HttpMethod.Get, "/api/favorites/ids", token: client))
            .ReadAsync<FavoriteIdsDto>();
        Assert.Equal(new[] { service.Id }, ids.ServiceIds);

        Assert.Equal(HttpStatusCode.NoContent, (await Client.CallAsync(HttpMethod.Delete, url, token: client)).StatusCode);

        favorites = await (await Client.CallAsync(HttpMethod.Get, "/api/favorites", token: client))
            .ReadAsync<FavoritesDto>();
        Assert.Empty(favorites.Services);
    }

    [Fact]
    public async Task Hidden_service_disappears_from_favorites_list()
    {
        var categoryId = await CreateCategoryAsync();
        var (performer, _) = await RegisterAsync("Performer");
        var service = await CreateServiceAsync(performer, categoryId);
        var (client, _) = await RegisterAsync("Client");

        await Client.CallAsync(HttpMethod.Post, $"/api/favorites/services/{service.Id}", token: client);
        await Client.CallAsync(HttpMethod.Patch, $"/api/performer/services/{service.Id}/hide", token: performer);

        var favorites = await (await Client.CallAsync(HttpMethod.Get, "/api/favorites", token: client))
            .ReadAsync<FavoritesDto>();

        Assert.Empty(favorites.Services);
    }

    [Fact]
    public async Task Client_can_favorite_performer_but_not_a_client()
    {
        var (performer, performerId) = await RegisterAsync("Performer");
        var (client, clientId) = await RegisterAsync("Client");

        Assert.Equal(HttpStatusCode.NoContent,
            (await Client.CallAsync(HttpMethod.Post, $"/api/favorites/performers/{performerId}", token: client)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await Client.CallAsync(HttpMethod.Post, $"/api/favorites/performers/{clientId}", token: client)).StatusCode);

        var favorites = await (await Client.CallAsync(HttpMethod.Get, "/api/favorites", token: client))
            .ReadAsync<FavoritesDto>();
        Assert.Equal(performerId, Assert.Single(favorites.Performers).Id);
    }

    [Fact]
    public async Task Favorites_require_client_role()
    {
        var (performer, _) = await RegisterAsync("Performer");

        var guest = await Client.CallAsync(HttpMethod.Get, "/api/favorites");
        var asPerformer = await Client.CallAsync(HttpMethod.Get, "/api/favorites", token: performer);

        Assert.Equal(HttpStatusCode.Unauthorized, guest.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asPerformer.StatusCode);
    }

    [Fact]
    public async Task Performer_portfolio_is_public_and_managed_by_owner_only()
    {
        var (performer, performerId) = await RegisterAsync("Performer");
        var (other, _) = await RegisterAsync("Performer");

        var create = await Client.CallAsync(HttpMethod.Post, "/api/performer/portfolio", new
        {
            title = "Ремонт смесителя",
            description = "Заменили картридж",
            beforeUrl = "/uploads/before.jpg",
            afterUrl = "/uploads/after.jpg"
        }, performer);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var item = await create.ReadAsync<PortfolioItemDto>();

        // гость видит портфолио на публичной странице
        var publicList = await (await Client.CallAsync(HttpMethod.Get, $"/api/performers/{performerId}/portfolio"))
            .ReadAsync<List<PortfolioItemDto>>();
        Assert.Equal(item.Id, Assert.Single(publicList).Id);

        // чужую работу нельзя ни изменить, ни удалить
        var update = await Client.CallAsync(HttpMethod.Put, $"/api/performer/portfolio/{item.Id}", new
        {
            title = "Взлом",
            afterUrl = "/uploads/after.jpg"
        }, other);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);

        var delete = await Client.CallAsync(HttpMethod.Delete, $"/api/performer/portfolio/{item.Id}", token: other);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        var ownDelete = await Client.CallAsync(HttpMethod.Delete, $"/api/performer/portfolio/{item.Id}", token: performer);
        Assert.Equal(HttpStatusCode.NoContent, ownDelete.StatusCode);
    }

    [Fact]
    public async Task Portfolio_accepts_only_own_upload_urls()
    {
        var (performer, _) = await RegisterAsync("Performer");

        var response = await Client.CallAsync(HttpMethod.Post, "/api/performer/portfolio", new
        {
            title = "Работа",
            afterUrl = "https://evil.example/pic.jpg"
        }, performer);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Client_cannot_manage_portfolio()
    {
        var (client, _) = await RegisterAsync("Client");

        var response = await Client.CallAsync(HttpMethod.Get, "/api/performer/portfolio", token: client);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
