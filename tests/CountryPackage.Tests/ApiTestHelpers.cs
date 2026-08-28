using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CountryPackage.Tests;

internal static class ApiTestHelpers
{
    internal static readonly JsonSerializerOptions Json = CreateJson();

    internal static HttpRequestMessage Command(HttpMethod method, string path, string userId, object? body = null, string? key = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-User-Id", userId);
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        return request;
    }

    internal static HttpRequestMessage Read(string path, string userId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-User-Id", userId);
        return request;
    }

    internal static async Task<HttpResponseMessage> UploadDocxAsync(
        HttpClient client,
        string userId,
        Guid packageId,
        int order,
        string documentPath,
        string? key = null)
    {
        var content = new MultipartFormDataContent();
        var bytes = await File.ReadAllBytesAsync(documentPath);
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        content.Add(file, "file", Path.GetFileName(documentPath));
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/packages/{packageId}/steps/{order}/document") { Content = content };
        request.Headers.Add("X-User-Id", userId);
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    internal static async Task<T> BodyAsync<T>(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<T>(Json);
        return body ?? throw new InvalidOperationException("Response body was empty.");
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
