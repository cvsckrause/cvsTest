using System.Net;
using System.Net.Http.Json;
using Erp.Api.Registration;

namespace Erp.Api.Tests;

[Collection("ErpApi")]
public sealed class RegistrationEndpointTests
{
    [Fact]
    public async Task Registration_is_reachable_without_a_login()
    {
        await using var fixture = ApiFixture.Start(
            environment: "Production",
            authenticationEnabled: true,
            developmentIdentityEnabled: false);

        using var response = await fixture.Client.PostAsJsonAsync(
            "/api/v1/registrations",
            new { firmenname = "", email = "info@example.test" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(Guid.Empty, fixture.Observed.TenantId);
    }

    [Fact]
    public async Task Registration_is_rate_limited_per_client()
    {
        await using var fixture = ApiFixture.Start(
            environment: "Production",
            authenticationEnabled: true,
            developmentIdentityEnabled: false,
            registrationPermits: 2);

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var response = await fixture.Client.PostAsJsonAsync(
                "/api/v1/registrations/approve",
                new { token = "" });
            statuses.Add(response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public void Initial_passwords_meet_the_pool_policy()
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var password = InitialPassword.Create();

            Assert.True(password.Length >= 12);
            Assert.Contains(password, char.IsUpper);
            Assert.Contains(password, char.IsLower);
            Assert.Contains(password, char.IsDigit);
            Assert.Contains(password, character => !char.IsLetterOrDigit(character));
        }
    }

    [Fact]
    public void Approval_tokens_are_stored_only_as_a_hash()
    {
        var token = ApprovalToken.Create();

        Assert.True(ApprovalToken.TryHash(token.Value, out var hash));
        Assert.Equal(token.Hash, hash);
        Assert.Equal(64, hash.Length);
        Assert.DoesNotContain(token.Value, hash, StringComparison.Ordinal);
        Assert.False(ApprovalToken.TryHash("  ", out _));
    }
}
