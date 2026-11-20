using System.Net;
using FixHub.Api.DTOs;
using Xunit;

namespace FixHub.Tests.Integration;

[Collection("api")]
public class PerformerVerificationTests : IntegrationTestBase
{
    public PerformerVerificationTests(ApiFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Admin_verifies_performer_and_badge_shows_in_catalog_and_profile_page()
    {
        var admin = await LoginAdminAsync();
        var categoryId = await CreateCategoryAsync();
        var (performer, performerId) = await RegisterAsync("Performer");
        var service = await CreateServiceAsync(performer, categoryId);

        // новый мастер не проверен
        var before = await Client.CallAsync(HttpMethod.Get, $"/api/catalog/{service.Id}");
        Assert.False((await before.ReadAsync<ServiceDto>()).PerformerIsVerified);

        var verify = await Client.CallAsync(HttpMethod.Post,
            $"/api/admin/users/{performerId}/verify", token: admin);
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        Assert.True((await verify.ReadAsync<AdminUserDto>()).IsVerified);

        var after = await Client.CallAsync(HttpMethod.Get, $"/api/catalog/{service.Id}");
        Assert.True((await after.ReadAsync<ServiceDto>()).PerformerIsVerified);

        var page = await Client.CallAsync(HttpMethod.Get, $"/api/performers/{performerId}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.True((await page.ReadAsync<PerformerPublicDto>()).IsVerified);

        var unverify = await Client.CallAsync(HttpMethod.Post,
            $"/api/admin/users/{performerId}/unverify", token: admin);
        Assert.False((await unverify.ReadAsync<AdminUserDto>()).IsVerified);
    }

    [Fact]
    public async Task Only_admin_can_verify_and_only_performers_can_be_verified()
    {
        var admin = await LoginAdminAsync();
        var (performer, performerId) = await RegisterAsync("Performer");
        var (_, clientId) = await RegisterAsync("Client");

        var byPerformer = await Client.CallAsync(HttpMethod.Post,
            $"/api/admin/users/{performerId}/verify", token: performer);
        Assert.Equal(HttpStatusCode.Forbidden, byPerformer.StatusCode);

        var client = await Client.CallAsync(HttpMethod.Post,
            $"/api/admin/users/{clientId}/verify", token: admin);
        Assert.Equal(HttpStatusCode.BadRequest, client.StatusCode);
    }

    [Fact]
    public async Task Performer_page_is_public_and_lists_only_own_services()
    {
        var categoryId = await CreateCategoryAsync();
        var (performer, performerId) = await RegisterAsync("Performer");
        var (other, _) = await RegisterAsync("Performer");
        var mine = await CreateServiceAsync(performer, categoryId);
        await CreateServiceAsync(other, categoryId);

        // без токена, как гость
        var page = await Client.CallAsync(HttpMethod.Get, $"/api/performers/{performerId}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal(1, (await page.ReadAsync<PerformerPublicDto>()).ServicesCount);

        var catalog = await Client.CallAsync(HttpMethod.Get, $"/api/catalog?performerId={performerId}&pageSize=50");
        var items = (await catalog.ReadAsync<PagedResult<ServiceDto>>()).Items;
        Assert.Single(items);
        Assert.Equal(mine.Id, items[0].Id);
    }

    [Fact]
    public async Task Blocked_performer_page_is_not_found()
    {
        var admin = await LoginAdminAsync();
        var (_, performerId) = await RegisterAsync("Performer");

        await Client.CallAsync(HttpMethod.Post, $"/api/admin/users/{performerId}/block", token: admin);

        var page = await Client.CallAsync(HttpMethod.Get, $"/api/performers/{performerId}");
        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
    }
}
