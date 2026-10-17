using System.Net;
using System.Text.Json;
using Npgsql;

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
        Assert.Matches(@"^R-\d+$", invoiceDoc.RootElement[0].GetProperty("number").GetString());
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

    [Fact]
    public async Task List_and_detail_agree_on_the_rounded_net_and_lines_keep_their_snapshot()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var bolt = await api.PostOkAsync("/api/v1/articles", new { sku = $"BOLT-{suffix}", name = "Schraube", salesPrice = 0.3335m, openingStock = 10m });
        var nut = await api.PostOkAsync("/api/v1/articles", new { sku = $"NUT-{suffix}", name = "Mutter", salesPrice = 0.125m, openingStock = 10m, taxCode = "Reduced7" });
        var order = await api.PostOkAsync(
            "/api/v1/sales-orders",
            new
            {
                customerId = fixture.SeedA.CustomerId,
                shipToAddressId = fixture.SeedA.AddressId,
                orderNumber = $"ROUND-{suffix}",
                lines = new[] { new { articleId = bolt.Id, quantity = 3m }, new { articleId = nut.Id, quantity = 1m } }
            },
            $"round-{suffix}");
        await api.PutAsync($"/api/v1/articles/{bolt.Id}", new { name = "Schraube neu", salesPrice = 1m });

        var detail = await api.GetAsync($"/api/v1/sales-orders/{order.Id}");
        using var detailDoc = JsonDocument.Parse(detail.Body);
        Assert.Equal(1.13m, detailDoc.RootElement.GetProperty("netAmount").GetDecimal());
        var lines = detailDoc.RootElement.GetProperty("lines");
        Assert.Equal("Schraube", lines[0].GetProperty("description").GetString());
        Assert.Equal("Standard19", lines[0].GetProperty("taxCode").GetString());
        Assert.Equal(1.00m, lines[0].GetProperty("netAmount").GetDecimal());
        Assert.Equal("Mutter", lines[1].GetProperty("description").GetString());
        Assert.Equal("Reduced7", lines[1].GetProperty("taxCode").GetString());
        Assert.Equal(0.13m, lines[1].GetProperty("netAmount").GetDecimal());

        var list = await api.GetAsync("/api/v1/sales-orders?status=Draft&limit=200");
        using var listDoc = JsonDocument.Parse(list.Body);
        var summary = listDoc.RootElement.EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == order.Id);
        Assert.Equal(1.13m, summary.GetProperty("netAmount").GetDecimal());
        Assert.False(summary.TryGetProperty("grossAmount", out _));
        Assert.False(summary.TryGetProperty("taxTotals", out _));
    }

    [Fact]
    public async Task Saved_order_and_its_invoice_share_the_mixed_rate_totals()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var bike = await api.PostOkAsync("/api/v1/articles", new
        {
            sku = $"BIKE-{suffix}",
            name = "Cityrad",
            salesPrice = 799.00m,
            openingStock = 5m
        });
        var lockItem = await api.PostOkAsync("/api/v1/articles", new
        {
            sku = $"LOCK-{suffix}",
            name = "Schloss",
            salesPrice = 12.99m,
            openingStock = 5m,
            taxCode = "Reduced7"
        });
        var order = await api.PostOkAsync(
            "/api/v1/sales-orders",
            new
            {
                customerId = fixture.SeedA.CustomerId,
                shipToAddressId = fixture.SeedA.AddressId,
                orderNumber = $"AE3-{suffix}",
                lines = new[]
                {
                    new { articleId = bike.Id, quantity = 2m },
                    new { articleId = lockItem.Id, quantity = 3m }
                }
            },
            $"ae3-{suffix}");

        var detail = await api.GetAsync($"/api/v1/sales-orders/{order.Id}");
        using var detailDoc = JsonDocument.Parse(detail.Body);
        AssertTaxTotals(detailDoc.RootElement, 1636.97m, 306.35m, 1943.32m);

        await api.PostOkAsync($"/api/v1/sales-orders/{order.Id}/release", new { }, $"ae3-release-{suffix}");
        var messageId = await fixture.Probe.OutboxIdForOrderEventAsync(fixture.TenantA, order.Id, "erp.sales-order.released.v1");
        await ProcessUntilAsync(messageId);

        var invoices = await api.GetAsync($"/api/v1/sales-orders/{order.Id}/invoices");
        using var invoiceList = JsonDocument.Parse(invoices.Body);
        var listed = Assert.Single(invoiceList.RootElement.EnumerateArray());
        AssertInvoiceContent(listed);
        var invoice = await api.GetAsync($"/api/v1/invoices/{listed.GetProperty("id").GetGuid()}");
        using var invoiceDoc = JsonDocument.Parse(invoice.Body);
        AssertInvoiceContent(invoiceDoc.RootElement);

        var released = await api.GetAsync($"/api/v1/sales-orders/{order.Id}");
        using var releasedDoc = JsonDocument.Parse(released.Body);
        Assert.Equal(
            TaxRows(listed.GetProperty("taxTotals")),
            TaxRows(releasedDoc.RootElement.GetProperty("taxTotals")));
    }

    [Fact]
    public async Task Zero_rated_order_has_one_zero_tax_total()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var article = await api.PostOkAsync("/api/v1/articles", new
        {
            sku = $"FREE-{suffix}",
            name = "Spende",
            salesPrice = 20m,
            openingStock = 1m,
            taxCode = "Zero"
        });
        var order = await api.PostOkAsync(
            "/api/v1/sales-orders",
            new
            {
                customerId = fixture.SeedA.CustomerId,
                shipToAddressId = fixture.SeedA.AddressId,
                orderNumber = $"ZERO-{suffix}",
                lines = new[] { new { articleId = article.Id, quantity = 1m } }
            },
            $"zero-{suffix}");

        var detail = await api.GetAsync($"/api/v1/sales-orders/{order.Id}");
        using var detailDoc = JsonDocument.Parse(detail.Body);
        Assert.Equal(0.00m, detailDoc.RootElement.GetProperty("taxAmount").GetDecimal());
        Assert.Equal(20.00m, detailDoc.RootElement.GetProperty("grossAmount").GetDecimal());
        var total = Assert.Single(detailDoc.RootElement.GetProperty("taxTotals").EnumerateArray());
        Assert.Equal(0m, total.GetProperty("rate").GetDecimal());
        Assert.Equal("Z", total.GetProperty("category").GetString());
        Assert.Equal(0.00m, total.GetProperty("taxAmount").GetDecimal());
    }

    [Fact]
    public async Task Legacy_invoice_returns_its_number_and_net_without_content()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var draft = await CreateDraftOrderAsync(api, "LEGACY");
        var invoiceId = Guid.NewGuid();
        var number = "R-LEGACY-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await using (var connection = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                insert into erp.invoices ("Id", "TenantId", "SalesOrderId", "Number", "NetAmount", "CreatedAt")
                values (@id, @tenant, @order, @number, 12.50, now())
                """,
                connection);
            command.Parameters.AddWithValue("id", invoiceId);
            command.Parameters.AddWithValue("tenant", fixture.TenantA);
            command.Parameters.AddWithValue("order", draft.Id);
            command.Parameters.AddWithValue("number", number);
            await command.ExecuteNonQueryAsync();
        }

        try
        {
            var invoice = await api.GetAsync($"/api/v1/invoices/{invoiceId}");
            using var invoiceDoc = JsonDocument.Parse(invoice.Body);
            AssertLegacy(invoiceDoc.RootElement, number);

            var listed = await api.GetAsync($"/api/v1/sales-orders/{draft.Id}/invoices");
            using var listDoc = JsonDocument.Parse(listed.Body);
            AssertLegacy(Assert.Single(listDoc.RootElement.EnumerateArray()), number);
        }
        finally
        {
            await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""delete from erp.invoices where "Id" = @id""", connection);
            command.Parameters.AddWithValue("id", invoiceId);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static (decimal Rate, string? Category, decimal Net, decimal Tax)[] TaxRows(JsonElement totals) =>
        totals.EnumerateArray()
            .Select(total => (
                total.GetProperty("rate").GetDecimal(),
                total.GetProperty("category").GetString(),
                total.GetProperty("netAmount").GetDecimal(),
                total.GetProperty("taxAmount").GetDecimal()))
            .ToArray();

    private static void AssertTaxTotals(JsonElement body, decimal net, decimal tax, decimal gross)
    {
        Assert.Equal(net, body.GetProperty("netAmount").GetDecimal());
        Assert.Equal(tax, body.GetProperty("taxAmount").GetDecimal());
        Assert.Equal(gross, body.GetProperty("grossAmount").GetDecimal());
        var totals = body.GetProperty("taxTotals").EnumerateArray().ToArray();
        Assert.Equal(2, totals.Length);
        Assert.Equal(19m, totals[0].GetProperty("rate").GetDecimal());
        Assert.Equal("S", totals[0].GetProperty("category").GetString());
        Assert.Equal(1598.00m, totals[0].GetProperty("netAmount").GetDecimal());
        Assert.Equal(303.62m, totals[0].GetProperty("taxAmount").GetDecimal());
        Assert.Equal(7m, totals[1].GetProperty("rate").GetDecimal());
        Assert.Equal("S", totals[1].GetProperty("category").GetString());
        Assert.Equal(38.97m, totals[1].GetProperty("netAmount").GetDecimal());
        Assert.Equal(2.73m, totals[1].GetProperty("taxAmount").GetDecimal());
    }

    private static void AssertInvoiceContent(JsonElement invoice)
    {
        AssertTaxTotals(invoice, 1636.97m, 306.35m, 1943.32m);
        Assert.True(invoice.GetProperty("contentComplete").GetBoolean());
        Assert.Equal("EUR", invoice.GetProperty("currency").GetString());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", invoice.GetProperty("invoiceDate").GetString());
        Assert.Equal(invoice.GetProperty("invoiceDate").GetString(), invoice.GetProperty("deliveryDate").GetString());
        var lines = invoice.GetProperty("lines").EnumerateArray().ToArray();
        Assert.Equal(2, lines.Length);
        Assert.Equal(1, lines[0].GetProperty("position").GetInt32());
        Assert.Equal("Cityrad", lines[0].GetProperty("description").GetString());
        Assert.Equal(19m, lines[0].GetProperty("rate").GetDecimal());
        Assert.Equal(2, lines[1].GetProperty("position").GetInt32());
        Assert.Equal("Schloss", lines[1].GetProperty("description").GetString());
        Assert.Equal(7m, lines[1].GetProperty("rate").GetDecimal());
        Assert.Equal("A Handels GmbH", invoice.GetProperty("seller").GetProperty("name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(invoice.GetProperty("buyer").GetProperty("name").GetString()));
    }

    private static void AssertLegacy(JsonElement invoice, string number)
    {
        Assert.Equal(number, invoice.GetProperty("number").GetString());
        Assert.Equal(12.50m, invoice.GetProperty("netAmount").GetDecimal());
        Assert.False(invoice.GetProperty("contentComplete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, invoice.GetProperty("taxAmount").ValueKind);
        Assert.Equal(JsonValueKind.Null, invoice.GetProperty("grossAmount").ValueKind);
        Assert.Equal(JsonValueKind.Null, invoice.GetProperty("seller").ValueKind);
        Assert.Equal(JsonValueKind.Null, invoice.GetProperty("buyer").ValueKind);
        Assert.Empty(invoice.GetProperty("lines").EnumerateArray());
        Assert.Empty(invoice.GetProperty("taxTotals").EnumerateArray());
    }

    private async Task ProcessUntilAsync(Guid messageId)
    {
        for (var attempt = 0; attempt < 8 && !await fixture.Probe.OutboxProcessedAsync(fixture.TenantA, messageId); attempt++)
        {
            await OutboxProcessor.ProcessOnceAsync(fixture.WorkerConnectionString);
        }
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
