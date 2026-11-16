using System.Net;
using FixHub.Api.DTOs;
using Xunit;

namespace FixHub.Tests.Integration;

[Collection("api")]
public class NotificationTests : IntegrationTestBase
{
    public NotificationTests(ApiFixture fixture) : base(fixture)
    {
    }

    // Исполнитель с услугой и клиент, который оформил заказ
    private async Task<(string Performer, string Client, OrderDto Order)> CreateOrderAsync()
    {
        var categoryId = await CreateCategoryAsync();
        var (performer, _) = await RegisterAsync("Performer");
        var service = await CreateServiceAsync(performer, categoryId);
        var (client, _) = await RegisterAsync("Client");

        Assert.Equal(HttpStatusCode.OK, (await AddToCartAsync(client, service.Id)).StatusCode);

        var checkout = await CheckoutAsync(client);
        Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);
        var order = Assert.Single(await checkout.ReadAsync<List<OrderDto>>());

        return (performer, client, order);
    }

    private async Task<PagedResult<NotificationDto>> GetListAsync(string token, string query = "")
    {
        var response = await Client.CallAsync(HttpMethod.Get, "/api/notifications" + query, token: token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<PagedResult<NotificationDto>>();
    }

    private async Task<int> GetUnreadCountAsync(string token)
    {
        var response = await Client.CallAsync(HttpMethod.Get, "/api/notifications/unread-count", token: token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadAsync<UnreadCountDto>()).Count;
    }

    [Fact]
    public async Task Performer_gets_notification_about_new_order()
    {
        var (performer, client, order) = await CreateOrderAsync();

        var item = Assert.Single((await GetListAsync(performer)).Items);
        Assert.Equal("NewOrder", item.Type);
        Assert.Equal(order.Id, item.OrderId);
        Assert.False(item.IsRead);

        // клиенту про его собственный заказ ничего не приходит
        Assert.Empty((await GetListAsync(client)).Items);
    }

    [Fact]
    public async Task Client_gets_notification_when_performer_changes_status()
    {
        var (performer, client, order) = await CreateOrderAsync();

        var confirm = await OrderActionAsync(performer, order.Id, "confirm");
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        var item = Assert.Single((await GetListAsync(client)).Items);
        Assert.Equal("OrderStatusChanged", item.Type);
        Assert.Equal(order.Id, item.OrderId);
    }

    [Fact]
    public async Task Performer_gets_notification_when_client_cancels()
    {
        var (performer, client, order) = await CreateOrderAsync();

        var cancel = await OrderActionAsync(client, order.Id, "cancel");
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        // новый заказ и отмена
        var items = (await GetListAsync(performer)).Items;
        Assert.Equal(2, items.Count);
        Assert.Contains(items, n => n.Type == "OrderStatusChanged" && n.OrderId == order.Id);
    }

    [Fact]
    public async Task Performer_gets_notification_about_new_review()
    {
        var (performer, client, order) = await CreateOrderAsync();

        foreach (var action in new[] { "confirm", "start", "complete" })
            Assert.Equal(HttpStatusCode.OK, (await OrderActionAsync(performer, order.Id, action)).StatusCode);

        var review = await Client.CallAsync(HttpMethod.Post, "/api/reviews",
            new { orderItemId = order.Items[0].Id, rating = 4, text = "Всё хорошо" }, client);
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        var reviewId = (await review.ReadAsync<ReviewDto>()).Id;

        var items = (await GetListAsync(performer)).Items;
        Assert.Contains(items, n => n.Type == "NewReview" && n.ReviewId == reviewId);
    }

    [Fact]
    public async Task Unread_count_and_mark_as_read_work()
    {
        var (performer, _, _) = await CreateOrderAsync();
        Assert.Equal(1, await GetUnreadCountAsync(performer));

        var id = Assert.Single((await GetListAsync(performer)).Items).Id;
        var read = await Client.CallAsync(HttpMethod.Post, $"/api/notifications/{id}/read", token: performer);
        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);

        Assert.Equal(0, await GetUnreadCountAsync(performer));
        Assert.Empty((await GetListAsync(performer, "?onlyUnread=true")).Items);

        // в общем списке уведомление осталось
        Assert.Single((await GetListAsync(performer)).Items);
    }

    [Fact]
    public async Task Read_all_marks_everything_as_read()
    {
        var (performer, client, order) = await CreateOrderAsync();
        await OrderActionAsync(client, order.Id, "cancel");
        Assert.Equal(2, await GetUnreadCountAsync(performer));

        var response = await Client.CallAsync(HttpMethod.Post, "/api/notifications/read-all", token: performer);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(0, await GetUnreadCountAsync(performer));
    }

    [Fact]
    public async Task Foreign_notification_returns_404_and_auth_is_required()
    {
        var (performer, client, _) = await CreateOrderAsync();
        var id = Assert.Single((await GetListAsync(performer)).Items).Id;

        // клиент не может прочитать чужое уведомление
        var foreign = await Client.CallAsync(HttpMethod.Post, $"/api/notifications/{id}/read", token: client);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        var anonymous = await Client.CallAsync(HttpMethod.Get, "/api/notifications");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }
}