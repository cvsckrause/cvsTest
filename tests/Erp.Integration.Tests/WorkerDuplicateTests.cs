namespace Erp.Integration.Tests;

[Collection(PostgresCollection.Name)]
public sealed class WorkerDuplicateTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Duplicate_outbox_message_is_processed_once()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var customer = await api.PostOkAsync("/api/v1/customers", new { number = $"WRK-{suffix}", name = "Worker Customer" });
        var address = await api.PostOkAsync("/api/v1/addresses", new
        {
            name = $"Worker {suffix}",
            street = "3 Test Street",
            postalCode = "10115",
            city = "Berlin",
            countryCode = "DE"
        });
        await api.PostOkAsync($"/api/v1/customers/{customer.Id}/addresses", new { addressId = address.Id, role = "Shipping" });
        var article = await api.PostOkAsync("/api/v1/articles", new
        {
            sku = $"WRK-{suffix}",
            name = "Worker Article",
            salesPrice = 10m,
            openingStock = 5m,
            warehouseCode = "MAIN"
        });
        var orderNumber = $"WRK-{suffix}";
        var order = await api.PostOkAsync(
            "/api/v1/sales-orders",
            new
            {
                customerId = customer.Id,
                shipToAddressId = address.Id,
                orderNumber,
                lines = new[] { new { articleId = article.Id, quantity = 1m } }
            },
            Guid.NewGuid().ToString("N"));
        await api.PostOkAsync($"/api/v1/sales-orders/{order.Id}/release", new { }, Guid.NewGuid().ToString("N"));

        var deliveryNumber = "DEL-" + orderNumber;
        var invoiceNumber = "INV-" + orderNumber;
        Assert.Equal(0, await fixture.Probe.CountDeliveriesAsync(fixture.TenantA, deliveryNumber));

        await OutboxProcessor.ProcessOnceAsync(fixture.AppConnectionString, fixture.TenantA);

        var messageId = await fixture.Probe.OutboxIdForOrderAsync(fixture.TenantA, order.Id);
        Assert.Equal(1, await fixture.Probe.CountDeliveriesAsync(fixture.TenantA, deliveryNumber));
        Assert.Equal(1, await fixture.Probe.CountInvoicesAsync(fixture.TenantA, invoiceNumber));
        Assert.Equal(1, await fixture.Probe.CountInboxAsync(messageId));

        Assert.Equal(1, await fixture.Probe.ClearProcessedAtAsync(fixture.TenantA, messageId));
        await OutboxProcessor.ProcessOnceAsync(fixture.AppConnectionString, fixture.TenantA);

        Assert.Equal(1, await fixture.Probe.CountDeliveriesAsync(fixture.TenantA, deliveryNumber));
        Assert.Equal(1, await fixture.Probe.CountInvoicesAsync(fixture.TenantA, invoiceNumber));
        Assert.Equal(1, await fixture.Probe.CountInboxAsync(messageId));
    }
}
