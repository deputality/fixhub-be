using System.Net;
using FixHub.Api.Common;
using FixHub.Api.DTOs;
using Xunit;

namespace FixHub.Tests.Integration;

[Collection("api")]
public class AuthTests : IntegrationTestBase
{
    public AuthTests(ApiFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Register_then_login_then_me_works()
    {
        var email = UniqueEmail("client");

        var register = await Client.CallAsync(HttpMethod.Post, "/api/auth/register", new
        {
            email,
            password = Password,
            fullName = "Test User",
            phone = "+375291234567",
            role = "Client"
        });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var registered = await register.ReadAsync<AuthResponse>();
        Assert.False(string.IsNullOrEmpty(registered.Token));
        Assert.Equal("Client", registered.User.Role);

        var login = await Client.CallAsync(HttpMethod.Post, "/api/auth/login",
            new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.ReadAsync<AuthResponse>()).Token;

        var me = await Client.CallAsync(HttpMethod.Get, "/api/auth/me", token: token);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(email, (await me.ReadAsync<UserDto>()).Email);
    }

    [Fact]
    public async Task Register_with_existing_email_returns_409_in_unified_format()
    {
        var email = UniqueEmail("dup");
        var body = new { email, password = Password, fullName = "Test", role = "Client" };

        var first = await Client.CallAsync(HttpMethod.Post, "/api/auth/register", body);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await Client.CallAsync(HttpMethod.Post, "/api/auth/register", body);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var error = await second.ReadAsync<ApiError>();
        Assert.Equal(409, error.Status);
        Assert.False(string.IsNullOrEmpty(error.Error));
        Assert.False(string.IsNullOrEmpty(error.TraceId));
    }

    [Fact]
    public async Task Registration_as_admin_is_rejected()
    {
        var response = await Client.CallAsync(HttpMethod.Post, "/api/auth/register", new
        {
            email = UniqueEmail("hacker"),
            password = Password,
            fullName = "Hacker",
            role = "Admin"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_registration_data_returns_400_with_details()
    {
        var response = await Client.CallAsync(HttpMethod.Post, "/api/auth/register", new
        {
            email = "not-an-email",
            password = "1",
            fullName = "X",
            role = "Client"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.ReadAsync<ApiError>();
        Assert.Equal(400, error.Status);
        Assert.NotNull(error.Details);
        Assert.NotEmpty(error.Details!);
        Assert.False(string.IsNullOrEmpty(error.TraceId));
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        var email = UniqueEmail("client");
        await Client.CallAsync(HttpMethod.Post, "/api/auth/register",
            new { email, password = Password, fullName = "Test", role = "Client" });

        var response = await Client.CallAsync(HttpMethod.Post, "/api/auth/login",
            new { email, password = "WrongPassword1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = await response.ReadAsync<ApiError>();
        Assert.Equal(401, error.Status);
    }

    [Fact]
    public async Task Protected_endpoint_without_token_returns_401_in_unified_format()
    {
        var response = await Client.CallAsync(HttpMethod.Get, "/api/cart");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = await response.ReadAsync<ApiError>();
        Assert.Equal(401, error.Status);
        Assert.False(string.IsNullOrEmpty(error.TraceId));
    }

    [Fact]
    public async Task Client_cannot_use_admin_endpoints()
    {
        var (clientToken, _) = await RegisterAsync("Client");

        var users = await Client.CallAsync(HttpMethod.Get, "/api/admin/users", token: clientToken);
        Assert.Equal(HttpStatusCode.Forbidden, users.StatusCode);
        Assert.Equal(403, (await users.ReadAsync<ApiError>()).Status);

        var category = await Client.CallAsync(HttpMethod.Post, "/api/categories",
            new { name = "Hack" }, clientToken);
        Assert.Equal(HttpStatusCode.Forbidden, category.StatusCode);
    }

    [Fact]
    public async Task Blocked_user_loses_access_immediately_and_cannot_login()
    {
        var admin = await LoginAdminAsync();

        var email = UniqueEmail("victim");
        var register = await Client.CallAsync(HttpMethod.Post, "/api/auth/register",
            new { email, password = Password, fullName = "Victim", role = "Client" });
        var auth = await register.ReadAsync<AuthResponse>();

        // до блокировки токен работает
        var before = await Client.CallAsync(HttpMethod.Get, "/api/cart", token: auth.Token);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        var block = await Client.CallAsync(HttpMethod.Post,
            $"/api/admin/users/{auth.User.Id}/block", token: admin);
        Assert.Equal(HttpStatusCode.OK, block.StatusCode);

        // уже выданный токен перестал работать сразу
        var after = await Client.CallAsync(HttpMethod.Get, "/api/cart", token: auth.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);

        // войти заблокированный пользователь не может
        var login = await Client.CallAsync(HttpMethod.Post, "/api/auth/login",
            new { email, password = Password });
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);

        // после разблокировки вход снова работает
        var unblock = await Client.CallAsync(HttpMethod.Post,
            $"/api/admin/users/{auth.User.Id}/unblock", token: admin);
        Assert.Equal(HttpStatusCode.OK, unblock.StatusCode);

        var loginAgain = await Client.CallAsync(HttpMethod.Post, "/api/auth/login",
            new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, loginAgain.StatusCode);
    }

    [Fact]
    public async Task Administrator_cannot_be_blocked()
    {
        var admin = await LoginAdminAsync();

        var me = await Client.CallAsync(HttpMethod.Get, "/api/auth/me", token: admin);
        var adminId = (await me.ReadAsync<UserDto>()).Id;

        var response = await Client.CallAsync(HttpMethod.Post,
            $"/api/admin/users/{adminId}/block", token: admin);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}