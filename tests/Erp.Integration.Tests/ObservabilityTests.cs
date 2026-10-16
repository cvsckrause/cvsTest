extern alias WorkerHost;

using Erp.Observability;
using OutboxMetrics = WorkerHost::Erp.Worker.OutboxMetrics;
using WorkerOptions = WorkerHost::Erp.Worker.WorkerOptions;

namespace Erp.Integration.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ObservabilityTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Release_correlation_is_logged_by_the_api_and_the_worker()
    {
        var correlationId = Guid.NewGuid().ToString("N");
        using var api = fixture.OpenSession(fixture.TenantA);
        var created = await ReleaseReadyOrderAsync(api, "OBS");

        fixture.Logs.Clear();
        await api.PostOkAsync(
            $"/api/v1/sales-orders/{created.Id}/release",
            new { },
            Guid.NewGuid().ToString("N"),
            correlationId);

        Assert.Contains(
            fixture.Logs.Entries,
            log => log.Message.Contains("/release", StringComparison.Ordinal)
                && Has(log, RequestLog.CorrelationId, correlationId)
                && Has(log, RequestLog.TenantId, fixture.TenantA.ToString())
                && Has(log, RequestLog.UserId, "integration-a"));

        var messageId = await fixture.Probe.OutboxIdForOrderAsync(fixture.TenantA, created.Id);
        Assert.Equal(correlationId, await fixture.Probe.OutboxCorrelationAsync(fixture.TenantA, messageId));

        var pending = await OutboxMetrics.ReadAsync(fixture.WorkerConnectionString, CancellationToken.None);
        Assert.True(pending.Backlog >= 1);
        Assert.True(pending.OldestPendingAgeSeconds >= 0);

        var workerLogs = new CapturedLogs();
        await OutboxProcessor.ProcessOnceAsync(fixture.WorkerConnectionString, logs: workerLogs);

        Assert.Contains(
            workerLogs.Entries,
            log => log.Message.Contains(messageId.ToString(), StringComparison.OrdinalIgnoreCase)
                && Has(log, RequestLog.CorrelationId, correlationId)
                && Has(log, RequestLog.TenantId, fixture.TenantA.ToString())
                && Has(log, RequestLog.UserId, "integration-a"));
    }

    [Fact]
    public async Task Dead_lettered_message_raises_the_outbox_alarm()
    {
        var before = await OutboxMetrics.ReadAsync(fixture.WorkerConnectionString, CancellationToken.None);
        using var api = fixture.OpenSession(fixture.TenantA);
        var created = await ReleaseReadyOrderAsync(api, "DLQ");
        await api.PostOkAsync($"/api/v1/sales-orders/{created.Id}/release", new { }, Guid.NewGuid().ToString("N"));
        var messageId = await fixture.Probe.OutboxIdForOrderAsync(fixture.TenantA, created.Id);
        await fixture.Probe.ReplacePayloadAsync(fixture.TenantA, messageId, "{");

        var options = new WorkerOptions { MaxAttempts = 3, BaseDelaySeconds = 0 };
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await OutboxProcessor.ProcessOnceAsync(fixture.WorkerConnectionString, options);
        }

        var after = await OutboxMetrics.ReadAsync(fixture.WorkerConnectionString, CancellationToken.None);
        Assert.Equal(before.DeadLetterCount + 1, after.DeadLetterCount);
        Assert.True(after.RaisesDeadLetterAlarm);

        using var document = System.Text.Json.JsonDocument.Parse(
            OutboxMetricsDocument.ToJson(after, DateTimeOffset.UnixEpoch));
        Assert.Equal(after.DeadLetterCount, document.RootElement.GetProperty(OutboxMetricsDocument.DeadLetterCount).GetInt32());
        Assert.Equal(
            OutboxMetricsDocument.Namespace,
            document.RootElement.GetProperty("_aws").GetProperty("CloudWatchMetrics")[0].GetProperty("Namespace").GetString());
    }

    private async Task<ApiResult> ReleaseReadyOrderAsync(ApiSession api, string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var customer = await api.PostOkAsync("/api/v1/customers", new { number = $"{label}-{suffix}", name = $"{label} Customer" });
        var address = await api.PostOkAsync("/api/v1/addresses", new
        {
            name = $"{label} {suffix}",
            street = "4 Test Street",
            postalCode = "10115",
            city = "Berlin",
            countryCode = "DE"
        });
        await api.PostOkAsync($"/api/v1/customers/{customer.Id}/addresses", new { addressId = address.Id, role = "Shipping" });
        var article = await api.PostOkAsync("/api/v1/articles", new
        {
            sku = $"{label}-{suffix}",
            name = $"{label} Article",
            salesPrice = 10m,
            openingStock = 5m,
            warehouseCode = "MAIN"
        });
        return await api.PostOkAsync(
            "/api/v1/sales-orders",
            new
            {
                customerId = customer.Id,
                shipToAddressId = address.Id,
                orderNumber = $"{label}-{suffix}",
                lines = new[] { new { articleId = article.Id, quantity = 1m } }
            },
            Guid.NewGuid().ToString("N"));
    }

    private static bool Has(CapturedLog log, string key, string value) =>
        log.Scopes.Any(pair => pair.Key == key && pair.Value?.ToString() == value);
}
