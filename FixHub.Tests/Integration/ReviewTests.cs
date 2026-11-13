using System.Net;
using FixHub.Api.Common;
using FixHub.Api.DTOs;
using Xunit;

namespace FixHub.Tests.Integration;

[Collection("api")]
public class ReviewTests : IntegrationTestBase
{
    public ReviewTests(ApiFixture fixture) : base(fixture)
    {
    }

    // Категория + исполнитель + его услуга
    private async Task<(string Token, int Id, ServiceDto Service)> PreparePerformerAsync(string? title = null)
    {
        var categoryId = await CreateCategoryAsync();
        var (token, id) = await RegisterAsync("Performer");
        var service = await CreateServiceAsync(token, categoryId, 50, title);
        return (token, id, service);
    }

    // Новый клиент заказывает услугу, исполнитель доводит заказ до статуса «Выполнен».
    // Возвращает токен клиента и id позиции заказа
    private async Task<(string ClientToken, int ClientId, int OrderItemId)> CompleteOrderAsync(
        string performerToken, int serviceId)
    {
        var (clientToken, clientId) = await RegisterAsync("Client");

        var add = await AddToCartAsync(clientToken, serviceId);
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);

        var checkout = await CheckoutAsync(clientToken);
        Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);
        var order = Assert.Single(await checkout.ReadAsync<List<OrderDto>>());

        foreach (var action in new[] { "confirm", "start", "complete" })
        {
            var step = await OrderActionAsync(performerToken, order.Id, action);
            Assert.Equal(HttpStatusCode.OK, step.StatusCode);
        }

        return (clientToken, clientId, Assert.Single(order.Items).Id);
    }

    private Task<HttpResponseMessage> PostReviewAsync(
        string? token, int orderItemId, int rating = 5, string text = "Отличная работа") =>
        Client.CallAsync(HttpMethod.Post, "/api/reviews", new { orderItemId, rating, text }, token);

    // Заказ выполнен и отзыв оставлен; возвращает созданный отзыв
    private async Task<ReviewDto> LeaveReviewAsync(string performerToken, int serviceId, int rating)
    {
        var (clientToken, _, itemId) = await CompleteOrderAsync(performerToken, serviceId);
        var response = await PostReviewAsync(clientToken, itemId, rating);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<ReviewDto>();
    }

    private async Task<ServiceDto> GetCardAsync(int serviceId)
    {
        var response = await Client.CallAsync(HttpMethod.Get, $"/api/catalog/{serviceId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<ServiceDto>();
    }

    private async Task<PagedResult<ReviewDto>> GetServiceReviewsAsync(int serviceId)
    {
        var response = await Client.CallAsync(HttpMethod.Get, $"/api/catalog/{serviceId}/reviews");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<PagedResult<ReviewDto>>();
    }

    private async Task<PagedResult<ServiceDto>> GetCatalogAsync(string query)
    {
        var response = await Client.CallAsync(HttpMethod.Get, $"/api/catalog?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<PagedResult<ServiceDto>>();
    }

    [Fact]
    public async Task Client_leaves_review_and_it_appears_everywhere()
    {
        var (performer, performerId, service) = await PreparePerformerAsync();
        var (client, clientId, itemId) = await CompleteOrderAsync(performer, service.Id);

        var response = await PostReviewAsync(client, itemId, 5, "Отличная работа");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var review = await response.ReadAsync<ReviewDto>();
        Assert.Equal(5, review.Rating);
        Assert.Equal(service.Id, review.ServiceId);
        Assert.Equal(clientId, review.ClientId);
        Assert.Equal(itemId, review.OrderItemId);
        Assert.False(review.IsHidden);

        // рейтинг в карточке услуги и исполнителя
        var card = await GetCardAsync(service.Id);
        Assert.Equal(5m, card.Rating);
        Assert.Equal(1, card.ReviewCount);
        Assert.Equal(5m, card.PerformerRating);
        Assert.Equal(1, card.PerformerReviewCount);

        // публичный список отзывов об услуге
        var list = await GetServiceReviewsAsync(service.Id);
        Assert.Equal(1, list.TotalCount);
        Assert.Equal(review.Id, Assert.Single(list.Items).Id);

        // мои отзывы
        var mine = await (await Client.CallAsync(HttpMethod.Get, "/api/reviews/my", token: client))
            .ReadAsync<List<ReviewDto>>();
        Assert.Equal(review.Id, Assert.Single(mine).Id);

        // отзывы о исполнителе и общий рейтинг
        var aboutMe = await (await Client.CallAsync(HttpMethod.Get, "/api/performer/reviews", token: performer))
            .ReadAsync<PerformerReviewsDto>();
        Assert.Equal(5m, aboutMe.Rating);
        Assert.Equal(1, aboutMe.ReviewCount);
        Assert.Equal(review.Id, Assert.Single(aboutMe.Reviews.Items).Id);
        Assert.Equal(performerId, (await GetCardAsync(service.Id)).PerformerId);
    }

    [Fact]
    public async Task Service_without_reviews_has_null_rating()
    {
        var (_, _, service) = await PreparePerformerAsync();

        var card = await GetCardAsync(service.Id);
        Assert.Null(card.Rating);
        Assert.Equal(0, card.ReviewCount);
        Assert.Null(card.PerformerRating);
        Assert.Equal(0, card.PerformerReviewCount);

        Assert.Equal(0, (await GetServiceReviewsAsync(service.Id)).TotalCount);
    }

    [Fact]
    public async Task Rating_is_average_of_visible_reviews()
    {
        var (performer, _, service) = await PreparePerformerAsync();

        await LeaveReviewAsync(performer, service.Id, 5);
        await LeaveReviewAsync(performer, service.Id, 4);

        var card = await GetCardAsync(service.Id);
        Assert.Equal(4.5m, card.Rating);
        Assert.Equal(2, card.ReviewCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task Rating_outside_range_returns_400(int rating)
    {
        var (performer, _, service) = await PreparePerformerAsync();
        var (client, _, itemId) = await CompleteOrderAsync(performer, service.Id);

        var response = await PostReviewAsync(client, itemId, rating);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(400, (await response.ReadAsync<ApiError>()).Status);
    }

    [Fact]
    public async Task Empty_text_returns_400()
    {
        var (performer, _, service) = await PreparePerformerAsync();
        var (client, _, itemId) = await CompleteOrderAsync(performer, service.Id);

        var response = await PostReviewAsync(client, itemId, 5, "   ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Foreign_order_item_returns_404()
    {
        var (performer, _, service) = await PreparePerformerAsync();
        var (_, _, itemId) = await CompleteOrderAsync(performer, service.Id);
        var (stranger, _) = await RegisterAsync("Client");

        var response = await PostReviewAsync(stranger, itemId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(404, (await response.ReadAsync<ApiError>()).Status);
    }

    [Fact]
    public async Task Review_before_order_is_completed_returns_409()
    {
        var (_, _, service) = await PreparePerformerAsync();
        var (client, _) = await RegisterAsync("Client");

        await AddToCartAsync(client, service.Id);
        var checkout = await CheckoutAsync(client);
        var order = Assert.Single(await checkout.ReadAsync<List<OrderDto>>());

        var response = await PostReviewAsync(client, Assert.Single(order.Items).Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(409, (await response.ReadAsync<ApiError>()).Status);
    }

    [Fact]
    public async Task Second_review_on_same_item_returns_409()
    {
        var (performer, _, service) = await PreparePerformerAsync();
        var (client, _, itemId) = await CompleteOrderAsync(performer, service.Id);

        Assert.Equal(HttpStatusCode.OK, (await PostReviewAsync(client, itemId)).StatusCode);

        var second = await PostReviewAsync(client, itemId, 1, "Передумал");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(409, (await second.ReadAsync<ApiError>()).Status);
        Assert.Equal(1, (await GetServiceReviewsAsync(service.Id)).TotalCount);
    }

    [Fact]
    public async Task Review_endpoints_check_authentication_and_role()
    {
        var (performer, _, service) = await PreparePerformerAsync();
        var (client, _, itemId) = await CompleteOrderAsync(performer, service.Id);

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostReviewAsync(null, itemId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostReviewAsync(performer, itemId)).StatusCode);

        var performerReviewsAsClient = await Client.CallAsync(
            HttpMethod.Get, "/api/performer/reviews", token: client);
        Assert.Equal(HttpStatusCode.Forbidden, performerReviewsAsClient.StatusCode);

        var adminListAsClient = await Client.CallAsync(HttpMethod.Get, "/api/admin/reviews", token: client);
        Assert.Equal(HttpStatusCode.Forbidden, adminListAsClient.StatusCode);
    }

    [Fact]
    public async Task Admin_hides_review_and_it_leaves_rating_and_lists()
    {
        var (performer, _, service) = await PreparePerformerAsync();
        var admin = await LoginAdminAsync();

        var first = await LeaveReviewAsync(performer, service.Id, 5);
        await LeaveReviewAsync(performer, service.Id, 3);
        Assert.Equal(4m, (await GetCardAsync(service.Id)).Rating);

        // скрываем отзыв с оценкой 5
        var hide = await Client.CallAsync(HttpMethod.Post, $"/api/admin/reviews/{first.Id}/hide", token: admin);
        Assert.Equal(HttpStatusCode.OK, hide.StatusCode);
        Assert.True((await hide.ReadAsync<ReviewDto>()).IsHidden);

        var card = await GetCardAsync(service.Id);
        Assert.Equal(3m, card.Rating);
        Assert.Equal(1, card.ReviewCount);
        Assert.DoesNotContain((await GetServiceReviewsAsync(service.Id)).Items, r => r.Id == first.Id);

        var aboutMe = await (await Client.CallAsync(HttpMethod.Get, "/api/performer/reviews", token: performer))
            .ReadAsync<PerformerReviewsDto>();
        Assert.Equal(1, aboutMe.ReviewCount);

        // админ видит скрытый отзыв в своём списке
        var hidden = await (await Client.CallAsync(
                HttpMethod.Get, "/api/admin/reviews?isHidden=true&pageSize=50", token: admin))
            .ReadAsync<PagedResult<ReviewDto>>();
        Assert.Contains(hidden.Items, r => r.Id == first.Id && r.IsHidden);

        // возвращаем отзыв
        var show = await Client.CallAsync(HttpMethod.Post, $"/api/admin/reviews/{first.Id}/show", token: admin);
        Assert.Equal(HttpStatusCode.OK, show.StatusCode);
        Assert.False((await show.ReadAsync<ReviewDto>()).IsHidden);

        var restored = await GetCardAsync(service.Id);
        Assert.Equal(4m, restored.Rating);
        Assert.Equal(2, restored.ReviewCount);
    }

    [Fact]
    public async Task Hide_review_checks_role_and_existence()
    {
        var (performer, _, service) = await PreparePerformerAsync();
        var review = await LeaveReviewAsync(performer, service.Id, 4);
        var admin = await LoginAdminAsync();

        var asPerformer = await Client.CallAsync(
            HttpMethod.Post, $"/api/admin/reviews/{review.Id}/hide", token: performer);
        Assert.Equal(HttpStatusCode.Forbidden, asPerformer.StatusCode);

        var missing = await Client.CallAsync(HttpMethod.Post, "/api/admin/reviews/999999/hide", token: admin);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(404, (await missing.ReadAsync<ApiError>()).Status);
    }

    [Fact]
    public async Task Catalog_filters_by_min_rating_and_sorts_by_rating()
    {
        var categoryId = await CreateCategoryAsync();
        var (performer, _) = await RegisterAsync("Performer");
        var prefix = $"Rate-{Guid.NewGuid():N}";

        var low = await CreateServiceAsync(performer, categoryId, 50, $"{prefix}-low");
        var high = await CreateServiceAsync(performer, categoryId, 50, $"{prefix}-high");
        var none = await CreateServiceAsync(performer, categoryId, 50, $"{prefix}-none");

        await LeaveReviewAsync(performer, low.Id, 2);
        await LeaveReviewAsync(performer, high.Id, 5);

        var search = $"search={Uri.EscapeDataString(prefix)}&pageSize=50";

        // сначала лучший рейтинг, услуги без отзывов в конце
        var sorted = await GetCatalogAsync($"{search}&sort=RatingDesc");
        Assert.Equal(
            new[] { high.Id, low.Id, none.Id },
            sorted.Items.Select(s => s.Id).ToArray());

        // фильтр: только услуги с рейтингом не ниже 4
        var filtered = await GetCatalogAsync($"{search}&minRating=4");
        Assert.Equal(high.Id, Assert.Single(filtered.Items).Id);
    }
}