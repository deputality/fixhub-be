using System.Net;
using FixHub.Api.DTOs;
using Xunit;

namespace FixHub.Tests.Integration;

[Collection("api")]
public class OrderMessageTests : IntegrationTestBase
{
    public OrderMessageTests(ApiFixture fixture) : base(fixture)
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

    private Task<HttpResponseMessage> SendAsync(string token, int orderId, string text) =>
        Client.CallAsync(HttpMethod.Post, $"/api/orders/{orderId}/messages", new { text }, token);

    private async Task<List<OrderMessageDto>> GetMessagesAsync(string token, int orderId)
    {
        var response = await Client.CallAsync(HttpMethod.Get, $"/api/orders/{orderId}/messages", token: token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<List<OrderMessageDto>>();
    }

    [Fact]
    public async Task Client_and_performer_can_chat()
    {
        var (performer, client, order) = await CreateOrderAsync();

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, order.Id, "Подойдёт завтра в 10:00?")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(performer, order.Id, "Да, буду в 10:00")).StatusCode);

        var forClient = await GetMessagesAsync(client, order.Id);
        Assert.Equal(2, forClient.Count);
        Assert.Equal("Подойдёт завтра в 10:00?", forClient[0].Text);
        Assert.Equal("Да, буду в 10:00", forClient[1].Text);
    }

    [Fact]
    public async Task Outsider_cannot_read_or_write()
    {
        var (_, client, order) = await CreateOrderAsync();
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, order.Id, "Привет")).StatusCode);

        var (stranger, _) = await RegisterAsync("Client");
        var read = await Client.CallAsync(HttpMethod.Get, $"/api/orders/{order.Id}/messages", token: stranger);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(stranger, order.Id, "Чужой")).StatusCode);

        var (otherPerformer, _) = await RegisterAsync("Performer");
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(otherPerformer, order.Id, "Чужой")).StatusCode);
    }

    [Fact]
    public async Task Requires_authorization()
    {
        var (_, _, order) = await CreateOrderAsync();

        var response = await Client.CallAsync(HttpMethod.Get, $"/api/orders/{order.Id}/messages");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Empty_message_is_rejected()
    {
        var (_, client, order) = await CreateOrderAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, order.Id, "   ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, order.Id, new string('a', 1001))).StatusCode);
    }

    [Fact]
    public async Task Cancelled_order_chat_is_read_only()
    {
        var (performer, client, order) = await CreateOrderAsync();
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, order.Id, "Привет")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await OrderActionAsync(performer, order.Id, "reject")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, order.Id, "Ещё одно")).StatusCode);
        Assert.Single(await GetMessagesAsync(client, order.Id));
    }

    [Fact]
    public async Task Unread_counter_and_read_marks_work()
    {
        var (performer, client, order) = await CreateOrderAsync();

        await SendAsync(client, order.Id, "Первое");
        await SendAsync(client, order.Id, "Второе");

        var unread = await Client.CallAsync(HttpMethod.Get, "/api/orders/messages/unread", token: performer);
        var list = await unread.ReadAsync<List<UnreadChatDto>>();
        var chat = Assert.Single(list, x => x.OrderId == order.Id);
        Assert.Equal(2, chat.Count);

        // у автора своих сообщений непрочитанных нет
        var mine = await (await Client.CallAsync(HttpMethod.Get, "/api/orders/messages/unread", token: client))
            .ReadAsync<List<UnreadChatDto>>();
        Assert.DoesNotContain(mine, x => x.OrderId == order.Id);

        // после открытия чата мастером сообщения прочитаны
        var seen = await GetMessagesAsync(performer, order.Id);
        Assert.Equal(2, seen.Count);

        var after = await (await Client.CallAsync(HttpMethod.Get, "/api/orders/messages/unread", token: performer))
            .ReadAsync<List<UnreadChatDto>>();
        Assert.DoesNotContain(after, x => x.OrderId == order.Id);

        // а клиент видит, что его сообщения прочитаны
        var forClient = await GetMessagesAsync(client, order.Id);
        Assert.All(forClient, m => Assert.True(m.IsRead));
    }

    [Fact]
    public async Task New_message_creates_single_notification_until_read()
    {
        var (performer, client, order) = await CreateOrderAsync();

        await SendAsync(client, order.Id, "Первое");
        await SendAsync(client, order.Id, "Второе");

        var response = await Client.CallAsync(HttpMethod.Get, "/api/notifications", token: performer);
        var page = await response.ReadAsync<PagedResult<NotificationDto>>();
        Assert.Single(page.Items, n => n.Type == "NewMessage" && n.OrderId == order.Id);

        // мастер открыл чат: уведомление о сообщениях прочитано
        await GetMessagesAsync(performer, order.Id);
        var after = await (await Client.CallAsync(HttpMethod.Get, "/api/notifications", token: performer))
            .ReadAsync<PagedResult<NotificationDto>>();
        Assert.All(after.Items.Where(n => n.Type == "NewMessage"), n => Assert.True(n.IsRead));
    }
}
