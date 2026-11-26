using System.Net;
using System.Text.Json;
using Xunit;

namespace FixHub.Tests.Integration;

[Collection("api")]
public class HealthTests : IntegrationTestBase
{
    public HealthTests(ApiFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Health_endpoint_is_public_and_reports_ok_when_database_is_available()
    {
        var response = await Client.CallAsync(HttpMethod.Get, "/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ok", json.RootElement.GetProperty("status").GetString());
    }
}
