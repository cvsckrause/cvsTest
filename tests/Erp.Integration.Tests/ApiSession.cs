using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Erp.Integration.Tests;

internal sealed class ApiSession : IDisposable
{
    private readonly HttpClient _client;

    public ApiSession(WebApplicationFactory<Program> factory, Guid tenantId)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add(TestTenantHandler.TenantHeader, tenantId.ToString());
        _client.DefaultRequestHeaders.Add(TestTenantHandler.UserHeader, "integration-test");
    }

    public Task<ApiResult> GetAsync(string path) =>
        SendAsync(HttpMethod.Get, path);

    public Task<ApiResult> PostAsync(string path, object body, string? idempotencyKey = null) =>
        SendAsync(HttpMethod.Post, path, body, idempotencyKey);

    public async Task<ApiResult> PostOkAsync(string path, object body, string? idempotencyKey = null)
    {
        var result = await PostAsync(path, body, idempotencyKey);
        if ((int)result.Status is < 200 or >= 300)
        {
            throw new InvalidOperationException($"{path} returned {(int)result.Status}: {result.Body}");
        }

        return result;
    }

    public void Dispose() => _client.Dispose();

    private async Task<ApiResult> SendAsync(HttpMethod method, string path, object? body = null, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        using var response = await _client.SendAsync(request);
        return new ApiResult(response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}

internal readonly record struct ApiResult(HttpStatusCode Status, string Body)
{
    public Guid Id
    {
        get
        {
            using var document = JsonDocument.Parse(Body);
            return document.RootElement.GetProperty("id").GetGuid();
        }
    }

    public bool Replayed
    {
        get
        {
            using var document = JsonDocument.Parse(Body);
            return document.RootElement.TryGetProperty("replayed", out var replayed) && replayed.GetBoolean();
        }
    }

    public bool Contains(Guid id) =>
        Body.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase);

    public decimal OnHand(Guid articleId)
    {
        using var document = JsonDocument.Parse(Body);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.GetProperty("articleId").GetGuid() == articleId)
            {
                return item.GetProperty("onHand").GetDecimal();
            }
        }

        throw new InvalidOperationException($"Stock for {articleId} was not returned.");
    }
}
