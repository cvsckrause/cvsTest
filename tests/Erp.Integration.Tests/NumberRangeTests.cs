using Erp.BuildingBlocks;
using Erp.Persistence;
using Erp.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Integration.Tests;

[Collection(PostgresCollection.Name)]
public sealed class NumberRangeTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Blank_order_number_is_assigned_and_a_manual_number_is_kept()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var graph = await CreateGraphAsync(api, "NUM");

        var first = await api.PostOkAsync("/api/v1/sales-orders", OrderBody(graph, null), Key());
        var second = await api.PostOkAsync("/api/v1/sales-orders", OrderBody(graph, null), Key());
        var manual = await api.PostOkAsync("/api/v1/sales-orders", OrderBody(graph, "manual-77"), Key());

        var firstNumber = Text(first, "orderNumber");
        var secondNumber = Text(second, "orderNumber");
        Assert.Matches(@"^AU-\d+$", firstNumber);
        Assert.Matches(@"^AU-\d+$", secondNumber);
        Assert.Equal(long.Parse(firstNumber[3..]) + 1, long.Parse(secondNumber[3..]));
        Assert.Equal("MANUAL-77", Text(manual, "orderNumber"));
    }

    [Fact]
    public async Task First_invoices_of_a_new_tenant_are_r_1_and_r_2()
    {
        var tenantId = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        var first = await AllocateAsync(tenantId, NumberDocumentType.Invoice, at, commit: true);
        var second = await AllocateAsync(tenantId, NumberDocumentType.Invoice, at, commit: true);

        Assert.Equal("R-1", first);
        Assert.Equal("R-2", second);
    }

    [Fact]
    public async Task Rolled_back_allocation_does_not_consume_a_number()
    {
        var at = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        var first = await AllocateAsync(fixture.TenantB, NumberDocumentType.Invoice, at, commit: false);
        var second = await AllocateAsync(fixture.TenantB, NumberDocumentType.Invoice, at, commit: false);

        Assert.Equal(first, second);
        Assert.Matches(@"^R-\d+$", first);
    }

    [Fact]
    public async Task Yearly_reset_starts_again_at_one()
    {
        var tenant = new TenantContext(fixture.TenantB, "ranges");
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql(fixture.AppConnectionString)
            .AddInterceptors(new TenantConnectionInterceptor(tenant))
            .Options;
        await using var db = new ErpDbContext(options, tenant);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
            update erp.number_ranges
            set "ResetYearly" = true, "Year" = 2026
            where "TenantId" = {fixture.TenantB} and "DocumentType" = {"Invoice"} and "Year" = 0
            """);
        Assert.Equal(1, updated);

        var june = await NumberRangeAllocator.AllocateAsync(
            db, fixture.TenantB, NumberDocumentType.Invoice, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);
        var july = await NumberRangeAllocator.AllocateAsync(
            db, fixture.TenantB, NumberDocumentType.Invoice, new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);
        var nextYear = await NumberRangeAllocator.AllocateAsync(
            db, fixture.TenantB, NumberDocumentType.Invoice, new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);
        await transaction.RollbackAsync();

        Assert.Matches(@"^R-\d+$", june);
        Assert.Equal(long.Parse(june[2..]) + 1, long.Parse(july[2..]));
        Assert.Equal("R-1", nextYear);
    }

    [Fact]
    public async Task Fifty_parallel_releases_produce_a_gapless_invoice_sequence()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var customer = await api.PostOkAsync("/api/v1/customers", new { number = $"P50-{suffix}", name = "Parallel Customer" });
        var address = await api.PostOkAsync("/api/v1/addresses", new
        {
            name = $"Parallel {suffix}",
            street = "4 Test Street",
            postalCode = "10115",
            city = "Berlin",
            countryCode = "DE"
        });
        await api.PostOkAsync($"/api/v1/customers/{customer.Id}/addresses", new { addressId = address.Id, role = "Shipping" });

        var orderIds = new List<Guid>(50);
        for (var index = 0; index < 50; index++)
        {
            var article = await api.PostOkAsync("/api/v1/articles", new
            {
                sku = $"P50-{suffix}-{index:00}",
                name = $"Parallel {index}",
                salesPrice = 1m,
                openingStock = 1m,
                warehouseCode = "MAIN"
            });
            var order = await api.PostOkAsync(
                "/api/v1/sales-orders",
                new
                {
                    customerId = customer.Id,
                    shipToAddressId = address.Id,
                    orderNumber = $"P50-{suffix}-{index:00}",
                    lines = new[] { new { articleId = article.Id, quantity = 1m } }
                },
                Key());
            orderIds.Add(order.Id);
        }

        await Task.WhenAll(orderIds.Select(id =>
            api.PostOkAsync($"/api/v1/sales-orders/{id}/release", new { }, Key())));

        for (var round = 0; round < 8; round++)
        {
            await Task.WhenAll(
                OutboxProcessor.ProcessOnceAsync(fixture.WorkerConnectionString),
                OutboxProcessor.ProcessOnceAsync(fixture.WorkerConnectionString),
                OutboxProcessor.ProcessOnceAsync(fixture.WorkerConnectionString));
            if ((await fixture.Probe.InvoiceSequenceForOrdersAsync(fixture.TenantA, orderIds)).Count == 50) break;
        }

        var numbers = await fixture.Probe.InvoiceSequenceForOrdersAsync(fixture.TenantA, orderIds);
        Assert.Equal(50, numbers.Count);
        Assert.Equal(Enumerable.Range(0, 50).Select(offset => numbers[0] + offset), numbers);

        var tenantNumbers = await fixture.Probe.InvoiceSequenceAsync(fixture.TenantA);
        Assert.Equal(Enumerable.Range(1, tenantNumbers.Count).Select(value => (long)value), tenantNumbers);
    }

    private async Task<string> AllocateAsync(Guid tenantId, NumberDocumentType documentType, DateTimeOffset allocatedAt, bool commit)
    {
        var tenant = new TenantContext(tenantId, "ranges");
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql(fixture.AppConnectionString)
            .AddInterceptors(new TenantConnectionInterceptor(tenant))
            .Options;
        await using var db = new ErpDbContext(options, tenant);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var number = await NumberRangeAllocator.AllocateAsync(db, tenantId, documentType, allocatedAt, CancellationToken.None);
        if (commit) await transaction.CommitAsync();
        else await transaction.RollbackAsync();
        return number;
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
            openingStock = 5m,
            warehouseCode = "MAIN"
        });
        return new TenantGraph(customer.Id, address.Id, article.Id, Guid.Empty, Guid.Empty);
    }

    private static object OrderBody(TenantGraph graph, string? orderNumber) =>
        new
        {
            customerId = graph.CustomerId,
            shipToAddressId = graph.AddressId,
            orderNumber,
            lines = new[] { new { articleId = graph.ArticleId, quantity = 1m } }
        };

    private static string Key() => Guid.NewGuid().ToString("N");

    private static string Text(ApiResult result, string property)
    {
        using var document = System.Text.Json.JsonDocument.Parse(result.Body);
        return document.RootElement.GetProperty(property).GetString()
            ?? throw new InvalidOperationException($"Response has no {property}.");
    }
}
