using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FixHub.Tests.Integration;

public static class ApiExtensions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<HttpResponseMessage> CallAsync(
        this HttpClient client,
        HttpMethod method,
        string url,
        object? body = null,
        string? token = null)
    {
        var request = new HttpRequestMessage(method, url);

        if (token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body != null)
            request.Content = JsonContent.Create(body, options: Json);

        return await client.SendAsync(request);
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(Json);
        return value!;
    }
}