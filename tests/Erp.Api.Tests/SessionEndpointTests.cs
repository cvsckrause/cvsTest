using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Erp.Platform.Domain;

namespace Erp.Api.Tests;

[Collection("ErpApi")]
public sealed class SessionEndpointTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Session_shows_the_Mandant_and_the_user_part_of_the_Cognito_username()
    {
        await using var fixture = ApiFixture.Start(
            environment: "Production",
            authenticationEnabled: true,
            developmentIdentityEnabled: false,
            user: User("sub-1", "1001.kunde"),
            memberships: [TenantMembership.Grant("sub-1", Tenant, isDefault: true)],
            mandanten: [new MandantSummary(Tenant, 1001, "Nordlicht GmbH")]);

        var body = await fixture.Client.GetFromJsonAsync<SessionBody>("/api/v1/session");

        Assert.NotNull(body);
        Assert.Equal(new SessionBody(Tenant, 1001, "Nordlicht GmbH", "kunde"), body);
    }

    [Fact]
    public async Task Session_without_a_registry_row_has_no_Mandant_label()
    {
        await using var fixture = ApiFixture.Start(
            environment: "Production",
            authenticationEnabled: true,
            developmentIdentityEnabled: false,
            user: User("smoke-1", "smoke-1"),
            memberships: [TenantMembership.Grant("smoke-1", Tenant, isDefault: true)]);

        var body = await fixture.Client.GetFromJsonAsync<SessionBody>("/api/v1/session");

        Assert.NotNull(body);
        Assert.Equal(new SessionBody(Tenant, null, null, "smoke-1"), body);
    }

    [Fact]
    public async Task Membership_list_carries_the_Mandant_number_and_name()
    {
        await using var fixture = ApiFixture.Start(
            environment: "Production",
            authenticationEnabled: true,
            developmentIdentityEnabled: false,
            user: User("sub-1", "1001.kunde"),
            memberships: [TenantMembership.Grant("sub-1", Tenant, isDefault: true)],
            mandanten: [new MandantSummary(Tenant, 1001, "Nordlicht GmbH")]);

        using var response = await fixture.Client.GetAsync("/api/v1/me/memberships");
        var body = await response.Content.ReadFromJsonAsync<MembershipBody>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        var row = Assert.Single(body.Memberships);
        Assert.Equal(1001, row.MandantNumber);
        Assert.Equal("Nordlicht GmbH", row.Firmenname);
    }

    private sealed record SessionBody(Guid TenantId, long? MandantNumber, string? Firmenname, string UserName);

    private sealed record MembershipBody(MembershipRow[] Memberships, Guid? DefaultTenantId);

    private sealed record MembershipRow(Guid TenantId, bool IsDefault, long? MandantNumber, string? Firmenname);

    private static ClaimsPrincipal User(string subject, string username) =>
        new(new ClaimsIdentity([new Claim("sub", subject), new Claim("username", username)], authenticationType: "Test"));
}
