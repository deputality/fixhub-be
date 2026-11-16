using System.Net;
using FixHub.Api.Common;
using FixHub.Api.DTOs;
using Xunit;

namespace FixHub.Tests.Integration;

[Collection("api")]
public class CartTests : IntegrationTestBase
{
    public CartTests(ApiFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Adding_same_service_twice_increases_quantity_and_calculates_total()
    {
        var categoryId = await CreateCategoryAsync();
        var (performer, _) = await RegisterAsync("Performer");
        var service = await CreateServiceAsync(performer, categoryId, 40);
        var (client, _) = await RegisterAsync("Client");

        Assert.Equal(HttpStatusCode.OK, (await AddToCartAsync(client, service.Id, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AddToCartAsync(client, service.Id, 2)).StatusCode);

        var response = await Client.CallAsync(HttpMethod.Get, "/api/cart", token: client);
        var cart = await response.ReadAsync<CartDto>();

        var item = Assert.Single(cart.Items);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(120m, item.LineTotal);
        Assert.Equal(120m, cart.TotalAmount);
    }

    [Fact]
    public async Task Hidden_service_cannot_be_added_to_cart()
    {
        var categoryId = await CreateCategoryAsync();
        var (performer, _) = await RegisterAsync("Performer");
        var service = await CreateServiceAsync(performer, categoryId, 40);
        var (client, _) = await RegisterAsync("Client");

        await Client.CallAsync(HttpMethod.Patch,
            $"/api/performer/services/{service.Id}/hide", token: performer);

        var response = await AddToCartAsync(client, service.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Performer_cannot_use_cart()
    {
        var (performer, _) = await RegisterAsync("Performer");

        var response = await Client.CallAsync(HttpMethod.Get, "/api/cart", token: performer);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Client_cannot_change_foreign_cart_item()
    {
        var categoryId = await CreateCategoryAsync();
        var (performer, _) = await RegisterAsync("Performer");
        var service = await CreateServiceAsync(performer, categoryId, 40);
        var (owner, _) = await RegisterAsync("Client");
        var (stranger, _) = await RegisterAsync("Client");

        var add = await AddToCartAsync(owner, service.Id, 1);
        var itemId = Assert.Single((await add.ReadAsync<CartDto>()).Items).Id;

        var update = await Client.CallAsync(HttpMethod.Put, $"/api/cart/items/{itemId}",
            new { quantity = 5 }, stranger);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);

        var delete = await Client.CallAsync(HttpMethod.Delete, $"/api/cart/items/{itemId}", token: stranger);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        // у владельца позиция осталась без изменений
        var cart = await (await Client.CallAsync(HttpMethod.Get, "/api/cart", token: owner)).ReadAsync<CartDto>();
        Assert.Equal(1, Assert.Single(cart.Items).Quantity);
    }

    [Fact]
    public async Task Zero_quantity_is_rejected_with_details()
    {
        var categoryId = await CreateCategoryAsync();
        var (performer, _) = await RegisterAsync("Performer");
        var service = await CreateServiceAsync(performer, categoryId, 40);
        var (client, _) = await RegisterAsync("Client");

        var response = await Client.CallAsync(HttpMethod.Post, "/api/cart/items",
            new { serviceId = service.Id, quantity = 0 }, client);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.ReadAsync<ApiError>();
        Assert.Equal(400, error.Status);
        Assert.NotNull(error.Details);
    }
}