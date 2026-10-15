using System.Net;

namespace Erp.Integration.Tests;

[Collection(PostgresCollection.Name)]
public sealed class IdempotencyTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Repeating_create_order_keeps_one_order()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var graph = await CreateGraphAsync(api, "ORD");
        var orderNumber = "ORD-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var key = Guid.NewGuid().ToString("N");
        var body = OrderBody(graph, orderNumber);

        var first = await api.PostAsync("/api/v1/sales-orders", body, key);
        var second = await api.PostAsync("/api/v1/sales-orders", body, key);

        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.False(first.Replayed);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.True(second.Replayed);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await fixture.Probe.CountOrdersAsync(fixture.TenantA, orderNumber));
    }

    [Fact]
    public async Task Repeating_release_keeps_one_outbox_message()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var graph = await CreateGraphAsync(api, "REL");
        var orderNumber = "REL-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var created = await api.PostOkAsync(
            "/api/v1/sales-orders",
            OrderBody(graph, orderNumber),
            Guid.NewGuid().ToString("N"));
        var key = Guid.NewGuid().ToString("N");

        var first = await api.PostAsync($"/api/v1/sales-orders/{created.Id}/release", new { }, key);
        var second = await api.PostAsync($"/api/v1/sales-orders/{created.Id}/release", new { }, key);

        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.False(first.Replayed);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.True(second.Replayed);
        Assert.Equal(created.Id, second.Id);
        Assert.Equal(1, await fixture.Probe.CountOutboxForOrderAsync(fixture.TenantA, created.Id));
    }

    [Fact]
    public async Task Repeating_goods_receipt_keeps_one_movement()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var graph = await CreateGraphAsync(api, "RCP");
        var reference = "RCP-" + Guid.NewGuid().ToString("N")[..8];
        var key = Guid.NewGuid().ToString("N");
        var body = new { articleId = graph.ArticleId, quantity = 3m, warehouseCode = "MAIN", reference };

        var first = await api.PostAsync("/api/v1/stock/receipts", body, key);
        var second = await api.PostAsync("/api/v1/stock/receipts", body, key);
        var stock = await api.GetAsync("/api/v1/stock");

        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.False(first.Replayed);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.True(second.Replayed);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await fixture.Probe.CountMovementsAsync(fixture.TenantA, reference));
        Assert.Equal(13m, stock.OnHand(graph.ArticleId));
    }

    private static async Task<TenantGraph> CreateGraphAsync(ApiSession api, string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var customer = await api.PostOkAsync("/api/v1/customers", new { number = $"{label}-{suffix}", name = $"{label} Customer" });
        var address = await api.PostOkAsync("/api/v1/addresses", new
        {
            name = $"{label} {suffix}",
            street = "2 Test Street",
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
            openingStock = 10m,
            warehouseCode = "MAIN"
        });
        return new TenantGraph(customer.Id, address.Id, article.Id, Guid.Empty, Guid.Empty);
    }

    private static object OrderBody(TenantGraph graph, string orderNumber) =>
        new
        {
            customerId = graph.CustomerId,
            shipToAddressId = graph.AddressId,
            orderNumber,
            lines = new[] { new { articleId = graph.ArticleId, quantity = 1m } }
        };
}
