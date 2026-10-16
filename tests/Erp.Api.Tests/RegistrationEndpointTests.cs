using System.Net;
using System.Net.Http.Json;
using Erp.Api.Registration;
using Microsoft.Extensions.DependencyInjection;

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
    public async Task Behind_two_proxies_the_rate_limit_uses_the_viewer_address()
    {
        await using var fixture = ApiFixture.Start(
            environment: "Production",
            authenticationEnabled: true,
            developmentIdentityEnabled: false,
            registrationPermits: 1,
            forwardLimit: 2);

        async Task<HttpStatusCode> Approve(string forwardedFor)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/registrations/approve")
            {
                Content = JsonContent.Create(new { token = "" }),
            };
            request.Headers.Add("X-Forwarded-For", forwardedFor);
            using var response = await fixture.Client.SendAsync(request);
            return response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.NotFound, await Approve("203.0.113.9, 198.51.100.1"));
        Assert.Equal(HttpStatusCode.NotFound, await Approve("203.0.113.10, 198.51.100.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Approve("192.0.2.77, 203.0.113.9, 198.51.100.1"));
    }

    [Fact]
    public async Task Configured_aws_settings_select_ses_and_cognito_administration()
    {
        await using var fixture = ApiFixture.Start(
            environment: "Production",
            authenticationEnabled: true,
            developmentIdentityEnabled: false,
            awsRegistration: true);

        Assert.IsType<SesMailSender>(fixture.Services.GetRequiredService<IMailSender>());
        Assert.IsType<AwsCognitoUserAdmin>(fixture.Services.GetRequiredService<ICognitoUserAdmin>());
    }

    [Fact]
    public async Task Without_aws_settings_production_registration_is_unavailable()
    {
        await using var fixture = ApiFixture.Start(
            environment: "Production",
            authenticationEnabled: true,
            developmentIdentityEnabled: false);

        Assert.IsType<UnconfiguredMailSender>(fixture.Services.GetRequiredService<IMailSender>());
        Assert.IsType<UnconfiguredCognitoUserAdmin>(fixture.Services.GetRequiredService<ICognitoUserAdmin>());
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
