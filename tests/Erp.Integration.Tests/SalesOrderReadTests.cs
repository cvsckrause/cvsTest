using System.Net;
using System.Text.Json;

namespace Erp.Integration.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SalesOrderReadTests(PostgresFixture fixture)
{
    [Fact]
    public async Task List_filters_by_status_and_hides_other_tenant()
    {
        using var tenantA = fixture.OpenSession(fixture.TenantA);
        using var tenantB = fixture.OpenSession(fixture.TenantB);

        var draft = await CreateDraftOrderAsync(tenantA, "LIST-DRAFT");

        var allA = await tenantA.GetAsync("/api/v1/sales-orders");
        Assert.Equal(HttpStatusCode.OK, allA.Status);
        Assert.True(allA.Contains(draft.Id));
        Assert.True(allA.Contains(fixture.SeedA.SalesOrderId));
        Assert.False(allA.Contains(fixture.SeedB.SalesOrderId));

        var drafts = await tenantA.GetAsync("/api/v1/sales-orders?status=Draft");
        Assert.Equal(HttpStatusCode.OK, drafts.Status);
        Assert.True(drafts.Contains(draft.Id));
        Assert.False(drafts.Contains(fixture.SeedA.SalesOrderId));

        var released = await tenantA.GetAsync("/api/v1/sales-orders?status=Released");
        Assert.Equal(HttpStatusCode.OK, released.Status);
        Assert.True(released.Contains(fixture.SeedA.SalesOrderId));
        Assert.False(released.Contains(draft.Id));

        var forB = await tenantB.GetAsync("/api/v1/sales-orders");
        Assert.Equal(HttpStatusCode.OK, forB.Status);
        Assert.False(forB.Contains(draft.Id));
        Assert.False(forB.Contains(fixture.SeedA.SalesOrderId));
        Assert.True(forB.Contains(fixture.SeedB.SalesOrderId));
    }

    [Fact]
    public async Task Invalid_status_is_bad_request()
    {
        using var api = fixture.OpenSession(fixture.TenantA);

        var result = await api.GetAsync("/api/v1/sales-orders?status=Nope");

        Assert.Equal(HttpStatusCode.BadRequest, result.Status);
    }

    [Fact]
    public async Task Deliveries_and_invoices_appear_after_worker_and_stay_tenant_scoped()
    {
        var order = await ReleaseFreshOrderAsync(fixture.TenantA, "READ-DOCS");
        await OutboxProcessor.ProcessOnceAsync(fixture.WorkerConnectionString);

        using var owner = fixture.OpenSession(fixture.TenantA);
        using var other = fixture.OpenSession(fixture.TenantB);

        var deliveries = await owner.GetAsync($"/api/v1/sales-orders/{order.Id}/deliveries");
        Assert.Equal(HttpStatusCode.OK, deliveries.Status);
        using var deliveryDoc = JsonDocument.Parse(deliveries.Body);
        Assert.Matches(@"^LS-\d+$", deliveryDoc.RootElement[0].GetProperty("number").GetString());

        var invoices = await owner.GetAsync($"/api/v1/sales-orders/{order.Id}/invoices");
        Assert.Equal(HttpStatusCode.OK, invoices.Status);
        using var invoiceDoc = JsonDocument.Parse(invoices.Body);
        Assert.Matches(@"^\d+$", invoiceDoc.RootElement[0].GetProperty("number").GetString());
        var invoiceId = invoiceDoc.RootElement[0].GetProperty("id").GetGuid();

        var invoice = await owner.GetAsync($"/api/v1/invoices/{invoiceId}");
        Assert.Equal(HttpStatusCode.OK, invoice.Status);
        Assert.True(invoice.Contains(order.Id));

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/sales-orders/{order.Id}/deliveries")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/sales-orders/{order.Id}/invoices")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/invoices/{invoiceId}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/sales-orders/{fixture.SeedA.SalesOrderId}/deliveries")).Status);
    }

    [Fact]
    public async Task Nested_reads_are_empty_before_worker_runs()
    {
        var order = await ReleaseFreshOrderAsync(fixture.TenantA, "EMPTY-DOCS");
        using var api = fixture.OpenSession(fixture.TenantA);

        var deliveries = await api.GetAsync($"/api/v1/sales-orders/{order.Id}/deliveries");
        var invoices = await api.GetAsync($"/api/v1/sales-orders/{order.Id}/invoices");

        Assert.Equal(HttpStatusCode.OK, deliveries.Status);
        Assert.Equal("[]", deliveries.Body.Trim());
        Assert.Equal(HttpStatusCode.OK, invoices.Status);
        Assert.Equal("[]", invoices.Body.Trim());
    }

    private async Task<(Guid Id, string Number)> CreateDraftOrderAsync(ApiSession api, string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var customer = await api.PostOkAsync("/api/v1/customers", new { number = $"{label}-{suffix}", name = $"{label} Customer" });
        var address = await api.PostOkAsync("/api/v1/addresses", new
        {
            name = $"{label} {suffix}",
            street = "2 Read Street",
            postalCode = "10115",
            city = "Berlin",
            countryCode = "DE"
        });
        await api.PostOkAsync($"/api/v1/customers/{customer.Id}/addresses", new { addressId = address.Id, role = "Shipping" });
        var article = await api.PostOkAsync("/api/v1/articles", new
        {
            sku = $"{label}-{suffix}",
            name = $"{label} Article",
            salesPrice = 12m,
            openingStock = 5m,
            warehouseCode = "MAIN"
        });
        var number = $"{label}-{suffix}";
        var order = await api.PostOkAsync(
            "/api/v1/sales-orders",
            new
            {
                customerId = customer.Id,
                shipToAddressId = address.Id,
                orderNumber = number,
                lines = new[] { new { articleId = article.Id, quantity = 1m } }
            },
            $"{label}-{suffix}-create");
        return (order.Id, number.ToUpperInvariant());
    }

    private async Task<(Guid Id, string Number)> ReleaseFreshOrderAsync(Guid tenantId, string label)
    {
        using var api = fixture.OpenSession(tenantId);
        var draft = await CreateDraftOrderAsync(api, label);
        await api.PostOkAsync($"/api/v1/sales-orders/{draft.Id}/release", new { }, $"{label}-release-{draft.Id:N}");
        return draft;
    }
}
