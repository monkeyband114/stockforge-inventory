using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StockForge.Tests.Integration.Support;

public sealed record ApiResponse<T>(HttpStatusCode Status, T? Value, JsonElement? Problem, HttpResponseHeaders Headers)
{
    public string? Code => Problem is { } p && p.TryGetProperty("code", out var code) ? code.GetString() : null;

    public T Ok()
    {
        Assert.True((int)Status is >= 200 and < 300, $"Expected success but got {(int)Status}: {Problem}");
        return Value!;
    }
}

/// <summary>Small typed HTTP client over the in-memory test server.</summary>
public sealed class ApiClient(HttpClient http, string? accessToken) : IDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public string? AccessToken { get; set; } = accessToken;

    public HttpClient Http => http;

    public Task<ApiResponse<T>> GetAsync<T>(string path) => SendAsync<T>(HttpMethod.Get, path, null);

    public Task<ApiResponse<T>> PostAsync<T>(string path, object? body = null, IDictionary<string, string>? headers = null) =>
        SendAsync<T>(HttpMethod.Post, path, body, headers);

    public Task<ApiResponse<T>> PutAsync<T>(string path, object? body) => SendAsync<T>(HttpMethod.Put, path, body);

    public Task<ApiResponse<T>> DeleteAsync<T>(string path) => SendAsync<T>(HttpMethod.Delete, path, null);

    public async Task<ApiResponse<T>> SendAsync<T>(HttpMethod method, string path, object? body, IDictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        if (AccessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        }

        foreach (var (key, value) in headers ?? new Dictionary<string, string>())
        {
            request.Headers.TryAddWithoutValidation(key, value);
        }

        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode)
        {
            var value = string.IsNullOrEmpty(text) || typeof(T) == typeof(object) ? default : JsonSerializer.Deserialize<T>(text, Json);
            return new ApiResponse<T>(response.StatusCode, value, null, response.Headers);
        }

        JsonElement? problem = null;
        if (!string.IsNullOrEmpty(text))
        {
            try
            {
                problem = JsonElement.Parse(text);
            }
            catch (JsonException)
            {
            }
        }

        return new ApiResponse<T>(response.StatusCode, default, problem, response.Headers);
    }

    public void Dispose() => http.Dispose();
}
