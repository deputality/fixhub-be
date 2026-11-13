using FixHub.Api.DTOs;

namespace FixHub.Tests.Integration;

public abstract class IntegrationTestBase
{
    protected const string Password = "Test12345";

    protected HttpClient Client { get; }

    protected IntegrationTestBase(ApiFixture fixture)
    {
        Client = fixture.Client;
    }

    // Уникальный email, чтобы тесты не мешали друг другу
    protected static string UniqueEmail(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}@test.com";

    protected async Task<(string Token, int UserId)> RegisterAsync(string role)
    {
        var response = await Client.CallAsync(HttpMethod.Post, "/api/auth/register", new
        {
            email = UniqueEmail(role.ToLowerInvariant()),
            password = Password,
            fullName = $"Test {role}",
            phone = "+375291234567",
            role
        });

        response.EnsureSuccessStatusCode();
        var auth = await response.ReadAsync<AuthResponse>();
        return (auth.Token, auth.User.Id);
    }

    // Администратор создаётся при старте приложения из настроек appsettings.json
    protected async Task<string> LoginAdminAsync()
    {
        var response = await Client.CallAsync(HttpMethod.Post, "/api/auth/login", new
        {
            email = "admin@fixhub.local",
            password = "Admin12345"
        });

        response.EnsureSuccessStatusCode();
        return (await response.ReadAsync<AuthResponse>()).Token;
    }
}