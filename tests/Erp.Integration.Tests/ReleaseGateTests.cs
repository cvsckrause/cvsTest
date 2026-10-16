using System.Net;
using System.Text.Json;
using Erp.BuildingBlocks;
using Erp.Persistence;
using Erp.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Integration.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ReleaseGateTests(PostgresFixture fixture)
{
    private static readonly (Guid Tenant, string User, long Number) RetryTenant =
        (Guid.Parse("0000000d-0000-0000-0000-000000000016"), "integration-incomplete-seller", 903);
    private static readonly (Guid Tenant, string User, long Number) RefusedTenant =
        (Guid.Parse("0000000e-0000-0000-0000-000000000016"), "integration-incomplete-seller-2", 904);

    [Fact]
    public async Task Release_with_incomplete_seller_details_returns_409_and_changes_nothing_until_they_are_complete()
    {
        await SeedIncompleteMandantAsync(RetryTenant);
        using var api = fixture.OpenSession(RetryTenant.User, RetryTenant.Tenant);
        var (orderId, articleId) = await DraftOrderAsync(api, "IC");
        var releaseKey = $"ic-release-{Guid.NewGuid():N}";

        var refused = await api.PostAsync($"/api/v1/sales-orders/{orderId}/release", new { }, releaseKey);

        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        using (var body = JsonDocument.Parse(refused.Body))
        {
            Assert.Contains("Seller details are incomplete", body.RootElement.GetProperty("error").GetString());
            Assert.Equal(
                Mandant.SellerFields,
                body.RootElement.GetProperty("missingSellerFields").EnumerateArray().Select(item => item.GetString()));
        }

        Assert.Equal(0m, await ReservedAsync(api, articleId));
        Assert.Equal(0, await fixture.Probe.CountOutboxForOrderAsync(RetryTenant.Tenant, orderId));
        Assert.Equal("Draft", StatusOf((await api.GetAsync($"/api/v1/sales-orders/{orderId}")).Body));

        var completed = await api.PutAsync("/api/v1/mandant/seller", new
        {
            street = "Deichstraße 4",
            postalCode = "20459",
            city = "Hamburg",
            countryCode = "DE",
            steuernummer = "22/333/33333"
        });
        Assert.Equal(HttpStatusCode.OK, completed.Status);

        var retried = await api.PostAsync($"/api/v1/sales-orders/{orderId}/release", new { }, releaseKey);

        Assert.Equal(HttpStatusCode.OK, retried.Status);
        Assert.Equal("Released", StatusOf(retried.Body));
        Assert.Equal(1m, await ReservedAsync(api, articleId));
    }

    [Fact]
    public async Task Release_payload_carries_the_releasing_tenants_seller_and_the_worker_still_delivers()
    {
        await SeedIncompleteMandantAsync(RefusedTenant);
        using var apiA = fixture.OpenSession(fixture.TenantA);
        using var apiRefused = fixture.OpenSession(RefusedTenant.User, RefusedTenant.Tenant);
        var (refusedOrder, _) = await DraftOrderAsync(apiRefused, "RF");
        var (orderA, _) = await DraftOrderAsync(apiA, "SA");
        string? sellerStreetA;
        using (var sellerA = JsonDocument.Parse((await apiA.GetAsync("/api/v1/mandant/seller")).Body))
        {
            sellerStreetA = sellerA.RootElement.GetProperty("street").GetString();
        }

        var refused = await apiRefused.PostAsync($"/api/v1/sales-orders/{refusedOrder}/release", new { }, $"rf-{Guid.NewGuid():N}");
        var released = await apiA.PostAsync($"/api/v1/sales-orders/{orderA}/release", new { }, $"sa-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        Assert.Equal(HttpStatusCode.OK, released.Status);
        var messageId = await fixture.Probe.OutboxIdForOrderEventAsync(fixture.TenantA, orderA, "erp.sales-order.released.v1");
        using (var payload = JsonDocument.Parse(await fixture.Probe.PayloadAsync(fixture.TenantA, messageId)))
        {
            var seller = payload.RootElement.GetProperty("seller");
            Assert.Equal("A Handels GmbH", seller.GetProperty("Name").GetString());
            Assert.Equal(sellerStreetA, seller.GetProperty("Street").GetString());
            Assert.Equal("Hamburg", seller.GetProperty("City").GetString());
        }

        await OutboxProcessor.ProcessOnceAsync(fixture.WorkerConnectionString);

        Assert.True(await fixture.Probe.OutboxProcessedAsync(fixture.TenantA, messageId));
        Assert.Equal(1, await fixture.Probe.CountDeliveriesForOrderAsync(fixture.TenantA, orderA));
    }

    private static async Task<(Guid OrderId, Guid ArticleId)> DraftOrderAsync(ApiSession api, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var customer = await api.PostOkAsync("/api/v1/customers", new { number = suffix, name = $"{label} Kunde" });
        var address = await api.PostOkAsync("/api/v1/addresses", new
        {
            name = $"{label} Lager",
            street = "1 Gate Street",
            postalCode = "10115",
            city = "Berlin",
            countryCode = "DE"
        });
        await api.PostOkAsync($"/api/v1/customers/{customer.Id}/addresses", new { addressId = address.Id, role = "Shipping" });
        var article = await api.PostOkAsync("/api/v1/articles", new { sku = suffix, name = $"{label} Artikel", salesPrice = 5m, openingStock = 3m });
        var order = await api.PostOkAsync(
            "/api/v1/sales-orders",
            new { customerId = customer.Id, shipToAddressId = address.Id, lines = new[] { new { articleId = article.Id, quantity = 1m } } },
            $"{suffix}-order");
        return (order.Id, article.Id);
    }

    private static async Task<decimal> ReservedAsync(ApiSession api, Guid articleId)
    {
        var stock = await api.GetAsync("/api/v1/stock");
        using var document = JsonDocument.Parse(stock.Body);
        return document.RootElement.EnumerateArray()
            .Single(item => item.GetProperty("articleId").GetGuid() == articleId)
            .GetProperty("reserved").GetDecimal();
    }

    private static string? StatusOf(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("status").GetString();
    }

    private async Task SeedIncompleteMandantAsync((Guid Tenant, string User, long Number) mandant)
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql(fixture.AdminConnectionString)
            .Options;
        await using var db = new ErpDbContext(options, new TenantContext(Guid.NewGuid(), "release-gate-test"));
        if (await db.Mandanten.AnyAsync(item => item.Id == mandant.Tenant)) return;
        db.Mandanten.Add(Mandant.Seed(mandant.Tenant, mandant.Number, $"Unvollständig {mandant.Number} GmbH", DateTimeOffset.UtcNow));
        db.TenantMemberships.Add(TenantMembership.Grant(mandant.User, mandant.Tenant, isDefault: true));
        await db.SaveChangesAsync();
    }
}
