using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.BuildingBlocks;
using Erp.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.Integration.Tests;

[Collection(PostgresCollection.Name)]
public sealed class InvoiceIssueTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Worker_issues_seller_buyer_lines_and_per_rate_totals_from_the_release()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var customer = await api.PostOkAsync("/api/v1/customers", new { number = $"NR-{suffix}", name = "Nordrad GmbH" });
        var shipTo = await api.PostOkAsync("/api/v1/addresses", new
        {
            name = "Lager Nord",
            street = "Kai 1",
            postalCode = "20457",
            city = "Hamburg",
            countryCode = "DE"
        });
        var billTo = await api.PostOkAsync("/api/v1/addresses", new
        {
            name = "Nordrad GmbH",
            street = "Rechnung 2",
            postalCode = "20095",
            city = "Hamburg",
            countryCode = "DE",
            additionalLine = "Buchhaltung"
        });
        await api.PostOkAsync($"/api/v1/customers/{customer.Id}/addresses", new { addressId = shipTo.Id, role = "Shipping" });
        await api.PostOkAsync($"/api/v1/customers/{customer.Id}/addresses", new { addressId = billTo.Id, role = "Billing" });
        var standard = await api.PostOkAsync("/api/v1/articles", new
        {
            sku = $"STD-{suffix}",
            name = "Cityrad",
            salesPrice = 10m,
            openingStock = 5m
        });
        var reduced = await api.PostOkAsync("/api/v1/articles", new
        {
            sku = $"RED-{suffix}",
            name = "Sattel",
            salesPrice = 1.50m,
            openingStock = 5m,
            taxCode = "Reduced7"
        });
        var order = await api.PostOkAsync(
            "/api/v1/sales-orders",
            new
            {
                customerId = customer.Id,
                shipToAddressId = shipTo.Id,
                orderNumber = $"INV-{suffix}",
                lines = new[]
                {
                    new { articleId = standard.Id, quantity = 2m },
                    new { articleId = reduced.Id, quantity = 1m }
                }
            },
            $"issue-{suffix}");
        using var sellerBefore = JsonDocument.Parse((await api.GetAsync("/api/v1/mandant/seller")).Body);
        var streetAtRelease = sellerBefore.RootElement.GetProperty("street").GetString();
        await api.PostOkAsync($"/api/v1/sales-orders/{order.Id}/release", new { }, $"issue-release-{suffix}");
        var changed = await api.PutAsync("/api/v1/mandant/seller", new
        {
            street = "Nach Freigabe 9",
            postalCode = sellerBefore.RootElement.GetProperty("postalCode").GetString(),
            city = sellerBefore.RootElement.GetProperty("city").GetString(),
            countryCode = sellerBefore.RootElement.GetProperty("countryCode").GetString(),
            steuernummer = sellerBefore.RootElement.GetProperty("steuernummer").GetString(),
            ustIdNr = sellerBefore.RootElement.GetProperty("ustIdNr").GetString()
        });
        Assert.Equal(System.Net.HttpStatusCode.OK, changed.Status);
        var messageId = await fixture.Probe.OutboxIdForOrderAsync(fixture.TenantA, order.Id);

        try
        {
            await ProcessUntilAsync(messageId);

            await using var db = new ErpDbContext(
            new DbContextOptionsBuilder<ErpDbContext>().UseNpgsql(fixture.AdminConnectionString).Options,
            new TenantContext(fixture.TenantA, "invoice-issue"));
        var invoice = await db.Invoices
            .Include(item => item.Lines)
            .Include(item => item.TaxTotals)
            .SingleAsync(item => item.SalesOrderId == order.Id);
        var occurredAt = await OutboxOccurredAtAsync(order.Id);
        var berlinDate = BusinessDate.Berlin(occurredAt);

        Assert.Equal("A Handels GmbH", invoice.SellerName);
        Assert.Equal(streetAtRelease, invoice.SellerStreet);
        Assert.NotEqual("Nach Freigabe 9", invoice.SellerStreet);
        Assert.Equal("20095", invoice.SellerPostalCode);
        Assert.Equal("Hamburg", invoice.SellerCity);
        Assert.Equal("DE", invoice.SellerCountryCode);
        Assert.Equal("22/111/11111", invoice.SellerSteuernummer);
        Assert.Equal("DE111111111", invoice.SellerUstIdNr);
        Assert.Equal("Nordrad GmbH", invoice.BuyerName);
        Assert.Equal($"NR-{suffix}", invoice.BuyerNumber);
        Assert.Equal("Rechnung 2", invoice.BuyerStreet);
        Assert.Equal("20095", invoice.BuyerPostalCode);
        Assert.Equal("Hamburg", invoice.BuyerCity);
        Assert.Equal("DE", invoice.BuyerCountryCode);
        Assert.Equal("Buchhaltung", invoice.BuyerAdditionalLine);
        Assert.Equal(berlinDate, invoice.InvoiceDate);
        Assert.Equal(berlinDate, invoice.DeliveryDate);
        Assert.True(invoice.ContentComplete);
        Assert.Equal(new[] { ("STD-" + suffix, "Cityrad", 2m, 10m, 20.00m, 19m), ("RED-" + suffix, "Sattel", 1m, 1.50m, 1.50m, 7m) },
            invoice.Lines.Select(line => (line.Sku, line.Description, line.Quantity, line.UnitPrice, line.NetAmount, line.Rate)).ToArray());
        Assert.Equal(new[] { (19m, "S", 20.00m, 3.80m), (7m, "S", 1.50m, 0.11m) },
            invoice.TaxTotals.Select(total => (total.Rate, total.Category, total.NetAmount, total.TaxAmount)).ToArray());
        Assert.Equal(21.50m, invoice.NetAmount);
        Assert.Equal(3.91m, invoice.TaxAmount);
            Assert.Equal(25.41m, invoice.GrossAmount);
        }
        finally
        {
            var restored = await api.PutAsync("/api/v1/mandant/seller", new
            {
                street = streetAtRelease,
                postalCode = sellerBefore.RootElement.GetProperty("postalCode").GetString(),
                city = sellerBefore.RootElement.GetProperty("city").GetString(),
                countryCode = sellerBefore.RootElement.GetProperty("countryCode").GetString(),
                additionalLine = sellerBefore.RootElement.GetProperty("additionalLine").GetString(),
                steuernummer = sellerBefore.RootElement.GetProperty("steuernummer").GetString(),
                ustIdNr = sellerBefore.RootElement.GetProperty("ustIdNr").GetString()
            });
            Assert.Equal(System.Net.HttpStatusCode.OK, restored.Status);
        }
    }

    [Fact]
    public async Task Worker_rejects_a_release_without_a_seller_and_creates_no_documents()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var article = await api.PostOkAsync("/api/v1/articles", new
        {
            sku = $"NOS-{suffix}",
            name = "Ohne Verkäufer",
            salesPrice = 4m,
            openingStock = 2m
        });
        var order = await api.PostOkAsync(
            "/api/v1/sales-orders",
            new
            {
                customerId = fixture.SeedA.CustomerId,
                shipToAddressId = fixture.SeedA.AddressId,
                orderNumber = $"NOS-{suffix}",
                lines = new[] { new { articleId = article.Id, quantity = 1m } }
            },
            $"noseller-{suffix}");
        await api.PostOkAsync($"/api/v1/sales-orders/{order.Id}/release", new { }, $"noseller-release-{suffix}");
        var messageId = await fixture.Probe.OutboxIdForOrderAsync(fixture.TenantA, order.Id);
        await StripSellerAsync(messageId);

        await ProcessUntilAsync(messageId);

        Assert.Equal(0, await fixture.Probe.CountDeliveriesForOrderAsync(fixture.TenantA, order.Id));
        Assert.Equal(0, await fixture.Probe.CountInvoicesForOrderAsync(fixture.TenantA, order.Id));
        Assert.Contains("seller", await LastErrorAsync(messageId), StringComparison.OrdinalIgnoreCase);
    }

    private async Task ProcessUntilAsync(Guid messageId)
    {
        for (var attempt = 0; attempt < 8 && !await fixture.Probe.OutboxProcessedAsync(fixture.TenantA, messageId); attempt++)
        {
            await OutboxProcessor.ProcessOnceAsync(fixture.WorkerConnectionString);
        }
    }

    private async Task<DateTimeOffset> OutboxOccurredAtAsync(Guid salesOrderId)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select "OccurredAt" from erp.outbox_messages
            where "TenantId" = @tenant and "Payload" like @pattern
            order by "OccurredAt" desc
            limit 1
            """,
            connection);
        command.Parameters.AddWithValue("tenant", fixture.TenantA);
        command.Parameters.AddWithValue("pattern", "%" + salesOrderId + "%");
        var value = await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Missing outbox timestamp.");
        return value switch
        {
            DateTimeOffset offset => offset,
            DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException($"Unexpected outbox timestamp {value.GetType().Name}."),
        };
    }

    private async Task StripSellerAsync(Guid messageId)
    {
        var payload = JsonNode.Parse(await fixture.Probe.PayloadAsync(fixture.TenantA, messageId))!.AsObject();
        payload.Remove("Seller");
        payload.Remove("seller");
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """update erp.outbox_messages set "Payload" = @payload where "Id" = @id""", connection);
        command.Parameters.AddWithValue("payload", payload.ToJsonString());
        command.Parameters.AddWithValue("id", messageId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private async Task<string> LastErrorAsync(Guid messageId)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """select "LastError" from erp.outbox_messages where "Id" = @id""", connection);
        command.Parameters.AddWithValue("id", messageId);
        return (string)(await command.ExecuteScalarAsync() ?? "");
    }
}
