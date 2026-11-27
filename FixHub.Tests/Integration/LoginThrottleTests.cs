using System.Net;
using FixHub.Api.Common;
using FixHub.Api.DTOs;
using Xunit;

namespace FixHub.Tests.Integration;

// По умолчанию вход закрывается после 5 неверных паролей подряд (Security:MaxLoginAttempts)
[Collection("api")]
public class LoginThrottleTests : IntegrationTestBase
{
    public LoginThrottleTests(ApiFixture fixture) : base(fixture)
    {
    }

    private async Task<string> RegisterWithEmailAsync()
    {
        var email = UniqueEmail("throttle");
        var response = await Client.CallAsync(HttpMethod.Post, "/api/auth/register", new
        {
            email,
            password = Password,
            fullName = "Throttle User",
            role = "Client"
        });
        response.EnsureSuccessStatusCode();
        return email;
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        Client.CallAsync(HttpMethod.Post, "/api/auth/login", new { email, password });

    [Fact]
    public async Task After_five_wrong_passwords_login_is_locked_even_with_correct_password()
    {
        var email = await RegisterWithEmailAsync();

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, "wrong-password")).StatusCode);

        var locked = await LoginAsync(email, Password);

        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.True(locked.Headers.Contains("Retry-After"));
        var error = await locked.ReadAsync<ApiError>();
        Assert.Equal(429, error.Status);
        Assert.Contains("мин", error.Error);
    }

    [Fact]
    public async Task Successful_login_resets_the_counter()
    {
        var email = await RegisterWithEmailAsync();

        for (var i = 0; i < 4; i++)
            await LoginAsync(email, "wrong-password");

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, Password)).StatusCode);

        // счётчик начался заново: ещё 4 ошибки подряд блокировку не вызывают
        for (var i = 0; i < 4; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, "wrong-password")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, Password)).StatusCode);
    }

    [Fact]
    public async Task Unknown_email_is_throttled_too_so_answers_do_not_reveal_registration()
    {
        var email = UniqueEmail("ghost");

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, "whatever123")).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(email, "whatever123")).StatusCode);
    }

    [Fact]
    public async Task Lock_of_one_account_does_not_affect_another()
    {
        var victim = await RegisterWithEmailAsync();
        var other = await RegisterWithEmailAsync();

        for (var i = 0; i < 5; i++)
            await LoginAsync(victim, "wrong-password");

        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(victim, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(other, Password)).StatusCode);
    }
}