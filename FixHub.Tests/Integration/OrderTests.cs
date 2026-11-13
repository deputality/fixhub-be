using System.Net;
using FixHub.Api.Common;
using FixHub.Api.DTOs;
using Xunit;

namespace FixHub.Tests.Integration;

[Collection("api")]
public class OrderTests : IntegrationTestBase
{
    public OrderTests(ApiFixture fixture) : base(fixture)
    {
    }

    private record Scenario(
        string ClientToken, string PerformerToken, int PerformerId, ServiceDto Service, OrderDto Order);

    // Исполнитель + услуга + клиент + корзина + оформленный заказ
    private async Task<Scenario> PrepareOrderAsync(decimal price = 50, int quantity = 2)
    {
        var categoryId = await CreateCategoryAsync();
        var (performerToken, performerId) = await RegisterAsync("Performer");
        var service = await CreateServiceAsync(performerToken, categoryId, price);
        var (clientToken, _) = await RegisterAsync("Client");

        var add = await AddToCartAsync(clientToken, service.Id, quantity);
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);

        var checkout = await CheckoutAsync(clientToken);
        Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);

        var orders = await checkout.ReadAsync<List<OrderDto>>();
        return new Scenario(clientToken, performerToken, performerId, service, Assert.Single(orders));
    }

    private async Task<OrderDto> GetOrderAsync(string token, int orderId)
    {
        var response = await Client.CallAsync(HttpMethod.Get, $"/api/orders/{orderId}", token: token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<OrderDto>();
    }

    [Fact]
    public async Task Full_flow_from_cart_to_completed_order_with_history_and_stats()
    {
        var s = await PrepareOrderAsync(price: 50, quantity: 2);

        Assert.Equal("New", s.Order.Status);
        Assert.Equal(100m, s.Order.TotalAmount);
        Assert.Equal(s.PerformerId, s.Order.PerformerId);
        var item = Assert.Single(s.Order.Items);
        Assert.Equal(50m, item.UnitPrice);
        Assert.Equal(2, item.Quantity);

        // корзина после оформления пуста
        var cart = await (await Client.CallAsync(HttpMethod.Get, "/api/cart", token: s.ClientToken))
            .ReadAsync<CartDto>();
        Assert.Empty(cart.Items);

        // клиент и исполнитель видят заказ в своих списках
        var mine = await (await Client.CallAsync(HttpMethod.Get, "/api/orders/my", token: s.ClientToken))
            .ReadAsync<List<OrderDto>>();
        Assert.Contains(mine, o => o.Id == s.Order.Id);

        var incoming = await (await Client.CallAsync(HttpMethod.Get, "/api/performer/orders", token: s.PerformerToken))
            .ReadAsync<List<OrderDto>>();
        Assert.Contains(incoming, o => o.Id == s.Order.Id);

        // цепочка статусов строго по порядку
        foreach (var (action, expected) in new[]
                 {
                     ("confirm", "Confirmed"),
                     ("start", "InProgress"),
                     ("complete", "Completed")
                 })
        {
            var step = await OrderActionAsync(s.PerformerToken, s.Order.Id, action);
            Assert.Equal(HttpStatusCode.OK, step.StatusCode);
            Assert.Equal(expected, (await step.ReadAsync<OrderDto>()).Status);
        }

        // история хранит все четыре записи
        var details = await GetOrderAsync(s.ClientToken, s.Order.Id);
        Assert.Equal(4, details.History.Count);
        Assert.Null(details.History[0].FromStatus);
        Assert.Equal("New", details.History[0].ToStatus);
        Assert.Equal("Completed", details.History[^1].ToStatus);

        // статистика исполнителя
        var stats = await (await Client.CallAsync(HttpMethod.Get, "/api/performer/stats", token: s.PerformerToken))
            .ReadAsync<PerformerStatsDto>();
        Assert.Equal(1, stats.CompletedOrders);
        Assert.Equal(100m, stats.CompletedAmount);
    }

    [Fact]
    public async Task Order_price_does_not_change_when_service_price_changes()
    {
        var s = await PrepareOrderAsync(price: 50, quantity: 2);

        var update = await Client.CallAsync(HttpMethod.Put, $"/api/performer/services/{s.Service.Id}", new
        {
            categoryId = s.Service.CategoryId,
            title = s.Service.Title,
            description = "Updated",
            price = 99,
            unit = "Hour"
        }, s.PerformerToken);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var order = await GetOrderAsync(s.ClientToken, s.Order.Id);
        Assert.Equal(50m, Assert.Single(order.Items).UnitPrice);
        Assert.Equal(100m, order.TotalAmount);

        // а в каталоге уже новая цена
        var card = await Client.CallAsync(HttpMethod.Get, $"/api/catalog/{s.Service.Id}");
        Assert.Equal(99m, (await card.ReadAsync<ServiceDto>()).Price);
    }

    [Fact]
    public async Task Cart_with_two_performers_creates_two_orders()
    {
        var categoryId = await CreateCategoryAsync();
        var (performer1, performer1Id) = await RegisterAsync("Performer");
        var (performer2, performer2Id) = await RegisterAsync("Performer");
        var service1 = await CreateServiceAsync(performer1, categoryId, 10);
        var service2 = await CreateServiceAsync(performer2, categoryId, 20);
        var (client, _) = await RegisterAsync("Client");

        await AddToCartAsync(client, service1.Id, 3);
        await AddToCartAsync(client, service2.Id, 1);

        var checkout = await CheckoutAsync(client);
        Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);
        var orders = await checkout.ReadAsync<List<OrderDto>>();

        Assert.Equal(2, orders.Count);
        Assert.Equal(30m, orders.Single(o => o.PerformerId == performer1Id).TotalAmount);
        Assert.Equal(20m, orders.Single(o => o.PerformerId == performer2Id).TotalAmount);

        // каждый исполнитель видит только свой заказ
        var incoming1 = await (await Client.CallAsync(HttpMethod.Get, "/api/performer/orders", token: performer1))
            .ReadAsync<List<OrderDto>>();
        var own1 = Assert.Single(incoming1);
        Assert.Equal(performer1Id, own1.PerformerId);

        var incoming2 = await (await Client.CallAsync(HttpMethod.Get, "/api/performer/orders", token: performer2))
            .ReadAsync<List<OrderDto>>();
        Assert.Equal(performer2Id, Assert.Single(incoming2).PerformerId);
    }

    [Fact]
    public async Task Checkout_with_empty_cart_returns_400()
    {
        var (client, _) = await RegisterAsync("Client");

        var response = await CheckoutAsync(client);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(400, (await response.ReadAsync<ApiError>()).Status);
    }

    [Fact]
    public async Task Checkout_with_unavailable_service_saves_nothing()
    {
        var categoryId = await CreateCategoryAsync();
        var (performer, _) = await RegisterAsync("Performer");
        var service1 = await CreateServiceAsync(performer, categoryId, 10);
        var service2 = await CreateServiceAsync(performer, categoryId, 20);
        var (client, _) = await RegisterAsync("Client");

        await AddToCartAsync(client, service1.Id, 1);
        await AddToCartAsync(client, service2.Id, 1);

        // вторую услугу скрывают после того, как клиент положил её в корзину
        await Client.CallAsync(HttpMethod.Patch,
            $"/api/performer/services/{service2.Id}/hide", token: performer);

        var checkout = await CheckoutAsync(client);
        Assert.Equal(HttpStatusCode.BadRequest, checkout.StatusCode);
        var error = await checkout.ReadAsync<ApiError>();
        Assert.Contains(service2.Title, error.Error);

        // заказ не создан
        var mine = await (await Client.CallAsync(HttpMethod.Get, "/api/orders/my", token: client))
            .ReadAsync<List<OrderDto>>();
        Assert.Empty(mine);

        // корзина осталась нетронутой
        var cart = await (await Client.CallAsync(HttpMethod.Get, "/api/cart", token: client))
            .ReadAsync<CartDto>();
        Assert.Equal(2, cart.Items.Count);
    }

    [Fact]
    public async Task Status_transitions_follow_the_rules_over_api()
    {
        var s = await PrepareOrderAsync();

        // нельзя пропустить статус
        var skip = await OrderActionAsync(s.PerformerToken, s.Order.Id, "complete");
        Assert.Equal(HttpStatusCode.Conflict, skip.StatusCode);
        Assert.Equal(409, (await skip.ReadAsync<ApiError>()).Status);

        // клиент не может подтверждать заказы
        var wrongRole = await OrderActionAsync(s.ClientToken, s.Order.Id, "confirm");
        Assert.Equal(HttpStatusCode.Forbidden, wrongRole.StatusCode);

        // подтверждение работает один раз
        Assert.Equal(HttpStatusCode.OK, (await OrderActionAsync(s.PerformerToken, s.Order.Id, "confirm")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await OrderActionAsync(s.PerformerToken, s.Order.Id, "confirm")).StatusCode);

        // клиент может отменить только новый заказ
        var tooLate = await OrderActionAsync(s.ClientToken, s.Order.Id, "cancel");
        Assert.Equal(HttpStatusCode.Conflict, tooLate.StatusCode);

        // а новый заказ клиент отменяет, и повторно отменить нельзя
        var other = await PrepareOrderAsync();
        var cancel = await OrderActionAsync(other.ClientToken, other.Order.Id, "cancel");
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal("Cancelled", (await cancel.ReadAsync<OrderDto>()).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await OrderActionAsync(other.ClientToken, other.Order.Id, "cancel")).StatusCode);
    }

    [Fact]
    public async Task Foreign_orders_are_invisible()
    {
        var s = await PrepareOrderAsync();
        var (otherClient, _) = await RegisterAsync("Client");
        var (otherPerformer, _) = await RegisterAsync("Performer");

        var view = await Client.CallAsync(HttpMethod.Get, $"/api/orders/{s.Order.Id}", token: otherClient);
        Assert.Equal(HttpStatusCode.NotFound, view.StatusCode);

        var confirm = await OrderActionAsync(otherPerformer, s.Order.Id, "confirm");
        Assert.Equal(HttpStatusCode.NotFound, confirm.StatusCode);

        var cancel = await OrderActionAsync(otherClient, s.Order.Id, "cancel");
        Assert.Equal(HttpStatusCode.NotFound, cancel.StatusCode);

        // заказ остался новым
        Assert.Equal("New", (await GetOrderAsync(s.ClientToken, s.Order.Id)).Status);
    }

    [Fact]
    public async Task Admin_sees_all_orders_and_can_filter_by_status()
    {
        var s = await PrepareOrderAsync();
        var admin = await LoginAdminAsync();

        var all = await Client.CallAsync(HttpMethod.Get, "/api/admin/orders?pageSize=50", token: admin);
        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        var page = await all.ReadAsync<PagedResult<OrderDto>>();
        Assert.True(page.TotalCount >= 1);
        Assert.Contains(page.Items, o => o.Id == s.Order.Id);

        var filtered = await Client.CallAsync(HttpMethod.Get, "/api/admin/orders?status=New&pageSize=50", token: admin);
        var newOrders = await filtered.ReadAsync<PagedResult<OrderDto>>();
        Assert.All(newOrders.Items, o => Assert.Equal("New", o.Status));

        // клиенту этот список недоступен
        var denied = await Client.CallAsync(HttpMethod.Get, "/api/admin/orders", token: s.ClientToken);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Performer_can_reject_new_order()
    {
        var s = await PrepareOrderAsync();

        var reject = await OrderActionAsync(s.PerformerToken, s.Order.Id, "reject");
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        Assert.Equal("Cancelled", (await reject.ReadAsync<OrderDto>()).Status);

        var details = await GetOrderAsync(s.ClientToken, s.Order.Id);
        Assert.Equal("Cancelled", details.Status);
        Assert.Equal("Cancelled", details.History[^1].ToStatus);

        // из отменённого заказа дальше пути нет
        var confirm = await OrderActionAsync(s.PerformerToken, s.Order.Id, "confirm");
        Assert.Equal(HttpStatusCode.Conflict, confirm.StatusCode);
    }
}