using System.Net;
using FixHub.Api.DTOs;
using Xunit;

namespace FixHub.Tests.Integration;

[Collection("api")]
public class ScheduleRequestsAndReviewExtrasTests : IntegrationTestBase
{
    public ScheduleRequestsAndReviewExtrasTests(ApiFixture fixture) : base(fixture)
    {
    }

    private static string Future(int days = 3) => DateTime.UtcNow.AddDays(days).ToString("yyyy-MM-dd");

    private async Task<(string Performer, int PerformerId, ServiceDto Service, int CategoryId)> PreparePerformerAsync()
    {
        var categoryId = await CreateCategoryAsync();
        var (token, id) = await RegisterAsync("Performer");
        var service = await CreateServiceAsync(token, categoryId);
        return (token, id, service, categoryId);
    }

    private Task<HttpResponseMessage> QuickAsync(string token, int serviceId, string? date, string? time) =>
        Client.CallAsync(HttpMethod.Post, "/api/orders/quick", new
        {
            serviceId,
            desiredDate = date,
            desiredTime = time,
            address = "Минск",
            contactName = "Test",
            contactPhone = "+375291234567"
        }, token);

    // ---------- причина отмены ----------

    [Fact]
    public async Task Cancel_requires_reason_and_saves_it_in_history()
    {
        var (performer, _, service, _) = await PreparePerformerAsync();
        var (client, _) = await RegisterAsync("Client");
        var order = await (await QuickAsync(client, service.Id, null, null)).ReadAsync<OrderDto>();

        var noReason = await Client.CallAsync(HttpMethod.Post, $"/api/orders/{order.Id}/cancel", new { }, client);
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var shortReason = await Client.CallAsync(HttpMethod.Post, $"/api/orders/{order.Id}/cancel",
            new { reason = "  a " }, client);
        Assert.Equal(HttpStatusCode.BadRequest, shortReason.StatusCode);

        var cancel = await Client.CallAsync(HttpMethod.Post, $"/api/orders/{order.Id}/cancel",
            new { reason = "Передумал" }, client);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal("Передумал", (await cancel.ReadAsync<OrderDto>()).CancelReason);

        var full = await (await Client.CallAsync(HttpMethod.Get, $"/api/orders/{order.Id}", token: performer))
            .ReadAsync<OrderDto>();
        Assert.Equal("Передумал", full.History.Last().Reason);

        var notes = await (await Client.CallAsync(HttpMethod.Get, "/api/notifications", token: performer))
            .ReadAsync<PagedResult<NotificationDto>>();
        Assert.Contains(notes.Items, n => n.Text.Contains("Передумал"));
    }

