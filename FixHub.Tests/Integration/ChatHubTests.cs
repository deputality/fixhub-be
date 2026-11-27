using System.Net;
using FixHub.Api.DTOs;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace FixHub.Tests.Integration;

// Чат в реальном времени. В тестовом сервере нет настоящих WebSocket, поэтому соединяемся через
// long polling: для SignalR это тот же протокол, меняется только способ доставки
[Collection("api")]
public class ChatHubTests : IntegrationTestBase
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    public ChatHubTests(ApiFixture fixture) : base(fixture)
    {
    }

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

    private HubConnection Build(string? token) =>
        new HubConnectionBuilder()
            .WithUrl("https://localhost/hubs/chat", options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(token);
                options.HttpMessageHandlerFactory = _ => Fixture.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

    private async Task<HubConnection> ConnectAsync(string token)
    {
        var connection = Build(token);
        await connection.StartAsync();
        return connection;
    }

    private static async Task<T> FirstAsync<T>(TaskCompletionSource<T> source)
    {
        var finished = await Task.WhenAny(source.Task, Task.Delay(Wait));
        Assert.True(finished == source.Task, "Событие не пришло за 10 секунд");
        return await source.Task;
    }

    [Fact]
    public async Task New_order_reaches_the_performer_without_reloading_the_page()
    {
        var categoryId = await CreateCategoryAsync();
        var (performer, _) = await RegisterAsync("Performer");
        var service = await CreateServiceAsync(performer, categoryId);
        var (client, _) = await RegisterAsync("Client");

        await using var hub = await ConnectAsync(performer);
        var changed = new TaskCompletionSource<int>();
        hub.On<OrdersChangedEvent>("OrdersChanged", e => changed.TrySetResult(e.OrderId));

        Assert.Equal(HttpStatusCode.OK, (await AddToCartAsync(client, service.Id)).StatusCode);
        var checkout = await CheckoutAsync(client);
        var order = Assert.Single(await checkout.ReadAsync<List<OrderDto>>());

        Assert.Equal(order.Id, await FirstAsync(changed));
    }

    [Fact]
    public async Task Confirming_an_order_is_pushed_to_the_client_instantly()
    {
        var (performer, client, order) = await CreateOrderAsync();

        await using var hub = await ConnectAsync(client);
        var changed = new TaskCompletionSource<int>();
        hub.On<OrdersChangedEvent>("OrdersChanged", e => changed.TrySetResult(e.OrderId));

        var confirmed = await Client.CallAsync(HttpMethod.Post, $"/api/orders/{order.Id}/confirm", token: performer);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);

        Assert.Equal(order.Id, await FirstAsync(changed));
    }

    [Fact]
    public async Task Message_sent_by_request_reaches_the_other_party_instantly()
    {
        var (performer, client, order) = await CreateOrderAsync();

        await using var hub = await ConnectAsync(performer);
        var received = new TaskCompletionSource<OrderMessageDto>();
        hub.On<OrderMessageDto>("MessageReceived", m => received.TrySetResult(m));
        Assert.True(await hub.InvokeAsync<bool>("JoinOrder", order.Id));

        var sent = await Client.CallAsync(HttpMethod.Post, $"/api/orders/{order.Id}/messages", new { text = "Я у подъезда" }, client);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);

        var message = await FirstAsync(received);
        Assert.Equal("Я у подъезда", message.Text);
        Assert.Equal(order.Id, message.OrderId);
    }

    [Fact]
    public async Task Message_sent_through_websocket_is_saved_and_delivered()
    {
        var (performer, client, order) = await CreateOrderAsync();

        await using var performerHub = await ConnectAsync(performer);
        var received = new TaskCompletionSource<OrderMessageDto>();
        performerHub.On<OrderMessageDto>("MessageReceived", m => received.TrySetResult(m));
        await performerHub.InvokeAsync<bool>("JoinOrder", order.Id);

        await using var clientHub = await ConnectAsync(client);
        await clientHub.InvokeAsync<bool>("JoinOrder", order.Id);
        var result = await clientHub.InvokeAsync<ChatSendResultDto>("SendMessage", order.Id, "Добрый день!");

        Assert.True(result.Ok);
        Assert.Equal("Добрый день!", (await FirstAsync(received)).Text);

        // сообщение сохранилось и видно в истории
        var history = await Client.CallAsync(HttpMethod.Get, $"/api/orders/{order.Id}/messages", token: performer);
        Assert.Single(await history.ReadAsync<List<OrderMessageDto>>());
    }

    [Fact]
    public async Task Websocket_send_reports_errors_instead_of_throwing()
    {
        var (_, client, order) = await CreateOrderAsync();

        await using var hub = await ConnectAsync(client);

        var empty = await hub.InvokeAsync<ChatSendResultDto>("SendMessage", order.Id, "   ");
        Assert.False(empty.Ok);
        Assert.Equal(400, empty.Status);

        var foreign = await hub.InvokeAsync<ChatSendResultDto>("SendMessage", 999999, "Привет");
        Assert.False(foreign.Ok);
        Assert.Equal(404, foreign.Status);
    }

    [Fact]
    public async Task Stranger_cannot_join_someone_elses_chat()
    {
        var (_, _, order) = await CreateOrderAsync();
        var (stranger, _) = await RegisterAsync("Client");

        await using var hub = await ConnectAsync(stranger);

        Assert.False(await hub.InvokeAsync<bool>("JoinOrder", order.Id));
    }

    [Fact]
    public async Task Connection_requires_a_valid_token()
    {
        var anonymous = Build(null);
        await Assert.ThrowsAnyAsync<Exception>(() => anonymous.StartAsync());
        await anonymous.DisposeAsync();

        var broken = Build("not-a-token");
        await Assert.ThrowsAnyAsync<Exception>(() => broken.StartAsync());
        await broken.DisposeAsync();
    }

    [Fact]
    public async Task Reading_a_message_is_reported_to_the_sender_and_updates_the_badge()
    {
        var (performer, client, order) = await CreateOrderAsync();

        // клиент пишет, мастер ещё не открывал чат: у мастера один непрочитанный
        await using var performerHub = await ConnectAsync(performer);
        var badge = new TaskCompletionSource<(int OrderId, int Count)>();
        performerHub.On<UnreadChange>("UnreadChanged", u => { if (u.Count > 0) badge.TrySetResult((u.OrderId, u.Count)); });

        await using var clientHub = await ConnectAsync(client);
        var read = new TaskCompletionSource<ReadReceipt>();
        clientHub.On<ReadReceipt>("MessagesRead", r => read.TrySetResult(r));
        await clientHub.InvokeAsync<bool>("JoinOrder", order.Id);

        await Client.CallAsync(HttpMethod.Post, $"/api/orders/{order.Id}/messages", new { text = "Когда сможете?" }, client);

        var unread = await FirstAsync(badge);
        Assert.Equal(order.Id, unread.OrderId);
        Assert.Equal(1, unread.Count);

        // мастер открыл чат и прочитал: клиент сразу получает «прочитано»
        await performerHub.InvokeAsync("MarkRead", order.Id);

        var receipt = await FirstAsync(read);
        Assert.Equal(order.Id, receipt.OrderId);
    }

    private record UnreadChange(int OrderId, int Count);

    private record ReadReceipt(int OrderId, int ReaderId);
    private record OrdersChangedEvent(int OrderId);
}
