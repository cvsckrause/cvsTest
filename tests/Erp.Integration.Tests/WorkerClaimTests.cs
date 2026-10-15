extern alias WorkerHost;

using OutboxReplay = WorkerHost::Erp.Worker.OutboxReplay;
using WorkerOptions = WorkerHost::Erp.Worker.WorkerOptions;

namespace Erp.Integration.Tests;

[Collection(PostgresCollection.Name)]
public sealed class WorkerClaimTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Worker_delivers_orders_for_both_tenants()
    {
        var role = await fixture.Probe.RoleAsync(fixture.WorkerConnectionString);
        Assert.Equal("erp_worker", role.User);
        Assert.False(role.Superuser);
        Assert.False(role.BypassRls);

        var orderA = await ReleaseOrderAsync(fixture.TenantA, "BOTH-A");
        var orderB = await ReleaseOrderAsync(fixture.TenantB, "BOTH-B");

        await OutboxProcessor.ProcessOnceAsync(fixture.WorkerConnectionString);

        Assert.Equal(1, await fixture.Probe.CountDeliveriesAsync(fixture.TenantA, "DEL-" + orderA.Number));
        Assert.Equal(1, await fixture.Probe.CountInvoicesAsync(fixture.TenantA, "INV-" + orderA.Number));
        Assert.Equal(1, await fixture.Probe.CountDeliveriesAsync(fixture.TenantB, "DEL-" + orderB.Number));
        Assert.Equal(1, await fixture.Probe.CountInvoicesAsync(fixture.TenantB, "INV-" + orderB.Number));
    }

    [Fact]
    public async Task Two_workers_produce_one_delivery()
    {
        var order = await ReleaseOrderAsync(fixture.TenantA, "RACE");

        await Task.WhenAll(
            OutboxProcessor.ProcessOnceAsync(fixture.WorkerConnectionString),
            OutboxProcessor.ProcessOnceAsync(fixture.WorkerConnectionString));

        Assert.Equal(1, await fixture.Probe.CountDeliveriesAsync(fixture.TenantA, "DEL-" + order.Number));
        Assert.Equal(1, await fixture.Probe.CountInvoicesAsync(fixture.TenantA, "INV-" + order.Number));
    }

    [Fact]
    public async Task Poison_message_is_dead_lettered_and_replays_after_the_payload_is_fixed()
    {
        var order = await ReleaseOrderAsync(fixture.TenantA, "POISON");
        var messageId = await fixture.Probe.OutboxIdForOrderAsync(fixture.TenantA, order.Id);
        var payload = await fixture.Probe.PayloadAsync(fixture.TenantA, messageId);
        await fixture.Probe.ReplacePayloadAsync(fixture.TenantA, messageId, "{");

        var options = new WorkerOptions { MaxAttempts = 3, BaseDelaySeconds = 0 };
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await OutboxProcessor.ProcessOnceAsync(fixture.WorkerConnectionString, options);
        }

        var failed = await fixture.Probe.OutboxStateAsync(fixture.TenantA, messageId);
        Assert.Equal(3, failed.Attempts);
        Assert.True(failed.DeadLettered);
        Assert.Equal(0, await fixture.Probe.CountDeliveriesAsync(fixture.TenantA, "DEL-" + order.Number));

        await OutboxProcessor.ProcessOnceAsync(fixture.WorkerConnectionString, options);
        Assert.Equal(0, await fixture.Probe.CountDeliveriesAsync(fixture.TenantA, "DEL-" + order.Number));

        await fixture.Probe.ReplacePayloadAsync(fixture.TenantA, messageId, payload);
        Assert.True(await OutboxReplay.ReplayAsync(fixture.WorkerConnectionString, messageId));
        await OutboxProcessor.ProcessOnceAsync(fixture.WorkerConnectionString, options);

        Assert.Equal(1, await fixture.Probe.CountDeliveriesAsync(fixture.TenantA, "DEL-" + order.Number));
        Assert.Equal(1, await fixture.Probe.CountInvoicesAsync(fixture.TenantA, "INV-" + order.Number));
    }

    private async Task<ReleasedOrder> ReleaseOrderAsync(Guid tenantId, string label)
    {
        using var api = fixture.OpenSession(tenantId);
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
        var orderNumber = $"{label}-{suffix}";
        var created = await api.PostOkAsync(
            "/api/v1/sales-orders",
            new
            {
                customerId = customer.Id,
                shipToAddressId = address.Id,
                orderNumber,
                lines = new[] { new { articleId = article.Id, quantity = 1m } }
            },
            Guid.NewGuid().ToString("N"));
        await api.PostOkAsync($"/api/v1/sales-orders/{created.Id}/release", new { }, Guid.NewGuid().ToString("N"));
        return new ReleasedOrder(orderNumber, created.Id);
    }

    private sealed record ReleasedOrder(string Number, Guid Id);
}
