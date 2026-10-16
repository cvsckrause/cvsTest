using System.Net;
using System.Text.Json;

namespace Erp.Integration.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SalesOrderCancellationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Release_then_cancel_restores_reserved_stock()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var graph = await CreateGraphAsync(api, "CXL", "NEBEN");
        await ReceiveAsync(api, graph.ArticleId, "MAIN", 4m);
        var orderNumber = OrderNumber("CXL");
        var created = await api.PostOkAsync(
            "/api/v1/sales-orders",
            OrderBody(graph, orderNumber, 3m, "NEBEN"),
            Key());

        var released = await api.PostOkAsync($"/api/v1/sales-orders/{created.Id}/release", new { }, Key());
        var reserved = await StockAsync(api, graph.ArticleId, "NEBEN");
        var mainWhileReserved = await StockAsync(api, graph.ArticleId, "MAIN");

        var cancelled = await api.PostOkAsync($"/api/v1/sales-orders/{created.Id}/cancel", new { }, Key());
        var after = await StockAsync(api, graph.ArticleId, "NEBEN");
        var mainAfter = await StockAsync(api, graph.ArticleId, "MAIN");

        Assert.Equal("Released", Text(released, "status"));
        Assert.Equal(10m, reserved.OnHand);
        Assert.Equal(3m, reserved.Reserved);
        Assert.Equal(4m, mainWhileReserved.OnHand);
        Assert.Equal(0m, mainWhileReserved.Reserved);
        Assert.Equal(HttpStatusCode.OK, cancelled.Status);
        Assert.Equal("Cancelled", Text(cancelled, "status"));
        Assert.Equal("NEBEN", Text(cancelled, "warehouseCode"));
        Assert.Equal(10m, after.OnHand);
        Assert.Equal(0m, after.Reserved);
        Assert.Equal(4m, mainAfter.OnHand);
        Assert.Equal(0m, mainAfter.Reserved);
        Assert.Equal(1, await fixture.Probe.CountOutboxForOrderEventAsync(
            fixture.TenantA, created.Id, "erp.sales-order.cancelled.v1"));
    }

    [Fact]
    public async Task Repeating_cancel_does_not_unreserve_twice()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var graph = await CreateGraphAsync(api, "REP", "NEBEN");
        var created = await api.PostOkAsync(
            "/api/v1/sales-orders",
            OrderBody(graph, OrderNumber("REP"), 3m, "NEBEN"),
            Key());
        await api.PostOkAsync($"/api/v1/sales-orders/{created.Id}/release", new { }, Key());
        var key = Key();

        var first = await api.PostAsync($"/api/v1/sales-orders/{created.Id}/cancel", new { }, key);
        var second = await api.PostAsync($"/api/v1/sales-orders/{created.Id}/cancel", new { }, key);
        var stock = await StockAsync(api, graph.ArticleId, "NEBEN");

        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.False(first.Replayed);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.True(second.Replayed);
        Assert.Equal(10m, stock.OnHand);
        Assert.Equal(0m, stock.Reserved);
        Assert.Equal(1, await fixture.Probe.CountOutboxForOrderEventAsync(
            fixture.TenantA, created.Id, "erp.sales-order.cancelled.v1"));
    }

    [Fact]
    public async Task A_second_cancel_is_rejected_and_reserved_stays_zero()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var graph = await CreateGraphAsync(api, "SEC", "NEBEN");
        var created = await api.PostOkAsync(
            "/api/v1/sales-orders",
            OrderBody(graph, OrderNumber("SEC"), 3m, "NEBEN"),
            Key());
        await api.PostOkAsync($"/api/v1/sales-orders/{created.Id}/release", new { }, Key());
        await api.PostOkAsync($"/api/v1/sales-orders/{created.Id}/cancel", new { }, Key());

        var again = await api.PostAsync($"/api/v1/sales-orders/{created.Id}/cancel", new { }, Key());
        var stock = await StockAsync(api, graph.ArticleId, "NEBEN");

        Assert.Equal(HttpStatusCode.Conflict, again.Status);
        Assert.Contains("Cancelled orders cannot be changed.", again.Body, StringComparison.Ordinal);
        Assert.Equal(0m, stock.Reserved);
    }

    [Fact]
    public async Task Cancel_after_delivery_is_rejected()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var graph = await CreateGraphAsync(api, "DLV", "MAIN");
        var orderNumber = OrderNumber("DLV");
        var created = await api.PostOkAsync(
            "/api/v1/sales-orders",
            OrderBody(graph, orderNumber, 3m, "MAIN"),
            Key());
        await api.PostOkAsync($"/api/v1/sales-orders/{created.Id}/release", new { }, Key());
        var releasedId = await fixture.Probe.OutboxIdForOrderEventAsync(
            fixture.TenantA, created.Id, "erp.sales-order.released.v1");
        await ProcessUntilAsync(fixture, releasedId);

        var cancel = await api.PostAsync($"/api/v1/sales-orders/{created.Id}/cancel", new { }, Key());
        var stock = await StockAsync(api, graph.ArticleId, "MAIN");

        Assert.Equal(1, await fixture.Probe.CountDeliveriesAsync(fixture.TenantA, $"DEL-{orderNumber}"));
        Assert.Equal(HttpStatusCode.Conflict, cancel.Status);
        Assert.Contains("Delivered orders cannot be cancelled.", cancel.Body, StringComparison.Ordinal);
        Assert.Equal(7m, stock.OnHand);
        Assert.Equal(0m, stock.Reserved);
    }

    [Fact]
    public async Task Worker_issues_reserved_stock_when_it_creates_the_delivery()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var graph = await CreateGraphAsync(api, "ISS", "MAIN");
        var orderNumber = OrderNumber("ISS");
        var created = await api.PostOkAsync(
            "/api/v1/sales-orders",
            OrderBody(graph, orderNumber, 3m, "MAIN"),
            Key());
        await api.PostOkAsync($"/api/v1/sales-orders/{created.Id}/release", new { }, Key());
        var releasedId = await fixture.Probe.OutboxIdForOrderEventAsync(
            fixture.TenantA, created.Id, "erp.sales-order.released.v1");
        await ProcessUntilAsync(fixture, releasedId);

        var stock = await StockAsync(api, graph.ArticleId, "MAIN");
        var deliveryNumber = $"DEL-{orderNumber}";

        Assert.Equal(7m, stock.OnHand);
        Assert.Equal(0m, stock.Reserved);
        Assert.Equal(1, await fixture.Probe.CountDeliveriesAsync(fixture.TenantA, deliveryNumber));
        Assert.Equal(1, await fixture.Probe.CountMovementsAsync(fixture.TenantA, deliveryNumber));
        Assert.Equal(1, CountIssueMovements(await api.GetAsync("/api/v1/stock/movements?limit=50"), deliveryNumber));
    }

    [Fact]
    public async Task Worker_issues_each_line_of_a_delivery()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var graph = await CreateGraphAsync(api, "ISL", "MAIN");
        var orderNumber = OrderNumber("ISL");
        var created = await api.PostOkAsync(
            "/api/v1/sales-orders",
            new
            {
                customerId = graph.CustomerId,
                shipToAddressId = graph.AddressId,
                orderNumber,
                warehouseCode = "MAIN",
                lines = new[]
                {
                    new { articleId = graph.ArticleId, quantity = 2m },
                    new { articleId = graph.ArticleId, quantity = 1m }
                }
            },
            Key());
        await api.PostOkAsync($"/api/v1/sales-orders/{created.Id}/release", new { }, Key());
        var releasedId = await fixture.Probe.OutboxIdForOrderEventAsync(
            fixture.TenantA, created.Id, "erp.sales-order.released.v1");
        await ProcessUntilAsync(fixture, releasedId);

        var stock = await StockAsync(api, graph.ArticleId, "MAIN");
        var deliveryNumber = $"DEL-{orderNumber}";

        Assert.Equal(7m, stock.OnHand);
        Assert.Equal(0m, stock.Reserved);
        Assert.Equal(2, await fixture.Probe.CountMovementsAsync(fixture.TenantA, deliveryNumber));
        Assert.Equal(2, CountIssueMovements(await api.GetAsync("/api/v1/stock/movements?limit=50"), deliveryNumber));
    }

    [Fact]
    public async Task Worker_does_not_deliver_a_cancelled_order()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var graph = await CreateGraphAsync(api, "WRK", "MAIN");
        var orderNumber = OrderNumber("WRK");
        var created = await api.PostOkAsync(
            "/api/v1/sales-orders",
            OrderBody(graph, orderNumber, 2m, "MAIN"),
            Key());
        await api.PostOkAsync($"/api/v1/sales-orders/{created.Id}/release", new { }, Key());
        await api.PostOkAsync($"/api/v1/sales-orders/{created.Id}/cancel", new { }, Key());
        var releasedId = await fixture.Probe.OutboxIdForOrderEventAsync(
            fixture.TenantA, created.Id, "erp.sales-order.released.v1");
        await ProcessUntilAsync(fixture, releasedId);

        Assert.Equal(0, await fixture.Probe.CountDeliveriesAsync(fixture.TenantA, $"DEL-{orderNumber}"));
        Assert.Equal(0, await fixture.Probe.CountInvoicesAsync(fixture.TenantA, $"INV-{orderNumber}"));
        Assert.True(await fixture.Probe.OutboxProcessedAsync(fixture.TenantA, releasedId));
        Assert.Equal(1, await fixture.Probe.CountInboxAsync(releasedId));
        var stock = await StockAsync(api, graph.ArticleId, "MAIN");
        Assert.Equal(10m, stock.OnHand);
        Assert.Equal(0m, stock.Reserved);
        Assert.Equal(0, await fixture.Probe.CountMovementsAsync(fixture.TenantA, $"DEL-{orderNumber}"));
    }

    [Fact]
    public async Task Cancelling_a_draft_blocks_a_later_release()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var graph = await CreateGraphAsync(api, "DFT", "MAIN");
        var created = await api.PostOkAsync(
            "/api/v1/sales-orders",
            OrderBody(graph, OrderNumber("DFT"), 3m, "MAIN"),
            Key());

        var cancelled = await api.PostOkAsync($"/api/v1/sales-orders/{created.Id}/cancel", new { }, Key());
        var release = await api.PostAsync($"/api/v1/sales-orders/{created.Id}/release", new { }, Key());
        var stock = await StockAsync(api, graph.ArticleId, "MAIN");

        Assert.Equal("Cancelled", Text(cancelled, "status"));
        Assert.Equal(HttpStatusCode.Conflict, release.Status);
        Assert.Equal(10m, stock.OnHand);
        Assert.Equal(0m, stock.Reserved);
    }

    [Fact]
    public async Task Create_stores_a_trimmed_upper_warehouse_code()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var graph = await CreateGraphAsync(api, "WHS", "MAIN");
        var created = await api.PostOkAsync(
            "/api/v1/sales-orders",
            OrderBody(graph, OrderNumber("WHS"), 1m, " neben "),
            Key());

        var loaded = await api.GetAsync($"/api/v1/sales-orders/{created.Id}");

        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.Equal("NEBEN", Text(created, "warehouseCode"));
        Assert.Equal(HttpStatusCode.OK, loaded.Status);
        Assert.Equal("NEBEN", Text(loaded, "warehouseCode"));
    }

    [Fact]
    public async Task Cancel_without_an_idempotency_key_is_rejected()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var graph = await CreateGraphAsync(api, "KEY", "MAIN");
        var created = await api.PostOkAsync(
            "/api/v1/sales-orders",
            OrderBody(graph, OrderNumber("KEY"), 1m, "MAIN"),
            Key());

        var cancel = await api.PostAsync($"/api/v1/sales-orders/{created.Id}/cancel", new { });

        Assert.Equal(HttpStatusCode.BadRequest, cancel.Status);
        Assert.Contains("Idempotency-Key", cancel.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancel_of_an_unknown_order_is_not_found()
    {
        using var api = fixture.OpenSession(fixture.TenantA);

        var cancel = await api.PostAsync($"/api/v1/sales-orders/{Guid.NewGuid()}/cancel", new { }, Key());

        Assert.Equal(HttpStatusCode.NotFound, cancel.Status);
    }

    private static async Task<TenantGraph> CreateGraphAsync(ApiSession api, string label, string warehouseCode)
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
            warehouseCode
        });
        return new TenantGraph(customer.Id, address.Id, article.Id, Guid.Empty, Guid.Empty);
    }

    private static Task<ApiResult> ReceiveAsync(ApiSession api, Guid articleId, string warehouseCode, decimal quantity) =>
        api.PostOkAsync("/api/v1/stock/receipts", new
        {
            articleId,
            quantity,
            warehouseCode,
            reference = "SEED-" + Guid.NewGuid().ToString("N")[..8]
        }, Key());

    private static object OrderBody(TenantGraph graph, string orderNumber, decimal quantity, string warehouseCode) =>
        new
        {
            customerId = graph.CustomerId,
            shipToAddressId = graph.AddressId,
            orderNumber,
            warehouseCode,
            lines = new[] { new { articleId = graph.ArticleId, quantity } }
        };

    private static string OrderNumber(string label) =>
        label + "-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static string Key() => Guid.NewGuid().ToString("N");

    private static async Task ProcessUntilAsync(PostgresFixture fixture, Guid messageId)
    {
        for (var attempt = 0; attempt < 8 && !await fixture.Probe.OutboxProcessedAsync(fixture.TenantA, messageId); attempt++)
        {
            await OutboxProcessor.ProcessOnceAsync(fixture.WorkerConnectionString);
        }
    }

    private static int CountIssueMovements(ApiResult result, string reference)
    {
        using var document = JsonDocument.Parse(result.Body);
        var count = 0;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.GetProperty("movementType").GetString() == "GoodsIssue"
                && item.GetProperty("reference").GetString() == reference)
            {
                count++;
            }
        }

        return count;
    }

    private static string Text(ApiResult result, string name)
    {
        using var document = JsonDocument.Parse(result.Body);
        return document.RootElement.GetProperty(name).GetString()
            ?? throw new InvalidOperationException($"Property {name} was null.");
    }

    private static async Task<(decimal OnHand, decimal Reserved)> StockAsync(ApiSession api, Guid articleId, string warehouseCode)
    {
        var stock = await api.GetAsync("/api/v1/stock");
        using var document = JsonDocument.Parse(stock.Body);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.GetProperty("articleId").GetGuid() == articleId
                && item.GetProperty("warehouseCode").GetString() == warehouseCode)
            {
                return (item.GetProperty("onHand").GetDecimal(), item.GetProperty("reserved").GetDecimal());
            }
        }

        throw new InvalidOperationException($"No {warehouseCode} balance for {articleId}.");
    }
}