    [Fact]
    public async Task Performer_reject_requires_reason()
    {
        var (performer, _, service, _) = await PreparePerformerAsync();
        var (client, _) = await RegisterAsync("Client");
        var order = await (await QuickAsync(client, service.Id, null, null)).ReadAsync<OrderDto>();

        var bad = await Client.CallAsync(HttpMethod.Post, $"/api/orders/{order.Id}/reject", new { }, performer);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var ok = await Client.CallAsync(HttpMethod.Post, $"/api/orders/{order.Id}/reject",
            new { reason = "Нет времени" }, performer);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    // ---------- повтор заказа ----------

    [Fact]
    public async Task Repeat_order_puts_services_into_cart()
    {
        var (_, _, service, _) = await PreparePerformerAsync();
        var (client, _) = await RegisterAsync("Client");
        var order = await (await QuickAsync(client, service.Id, null, null)).ReadAsync<OrderDto>();

        var repeat = await Client.CallAsync(HttpMethod.Post, $"/api/orders/{order.Id}/repeat", token: client);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        var result = await repeat.ReadAsync<RepeatOrderResultDto>();
        Assert.Equal(1, result.Added);
        Assert.Empty(result.Skipped);

        var cart = await (await Client.CallAsync(HttpMethod.Get, "/api/cart", token: client)).ReadAsync<CartDto>();
        Assert.Equal(service.Id, Assert.Single(cart.Items).ServiceId);

        var (other, _) = await RegisterAsync("Client");
        Assert.Equal(HttpStatusCode.NotFound,
            (await Client.CallAsync(HttpMethod.Post, $"/api/orders/{order.Id}/repeat", token: other)).StatusCode);
    }

    // ---------- расписание ----------

    [Fact]
    public async Task Schedule_gives_slots_and_prevents_double_booking()
    {
        var (performer, performerId, service, _) = await PreparePerformerAsync();
        var (client, _) = await RegisterAsync("Client");
        var date = Future();
        var day = (int)DateOnly.Parse(date).DayOfWeek;
        if (day == 0) day = 7;

        // без расписания слотов нет и время не требуется
        var none = await (await Client.CallAsync(HttpMethod.Get, $"/api/performers/{performerId}/slots?date={date}"))
            .ReadAsync<SlotsDto>();
        Assert.False(none.HasSchedule);

        var invalid = await Client.CallAsync(HttpMethod.Put, "/api/performer/schedule", new
        {
            days = new[] { new { dayOfWeek = day, startTime = "12:00", endTime = "12:30" } }
        }, performer);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var save = await Client.CallAsync(HttpMethod.Put, "/api/performer/schedule", new
        {
            days = new[] { new { dayOfWeek = day, startTime = "09:00", endTime = "12:00" } }
        }, performer);
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        var slots = await (await Client.CallAsync(HttpMethod.Get, $"/api/performers/{performerId}/slots?date={date}"))
            .ReadAsync<SlotsDto>();
        Assert.True(slots.HasSchedule);
        Assert.Equal(new[] { "09:00", "10:00", "11:00" }, slots.Slots.Select(s => s.Time).ToArray());

        // время обязательно
        Assert.Equal(HttpStatusCode.BadRequest, (await QuickAsync(client, service.Id, date, null)).StatusCode);
        // вне расписания
        Assert.Equal(HttpStatusCode.BadRequest, (await QuickAsync(client, service.Id, date, "15:00")).StatusCode);

        var ok = await QuickAsync(client, service.Id, date, "10:00");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var order = await ok.ReadAsync<OrderDto>();
        Assert.Equal("10:00", Assert.Single(order.Items).DesiredTime);

        // слот занят
        var (other, _) = await RegisterAsync("Client");
        Assert.Equal(HttpStatusCode.BadRequest, (await QuickAsync(other, service.Id, date, "10:00")).StatusCode);

        var after = await (await Client.CallAsync(HttpMethod.Get, $"/api/performers/{performerId}/slots?date={date}"))
            .ReadAsync<SlotsDto>();
        Assert.False(after.Slots.Single(s => s.Time == "10:00").IsFree);

        // после отмены слот снова свободен
        await Client.CallAsync(HttpMethod.Post, $"/api/orders/{order.Id}/cancel", new { reason = "Не нужно" }, client);
        Assert.Equal(HttpStatusCode.OK, (await QuickAsync(other, service.Id, date, "10:00")).StatusCode);
    }

    // ---------- отзывы: фото и ответ ----------

    [Fact]
    public async Task Review_has_photos_and_performer_can_reply()
    {
        var (performer, _, service, _) = await PreparePerformerAsync();
        var (client, _) = await RegisterAsync("Client");
        var order = await (await QuickAsync(client, service.Id, null, null)).ReadAsync<OrderDto>();
        foreach (var action in new[] { "confirm", "start", "complete" })
            Assert.Equal(HttpStatusCode.OK, (await OrderActionAsync(performer, order.Id, action)).StatusCode);

        var itemId = order.Items.Single().Id;

        // больше трёх фото и чужие адреса не принимаются
        var tooMany = await Client.CallAsync(HttpMethod.Post, "/api/reviews", new
        {
            orderItemId = itemId, rating = 5, text = "ok",
            photoUrls = new[] { "/uploads/a.jpg", "/uploads/b.jpg", "/uploads/c.jpg", "/uploads/d.jpg" }
        }, client);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);

        var foreign = await Client.CallAsync(HttpMethod.Post, "/api/reviews", new
        {
            orderItemId = itemId, rating = 5, text = "ok", photoUrls = new[] { "http://evil.com/a.jpg" }
        }, client);
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);

        var created = await Client.CallAsync(HttpMethod.Post, "/api/reviews", new
        {
            orderItemId = itemId, rating = 5, text = "Отлично",
            photoUrls = new[] { "/uploads/a.jpg", "/uploads/b.jpg" }
        }, client);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var review = await created.ReadAsync<ReviewDto>();
        Assert.Equal(new[] { "/uploads/a.jpg", "/uploads/b.jpg" }, review.PhotoUrls.ToArray());
        Assert.Null(review.ReplyText);

