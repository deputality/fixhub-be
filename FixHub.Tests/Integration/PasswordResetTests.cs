using System.Net;
using System.Text.RegularExpressions;
using FixHub.Api.Common;
using FixHub.Api.DTOs;
using Xunit;

namespace FixHub.Tests.Integration;

[Collection("api")]
public class PasswordResetTests : IntegrationTestBase
{
    private const string NewPassword = "NewPass98765";

    public PasswordResetTests(ApiFixture fixture) : base(fixture)
    {
    }

    private async Task<string> RegisterWithEmailAsync(string email)
    {
        var response = await Client.CallAsync(HttpMethod.Post, "/api/auth/register", new
        {
            email,
            password = Password,
            fullName = "Reset User",
            role = "Client"
        });
        response.EnsureSuccessStatusCode();
        return email;
    }

    private Task<HttpResponseMessage> ForgotAsync(string email) =>
        Client.CallAsync(HttpMethod.Post, "/api/auth/forgot-password", new { email });

    private Task<HttpResponseMessage> ResetAsync(string token, string password) =>
        Client.CallAsync(HttpMethod.Post, "/api/auth/reset-password", new { token, newPassword = password });

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        Client.CallAsync(HttpMethod.Post, "/api/auth/login", new { email, password });

    // Токен лежит в ссылке из письма: .../reset-password?token=<64 символа>
    private string TokenFromEmail(string email)
    {
        var mail = Assert.Single(Emails.To(email));
        var match = Regex.Match(mail.Body, @"/reset-password\?token=([0-9a-f]+)");
        Assert.True(match.Success, "В письме нет ссылки для сброса пароля");
        return match.Groups[1].Value;
    }

    [Fact]
    public async Task Forgot_password_for_unknown_email_gives_same_answer_and_sends_nothing()
    {
        var email = UniqueEmail("nobody");

        var response = await ForgotAsync(email);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(Emails.To(email));
    }

    [Fact]
    public async Task Link_from_email_sets_new_password_and_old_one_stops_working()
    {
        var email = await RegisterWithEmailAsync(UniqueEmail("reset"));

        Assert.Equal(HttpStatusCode.OK, (await ForgotAsync(email)).StatusCode);
        var token = TokenFromEmail(email);

        var reset = await ResetAsync(token, NewPassword);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Reset_link_works_only_once()
    {
        var email = await RegisterWithEmailAsync(UniqueEmail("once"));
        await ForgotAsync(email);
        var token = TokenFromEmail(email);

        Assert.Equal(HttpStatusCode.OK, (await ResetAsync(token, NewPassword)).StatusCode);

        var second = await ResetAsync(token, "AnotherPass1234");
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);

        // после повторной попытки действует всё тот же пароль, заданный первым разом
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Reset_with_wrong_token_returns_400_in_unified_format()
    {
        var response = await ResetAsync(new string('a', 64), NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.ReadAsync<ApiError>();
        Assert.False(string.IsNullOrEmpty(error.Error));
    }

    [Fact]
    public async Task Reset_with_too_short_password_is_rejected_and_link_stays_valid()
    {
        var email = await RegisterWithEmailAsync(UniqueEmail("short"));
        await ForgotAsync(email);
        var token = TokenFromEmail(email);

        var tooShort = await ResetAsync(token, "123");
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);

        // ошибка валидации не «сжигает» ссылку: с нормальным паролем она ещё работает
        Assert.Equal(HttpStatusCode.OK, (await ResetAsync(token, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Second_request_within_a_minute_does_not_send_another_email()
    {
        var email = await RegisterWithEmailAsync(UniqueEmail("spam"));

        await ForgotAsync(email);
        await ForgotAsync(email);
        await ForgotAsync(email);

        Assert.Single(Emails.To(email));
    }
}