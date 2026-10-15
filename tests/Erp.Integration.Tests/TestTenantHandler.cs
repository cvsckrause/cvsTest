using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Erp.Integration.Tests;

internal sealed class TestTenantHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string TenantHeader = "X-Test-Tenant";
    public const string UserHeader = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var tenant = Request.Headers[TenantHeader].ToString();
        var user = Request.Headers[UserHeader].ToString();
        if (!Guid.TryParse(tenant, out _) || string.IsNullOrWhiteSpace(user))
        {
            return Task.FromResult(AuthenticateResult.Fail("Test tenant headers are required."));
        }

        var identity = new ClaimsIdentity(
            [new Claim("sub", user), new Claim("custom:tenant_id", tenant)],
            authenticationType: Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