        // отвечать может только мастер этого отзыва
        var (stranger, _) = await RegisterAsync("Performer");
        Assert.Equal(HttpStatusCode.NotFound, (await Client.CallAsync(HttpMethod.Post,
            $"/api/performer/reviews/{review.Id}/reply", new { text = "Спасибо" }, stranger)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.CallAsync(HttpMethod.Post,
            $"/api/performer/reviews/{review.Id}/reply", new { text = "   " }, performer)).StatusCode);

        var reply = await Client.CallAsync(HttpMethod.Post,
            $"/api/performer/reviews/{review.Id}/reply", new { text = "Спасибо!" }, performer);
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        var replied = await reply.ReadAsync<ReviewDto>();
        Assert.Equal("Спасибо!", replied.ReplyText);
        Assert.NotNull(replied.RepliedAt);

        var public_ = await (await Client.CallAsync(HttpMethod.Get, $"/api/catalog/{service.Id}/reviews"))
            .ReadAsync<PagedResult<ReviewDto>>();
        Assert.Equal("Спасибо!", Assert.Single(public_.Items).ReplyText);

        var notes = await (await Client.CallAsync(HttpMethod.Get, "/api/notifications", token: client))
            .ReadAsync<PagedResult<NotificationDto>>();
        Assert.Contains(notes.Items, n => n.Type == "ReviewReply");

