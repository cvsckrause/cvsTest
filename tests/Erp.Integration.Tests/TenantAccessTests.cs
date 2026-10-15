using System.Net;

namespace Erp.Integration.Tests;

[Collection(PostgresCollection.Name)]
public sealed class TenantAccessTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Anonymous_request_is_unauthorized()
    {
        using var client = fixture.CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/customers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_member_tenant_header_is_forbidden()
    {
        using var api = fixture.OpenSession("integration-a", fixture.TenantB);

        var result = await api.GetAsync("/api/v1/customers");

        Assert.Equal(HttpStatusCode.Forbidden, result.Status);
        Assert.False(result.Contains(fixture.SeedA.CustomerId));
        Assert.False(result.Contains(fixture.SeedB.CustomerId));
    }

    [Fact]
    public async Task Member_sees_only_the_selected_tenant()
    {
        using var api = fixture.OpenSession("integration-b", fixture.TenantB);

        var result = await api.GetAsync("/api/v1/customers");

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.True(result.Contains(fixture.SeedB.CustomerId));
        Assert.False(result.Contains(fixture.SeedA.CustomerId));
    }

    [Fact]
    public async Task Missing_tenant_header_uses_the_default_membership()
    {
        using var api = fixture.OpenSession("integration-a", tenantId: null);

        var result = await api.GetAsync("/api/v1/customers");

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.True(result.Contains(fixture.SeedA.CustomerId));
        Assert.False(result.Contains(fixture.SeedB.CustomerId));
    }
}
