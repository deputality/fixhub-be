using System.Net;
using FixHub.Api.DTOs;
using Xunit;

namespace FixHub.Tests.Integration;

[Collection("api")]
public class AdminStatsTests : IntegrationTestBase
{
    public AdminStatsTests(ApiFixture fixture) : base(fixture)
    {
    }

    private async Task<AdminStatsDto> GetStatsAsync(string adminToken)
    {
        var response = await Client.CallAsync(HttpMethod.Get, "/api/admin/stats", token: adminToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<AdminStatsDto>();
    }

    [Fact]
    public async Task Stats_require_admin_role()
    {
        var anonymous = await Client.CallAsync(HttpMethod.Get, "/api/admin/stats");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var (client, _) = await RegisterAsync("Client");
        var forbidden = await Client.CallAsync(HttpMethod.Get, "/api/admin/stats", token: client);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Stats_change_after_order_is_completed()
    {
        var admin = await LoginAdminAsync();
        var before = await GetStatsAsync(admin);

        var categoryId = await CreateCategoryAsync();
        var (performer, _) = await RegisterAsync("Performer");
        var service = await CreateServiceAsync(performer, categoryId, 50);
        var (client, _) = await RegisterAsync("Client");

        Assert.Equal(HttpStatusCode.OK, (await AddToCartAsync(client, service.Id)).StatusCode);
        var checkout = await CheckoutAsync(client);
        var order = Assert.Single(await checkout.ReadAsync<List<OrderDto>>());

        foreach (var action in new[] { "confirm", "start", "complete" })
            Assert.Equal(HttpStatusCode.OK, (await OrderActionAsync(performer, order.Id, action)).StatusCode);

        var after = await GetStatsAsync(admin);

        Assert.Equal(before.ClientsCount + 1, after.ClientsCount);
        Assert.Equal(before.PerformersCount + 1, after.PerformersCount);
        Assert.Equal(before.ServicesCount + 1, after.ServicesCount);
        Assert.Equal(before.OrdersCount + 1, after.OrdersCount);
        Assert.Equal(before.OrdersLast30Days + 1, after.OrdersLast30Days);
        Assert.Equal(before.OrdersByStatus["Completed"] + 1, after.OrdersByStatus["Completed"]);
        Assert.Equal(before.CompletedAmount + 50, after.CompletedAmount);
        Assert.True(after.TopPerformers.Count <= 5);
    }
}