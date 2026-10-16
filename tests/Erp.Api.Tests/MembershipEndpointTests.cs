using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Erp.Platform.Domain;

namespace Erp.Api.Tests;

[Collection("ErpApi")]
public sealed class MembershipEndpointTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantC = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Authenticated_user_receives_memberships_and_the_server_default()
    {
        await using var fixture = ApiFixture.Start(
            environment: "Production",
            authenticationEnabled: true,
            developmentIdentityEnabled: false,
            user: User("user-1"),
            memberships:
            [
                TenantMembership.Grant("user-1", TenantA, isDefault: false),
                TenantMembership.Grant("user-1", TenantB, isDefault: true),
            ]);

        using var response = await fixture.Client.GetAsync("/api/v1/me/memberships");
        var body = await response.Content.ReadFromJsonAsync<MembershipBody>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Guid.Empty, fixture.Observed.TenantId);
        Assert.NotNull(body);
        Assert.Equal(TenantB, body.DefaultTenantId);
        Assert.Contains(body.Memberships, row => row.TenantId == TenantA && !row.IsDefault);
        Assert.Contains(body.Memberships, row => row.TenantId == TenantB && row.IsDefault);
    }

    [Fact]
    public async Task Authenticated_user_with_no_membership_gets_an_empty_list()
    {
        await using var fixture = ApiFixture.Start(
            environment: "Production",
            authenticationEnabled: true,
            developmentIdentityEnabled: false,
            user: User("user-1"));

        using var response = await fixture.Client.GetAsync("/api/v1/me/memberships");
        var body = await response.Content.ReadFromJsonAsync<MembershipBody>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Guid.Empty, fixture.Observed.TenantId);
        Assert.NotNull(body);
        Assert.Empty(body.Memberships);
        Assert.Null(body.DefaultTenantId);
    }

    [Fact]
    public async Task Anonymous_membership_request_is_unauthorized()
    {
        await using var fixture = ApiFixture.Start(
            environment: "Production",
            authenticationEnabled: true,
            developmentIdentityEnabled: false,
            user: new ClaimsPrincipal(new ClaimsIdentity()));

        using var response = await fixture.Client.GetAsync("/api/v1/me/memberships");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_member_tenant_header_does_not_forbid_the_membership_list()
    {
        await using var fixture = ApiFixture.Start(
            environment: "Production",
            authenticationEnabled: true,
            developmentIdentityEnabled: false,
            user: User("user-1"),
            memberships: [TenantMembership.Grant("user-1", TenantB, isDefault: true)],
            tenantHeader: TenantA.ToString());

        using var response = await fixture.Client.GetAsync("/api/v1/me/memberships");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Guid.Empty, fixture.Observed.TenantId);
    }

    [Fact]
    public async Task Each_user_sees_only_their_own_memberships()
    {
        var shared = new[]
        {
            TenantMembership.Grant("user-a", TenantA, isDefault: true),
            TenantMembership.Grant("user-b", TenantC, isDefault: true),
        };

        await using var fixtureA = ApiFixture.Start(
            environment: "Production",
            authenticationEnabled: true,
            developmentIdentityEnabled: false,
            user: User("user-a"),
            memberships: shared);
        await using var fixtureB = ApiFixture.Start(
            environment: "Production",
            authenticationEnabled: true,
            developmentIdentityEnabled: false,
            user: User("user-b"),
            memberships: shared);

        var bodyA = await Read(fixtureA);
        var bodyB = await Read(fixtureB);

        Assert.Equal([TenantA], bodyA.Memberships.Select(row => row.TenantId).ToArray());
        Assert.Equal([TenantC], bodyB.Memberships.Select(row => row.TenantId).ToArray());
    }

    private static async Task<MembershipBody> Read(ApiFixture fixture)
    {
        using var response = await fixture.Client.GetAsync("/api/v1/me/memberships");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MembershipBody>();
        Assert.NotNull(body);
        return body;
    }

    private sealed record MembershipBody(MembershipRow[] Memberships, Guid? DefaultTenantId);

    private sealed record MembershipRow(Guid TenantId, bool IsDefault);

    private static ClaimsPrincipal User(string userId) =>
        new(new ClaimsIdentity([new Claim("sub", userId)], authenticationType: "Test"));
}
