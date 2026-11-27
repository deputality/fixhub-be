using System.Net;
using FixHub.Api.DTOs;
using Xunit;

namespace FixHub.Tests.Integration;

// У непроверенного мастера возможностей меньше: после проверки лимиты снимаются
[Collection("api")]
public class PerformerLimitsTests : IntegrationTestBase
{
    public PerformerLimitsTests(ApiFixture fixture) : base(fixture)
    {
    }

    private Task<HttpResponseMessage> AddServiceAsync(string token, int categoryId) =>
        Client.CallAsync(HttpMethod.Post, "/api/performer/services", new
        {
            categoryId, title = UniqueTitle(), description = "Описание", price = 50, unit = "Hour"
        }, token);

    private Task<HttpResponseMessage> AddPortfolioAsync(string token) =>
        Client.CallAsync(HttpMethod.Post, "/api/performer/portfolio", new
        {
            title = "Работа", afterUrl = "/uploads/a.jpg"
        }, token);

    [Fact]
    public async Task Verification_status_shows_limits()
    {
        var (performer, performerId) = await RegisterAsync("Performer");

        var before = await (await Client.CallAsync(HttpMethod.Get, "/api/performer/verification", token: performer))
            .ReadAsync<VerificationStatusDto>();
        Assert.False(before.IsVerified);
        Assert.Equal(3, before.Limits.MaxServices);
        Assert.Equal(5, before.Limits.MaxPortfolioItems);
        Assert.False(before.Limits.CanSendOffers);

        await VerifyPerformerAsync(performerId);

        var after = await (await Client.CallAsync(HttpMethod.Get, "/api/performer/verification", token: performer))
            .ReadAsync<VerificationStatusDto>();
        Assert.Null(after.Limits.MaxServices);
        Assert.Equal(30, after.Limits.MaxPortfolioItems);
        Assert.True(after.Limits.CanSendOffers);
    }

    [Fact]
    public async Task Unverified_performer_cannot_exceed_services_limit_even_by_hiding_and_showing()
    {
        var categoryId = await CreateCategoryAsync();
        var (performer, performerId) = await RegisterAsync("Performer");

        var created = new List<ServiceDto>();
        for (var i = 0; i < 3; i++)
        {
            var response = await AddServiceAsync(performer, categoryId);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            created.Add(await response.ReadAsync<ServiceDto>());
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await AddServiceAsync(performer, categoryId)).StatusCode);

        // скрытая услуга по-прежнему считается: четвёртую создать нельзя
        await Client.CallAsync(HttpMethod.Patch, $"/api/performer/services/{created[0].Id}/hide", token: performer);
        Assert.Equal(HttpStatusCode.Forbidden, (await AddServiceAsync(performer, categoryId)).StatusCode);

        // после проверки лимита нет
        await VerifyPerformerAsync(performerId);
        Assert.Equal(HttpStatusCode.OK, (await AddServiceAsync(performer, categoryId)).StatusCode);
    }

    [Fact]
    public async Task Unverified_performer_cannot_exceed_portfolio_limit()
    {
        var (performer, performerId) = await RegisterAsync("Performer");

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await AddPortfolioAsync(performer)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await AddPortfolioAsync(performer)).StatusCode);

        await VerifyPerformerAsync(performerId);
        Assert.Equal(HttpStatusCode.OK, (await AddPortfolioAsync(performer)).StatusCode);
    }

    [Fact]
    public async Task Unverified_performer_cannot_send_offers_but_can_still_work_with_orders()
    {
        var categoryId = await CreateCategoryAsync();
        var (performer, performerId) = await RegisterAsync("Performer");
        var service = await CreateServiceAsync(performer, categoryId);
        var (client, _) = await RegisterAsync("Client");

        var request = await (await Client.CallAsync(HttpMethod.Post, "/api/requests", new
        {
            categoryId, title = "Уборка", description = "После ремонта", address = "Минск",
            contactName = "К", contactPhone = "+375291234567"
        }, client)).ReadAsync<RequestDto>();

        var denied = await Client.CallAsync(HttpMethod.Put, $"/api/performer/requests/{request.Id}/offer",
            new { serviceId = service.Id, price = 50 }, performer);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        await VerifyPerformerAsync(performerId);

        var allowed = await Client.CallAsync(HttpMethod.Put, $"/api/performer/requests/{request.Id}/offer",
            new { serviceId = service.Id, price = 50 }, performer);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }
}
