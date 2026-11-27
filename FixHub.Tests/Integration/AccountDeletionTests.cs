using System.Net;
using FixHub.Api.DTOs;
using Xunit;

namespace FixHub.Tests.Integration;

[Collection("api")]
public class AccountDeletionTests : IntegrationTestBase
{
    public AccountDeletionTests(ApiFixture fixture) : base(fixture)
    {
    }

    private Task<HttpResponseMessage> DeleteAsync(string token, string password) =>
        Client.CallAsync(HttpMethod.Post, "/api/profile/delete", new { password }, token);

    private async Task<(string Token, string Email)> RegisterWithEmailAsync(string role)
    {
        var email = UniqueEmail("del");
        var response = await Client.CallAsync(HttpMethod.Post, "/api/auth/register", new
        {
            email,
            password = Password,
            fullName = "Delete Me",
            phone = "+375291234567",
            role
        });
        response.EnsureSuccessStatusCode();
        return ((await response.ReadAsync<AuthResponse>()).Token, email);
    }

    [Fact]
    public async Task Wrong_password_does_not_delete_account()
    {
        var (token, _) = await RegisterWithEmailAsync("Client");

        var response = await DeleteAsync(token, "not-my-password");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var me = await Client.CallAsync(HttpMethod.Get, "/api/auth/me", token: token);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Deleted_client_cannot_log_in_and_old_token_stops_working()
    {
        var (token, email) = await RegisterWithEmailAsync("Client");

        var response = await DeleteAsync(token, Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var me = await Client.CallAsync(HttpMethod.Get, "/api/auth/me", token: token);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);

        var login = await Client.CallAsync(HttpMethod.Post, "/api/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Email_of_deleted_account_can_be_used_for_new_registration()
    {
        var (token, email) = await RegisterWithEmailAsync("Client");
        Assert.Equal(HttpStatusCode.OK, (await DeleteAsync(token, Password)).StatusCode);

        var again = await Client.CallAsync(HttpMethod.Post, "/api/auth/register", new
        {
            email,
            password = Password,
            fullName = "Same Person Again",
            role = "Client"
        });

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task Account_with_active_order_cannot_be_deleted_until_order_is_cancelled()
    {
        var categoryId = await CreateCategoryAsync();
        var (performerToken, _) = await RegisterAsync("Performer");
        var service = await CreateServiceAsync(performerToken, categoryId);
        var (clientToken, _) = await RegisterAsync("Client");

        Assert.Equal(HttpStatusCode.OK, (await AddToCartAsync(clientToken, service.Id)).StatusCode);
        var checkout = await CheckoutAsync(clientToken);
        Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);
        var order = Assert.Single(await checkout.ReadAsync<List<OrderDto>>());

        var blocked = await DeleteAsync(clientToken, Password);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        // мастеру тоже нельзя уйти, пока заказ не закрыт
        var performerBlocked = await DeleteAsync(performerToken, Password);
        Assert.Equal(HttpStatusCode.Conflict, performerBlocked.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await OrderActionAsync(clientToken, order.Id, "cancel")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await DeleteAsync(clientToken, Password)).StatusCode);
    }

    [Fact]
    public async Task Deleted_performer_disappears_from_public_pages()
    {
        var (token, _) = await RegisterWithEmailAsync("Performer");
        var me = await (await Client.CallAsync(HttpMethod.Get, "/api/auth/me", token: token)).ReadAsync<UserDto>();

        var before = await Client.CallAsync(HttpMethod.Get, $"/api/performers/{me.Id}");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await DeleteAsync(token, Password)).StatusCode);

        var after = await Client.CallAsync(HttpMethod.Get, $"/api/performers/{me.Id}");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Administrator_account_cannot_be_deleted()
    {
        var admin = await LoginAdminAsync();

        var response = await DeleteAsync(admin, "Admin12345");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
