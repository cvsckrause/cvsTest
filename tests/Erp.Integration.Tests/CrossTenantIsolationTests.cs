using System.Net;

namespace Erp.Integration.Tests;

[Collection(PostgresCollection.Name)]
public sealed class CrossTenantIsolationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Tenant_b_lists_and_details_never_return_tenant_a()
    {
        using var client = fixture.OpenSession(fixture.TenantB);
        var owner = fixture.SeedA;
        var viewer = fixture.SeedB;

        await AssertListHidesOwner(client, "/api/v1/customers", owner.CustomerId, viewer.CustomerId);
        await AssertListHidesOwner(client, "/api/v1/addresses", owner.AddressId, viewer.AddressId);
        await AssertListHidesOwner(client, "/api/v1/articles", owner.ArticleId, viewer.ArticleId);
        await AssertListHidesOwner(client, "/api/v1/stock", owner.ArticleId, viewer.ArticleId);
        await AssertListHidesOwner(client, "/api/v1/stock/movements", owner.MovementId, viewer.MovementId);
        await AssertListHidesOwner(client, $"/api/v1/customers/{viewer.CustomerId}/addresses", owner.AddressId, viewer.AddressId);

        await AssertDetailHidden(client, $"/api/v1/addresses/{owner.AddressId}");
        await AssertDetailVisible(client, $"/api/v1/addresses/{viewer.AddressId}", viewer.AddressId);
        await AssertDetailHidden(client, $"/api/v1/articles/{owner.ArticleId}");
        await AssertDetailVisible(client, $"/api/v1/articles/{viewer.ArticleId}", viewer.ArticleId);
        await AssertDetailHidden(client, $"/api/v1/sales-orders/{owner.SalesOrderId}");
        await AssertDetailVisible(client, $"/api/v1/sales-orders/{viewer.SalesOrderId}", viewer.SalesOrderId);
        await AssertDetailHidden(client, $"/api/v1/customers/{owner.CustomerId}/addresses");
    }

    private static async Task AssertListHidesOwner(ApiSession client, string path, Guid ownerId, Guid viewerId)
    {
        var result = await client.GetAsync(path);
        Assert.True(result.Status == HttpStatusCode.OK, $"{path} returned {(int)result.Status}: {result.Body}");
        Assert.False(result.Contains(ownerId), $"{path} returned tenant-A id {ownerId}.");
        Assert.True(result.Contains(viewerId), $"{path} did not return tenant-B id {viewerId}.");
    }

    private static async Task AssertDetailHidden(ApiSession client, string path)
    {
        var result = await client.GetAsync(path);
        Assert.True(result.Status == HttpStatusCode.NotFound, $"{path} returned {(int)result.Status}: {result.Body}");
    }

    private static async Task AssertDetailVisible(ApiSession client, string path, Guid viewerId)
    {
        var result = await client.GetAsync(path);
        Assert.True(result.Status == HttpStatusCode.OK, $"{path} returned {(int)result.Status}: {result.Body}");
        Assert.True(result.Contains(viewerId), $"{path} did not return {viewerId}.");
    }
}