        var removed = await Client.CallAsync(HttpMethod.Delete, $"/api/performer/reviews/{review.Id}/reply", token: performer);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Null((await removed.ReadAsync<ReviewDto>()).ReplyText);
    }

    // ---------- заявки и предложения цены ----------

    [Fact]
    public async Task Price_request_flow_creates_order_from_chosen_offer()
    {
        var (performer1, _, service1, categoryId) = await PreparePerformerAsync();
        var (performer2, _) = await RegisterAsync("Performer");
        var service2 = await CreateServiceAsync(performer2, categoryId, 80);
        var (client, _) = await RegisterAsync("Client");

        var create = await Client.CallAsync(HttpMethod.Post, "/api/requests", new
        {
            categoryId,
            title = "Починить кран",
            description = "Течёт на кухне",
            address = "Минск, Тестовая 1",
            contactName = "Клиент",
            contactPhone = "+375291234567",
            desiredDate = Future()
        }, client);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var request = await create.ReadAsync<RequestDto>();
        Assert.Equal("Open", request.Status);

        // мастер видит заявку без адреса и контактов
        var feed = await (await Client.CallAsync(HttpMethod.Get, "/api/performer/requests", token: performer1))
            .ReadAsync<List<RequestDto>>();
        var seen = Assert.Single(feed, r => r.Id == request.Id);
        Assert.Null(seen.Address);
        Assert.Null(seen.ContactPhone);

        // нельзя предложить чужую услугу
        var foreign = await Client.CallAsync(HttpMethod.Put, $"/api/performer/requests/{request.Id}/offer",
            new { serviceId = service2.Id, price = 70 }, performer1);
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);

        var offer1 = await Client.CallAsync(HttpMethod.Put, $"/api/performer/requests/{request.Id}/offer",
            new { serviceId = service1.Id, price = 100, message = "Приеду завтра" }, performer1);
        Assert.Equal(HttpStatusCode.OK, offer1.StatusCode);

        var offer2 = await (await Client.CallAsync(HttpMethod.Put, $"/api/performer/requests/{request.Id}/offer",
            new { serviceId = service2.Id, price = 90 }, performer2)).ReadAsync<OfferDto>();

        // клиент получил уведомления и видит оба предложения
        var notes = await (await Client.CallAsync(HttpMethod.Get, "/api/notifications", token: client))
            .ReadAsync<PagedResult<NotificationDto>>();
        Assert.Equal(2, notes.Items.Count(n => n.Type == "NewOffer" && n.RequestId == request.Id));

        var detail = await (await Client.CallAsync(HttpMethod.Get, $"/api/requests/{request.Id}", token: client))
            .ReadAsync<RequestDetailDto>();
        Assert.Equal(2, detail.Offers.Count);

        // чужой клиент заявку не видит
        var (stranger, _) = await RegisterAsync("Client");
        Assert.Equal(HttpStatusCode.NotFound,
            (await Client.CallAsync(HttpMethod.Get, $"/api/requests/{request.Id}", token: stranger)).StatusCode);

        var accept = await Client.CallAsync(HttpMethod.Post,
            $"/api/requests/{request.Id}/offers/{offer2.Id}/accept", token: client);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        var result = await accept.ReadAsync<AcceptOfferResultDto>();
        Assert.Equal("Chosen", result.Request.Status);
        Assert.Equal(result.Order.Id, result.Request.OrderId);
        Assert.Equal(90m, result.Order.TotalAmount);
        Assert.Equal("New", result.Order.Status);

        // победитель видит контакты и получает уведомление, второй мастер — отказ
        var feed2 = await (await Client.CallAsync(HttpMethod.Get, "/api/performer/requests", token: performer2))
            .ReadAsync<List<RequestDto>>();
        Assert.Equal("Минск, Тестовая 1", feed2.Single(r => r.Id == request.Id).Address);

        var feed1 = await (await Client.CallAsync(HttpMethod.Get, "/api/performer/requests", token: performer1))
            .ReadAsync<List<RequestDto>>();
        var lost = feed1.Single(r => r.Id == request.Id);
        Assert.Null(lost.Address);
        Assert.Equal("Rejected", lost.MyOffer!.Status);

        // повторно выбрать нельзя
        Assert.Equal(HttpStatusCode.Conflict, (await Client.CallAsync(HttpMethod.Post,
            $"/api/requests/{request.Id}/offers/{offer2.Id}/accept", token: client)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.CallAsync(HttpMethod.Put,
            $"/api/performer/requests/{request.Id}/offer",
            new { serviceId = service1.Id, price = 10 }, performer1)).StatusCode);
    }

    [Fact]
    public async Task Request_can_be_cancelled_and_offer_withdrawn()
    {
        var (performer, _, service, categoryId) = await PreparePerformerAsync();
        var (client, _) = await RegisterAsync("Client");

        var request = await (await Client.CallAsync(HttpMethod.Post, "/api/requests", new
        {
            categoryId, title = "Уборка", description = "После ремонта", address = "Минск",
            contactName = "К", contactPhone = "+375291234567"
        }, client)).ReadAsync<RequestDto>();

        Assert.Equal(HttpStatusCode.OK, (await Client.CallAsync(HttpMethod.Put,
            $"/api/performer/requests/{request.Id}/offer", new { serviceId = service.Id, price = 50 }, performer)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await Client.CallAsync(HttpMethod.Delete, $"/api/performer/requests/{request.Id}/offer", token: performer)).StatusCode);

        var mine = await (await Client.CallAsync(HttpMethod.Get, "/api/requests/my", token: client))
            .ReadAsync<List<RequestDto>>();
        Assert.Equal(0, mine.Single(r => r.Id == request.Id).OffersCount);

        var cancel = await Client.CallAsync(HttpMethod.Post, $"/api/requests/{request.Id}/cancel", token: client);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal("Cancelled", (await cancel.ReadAsync<RequestDto>()).Status);

        Assert.Equal(HttpStatusCode.Conflict, (await Client.CallAsync(HttpMethod.Put,
            $"/api/performer/requests/{request.Id}/offer", new { serviceId = service.Id, price = 60 }, performer)).StatusCode);
    }

    // ---------- портфолио: несколько фото ----------

    [Fact]
    public async Task Portfolio_item_keeps_extra_photos()
    {
        var (performer, performerId, _, _) = await PreparePerformerAsync();

        var tooMany = await Client.CallAsync(HttpMethod.Post, "/api/performer/portfolio", new
        {
            title = "Работа", afterUrl = "/uploads/a.jpg",
            photoUrls = Enumerable.Range(0, 11).Select(i => $"/uploads/{i}.jpg").ToArray()
        }, performer);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);

        var create = await Client.CallAsync(HttpMethod.Post, "/api/performer/portfolio", new
        {
            title = "Работа", afterUrl = "/uploads/a.jpg", photoUrls = new[] { "/uploads/1.jpg", "/uploads/2.jpg" }
        }, performer);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var item = await create.ReadAsync<PortfolioItemDto>();
        Assert.Equal(2, item.PhotoUrls.Count);

        var update = await Client.CallAsync(HttpMethod.Put, $"/api/performer/portfolio/{item.Id}", new
        {
            title = "Работа", afterUrl = "/uploads/a.jpg", photoUrls = new[] { "/uploads/3.jpg" }
        }, performer);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var list = await (await Client.CallAsync(HttpMethod.Get, $"/api/performers/{performerId}/portfolio"))
            .ReadAsync<List<PortfolioItemDto>>();
        Assert.Equal(new[] { "/uploads/3.jpg" }, Assert.Single(list).PhotoUrls.ToArray());
    }
}
