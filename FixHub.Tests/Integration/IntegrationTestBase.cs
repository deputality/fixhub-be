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

    // Уникальные значения, чтобы тесты не мешали друг другу
    protected static string UniqueEmail(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}@test.com";

    protected static string UniqueTitle() => $"Svc-{Guid.NewGuid():N}";

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

    protected async Task<int> CreateCategoryAsync()
    {
        var admin = await LoginAdminAsync();
        var response = await Client.CallAsync(HttpMethod.Post, "/api/categories",
            new { name = $"Cat-{Guid.NewGuid():N}" }, admin);

        response.EnsureSuccessStatusCode();
        return (await response.ReadAsync<CategoryDto>()).Id;
    }

    protected async Task<ServiceDto> CreateServiceAsync(
        string performerToken, int categoryId, decimal price = 50, string? title = null)
    {
        var response = await Client.CallAsync(HttpMethod.Post, "/api/performer/services", new
        {
            categoryId,
            title = title ?? UniqueTitle(),
            description = "Test description",
            price,
            unit = "Hour"
        }, performerToken);

        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<ServiceDto>();
    }

    protected Task<HttpResponseMessage> AddToCartAsync(string clientToken, int serviceId, int quantity = 1) =>
        Client.CallAsync(HttpMethod.Post, "/api/cart/items", new { serviceId, quantity }, clientToken);

    protected Task<HttpResponseMessage> CheckoutAsync(string clientToken) =>
        Client.CallAsync(HttpMethod.Post, "/api/orders", new
        {
            address = "Minsk, Test st. 1",
            contactName = "Test Client",
            contactPhone = "+375291234567"
        }, clientToken);

    // action: confirm, start, complete, reject, cancel
    protected Task<HttpResponseMessage> OrderActionAsync(string token, int orderId, string action) =>
        Client.CallAsync(HttpMethod.Post, $"/api/orders/{orderId}/{action}", token: token);
}