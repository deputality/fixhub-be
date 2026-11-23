using System.Net;
using FixHub.Api.DTOs;
using Xunit;

namespace FixHub.Tests.Integration;

[Collection("api")]
public class CategoryStyleTests : IntegrationTestBase
{
    public CategoryStyleTests(ApiFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Admin_sets_category_style_and_clients_see_it()
    {
        var admin = await LoginAdminAsync();

        var created = await Client.CallAsync(HttpMethod.Post, "/api/categories",
            new { name = $"Style-{Guid.NewGuid():N}", icon = "car", color = "#EF4444" }, admin);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var dto = await created.ReadAsync<CategoryDto>();
        Assert.Equal("car", dto.Icon);
        Assert.Equal("#ef4444", dto.Color);

        // публичный каталог категорий отдаёт стиль
        var list = await Client.CallAsync(HttpMethod.Get, "/api/categories");
        var item = (await list.ReadAsync<List<CategoryDto>>()).Single(c => c.Id == dto.Id);
        Assert.Equal("car", item.Icon);
        Assert.Equal("#ef4444", item.Color);

        // сброс на автоматический подбор
        var reset = await Client.CallAsync(HttpMethod.Put, $"/api/categories/{dto.Id}",
            new { name = dto.Name, icon = (string?)null, color = (string?)null }, admin);
        var after = await reset.ReadAsync<CategoryDto>();
        Assert.Null(after.Icon);
        Assert.Null(after.Color);
    }

    [Theory]
    [InlineData("Wrench!", "#112233")]
    [InlineData("car", "red")]
    [InlineData("car", "#12345")]
    public async Task Invalid_style_is_rejected(string icon, string color)
    {
        var admin = await LoginAdminAsync();

        var response = await Client.CallAsync(HttpMethod.Post, "/api/categories",
            new { name = $"Bad-{Guid.NewGuid():N}", icon, color }, admin);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
