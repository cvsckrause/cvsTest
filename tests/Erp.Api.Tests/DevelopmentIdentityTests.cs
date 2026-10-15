using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Erp.Api.Tests;

public sealed class DevelopmentIdentityTests
{
    private static readonly Guid ClaimTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ConfiguredTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Authenticated_request_without_tenant_claim_is_forbidden()
    {
        await using var fixture = ApiFixture.Start(
            environment: "Production",
            authenticationEnabled: true,
            developmentIdentityEnabled: false,
            configuredTenant: ConfiguredTenant,
            configuredUserId: "local-developer",
            user: AuthenticatedUser("user-1"));

        var status = await GetStatus(fixture, "/api/v1/customers");

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(Guid.Empty, fixture.Observed.TenantId);
    }

    [Fact]
    public async Task Authenticated_request_with_invalid_tenant_claim_is_forbidden()
    {
        await using var fixture = ApiFixture.Start(
            environment: "Production",
            authenticationEnabled: true,
            developmentIdentityEnabled: false,
            configuredTenant: ConfiguredTenant,
            configuredUserId: "local-developer",
            user: AuthenticatedUser("user-1", tenantClaim: "not-a-guid"));

        var status = await GetStatus(fixture, "/api/v1/customers");

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(Guid.Empty, fixture.Observed.TenantId);
    }

    [Fact]
    public async Task Authenticated_request_without_user_id_is_forbidden()
    {
        await using var fixture = ApiFixture.Start(
            environment: "Production",
            authenticationEnabled: true,
            developmentIdentityEnabled: false,
            configuredUserId: "local-developer",
            user: AuthenticatedUser(userId: null, tenantClaim: ClaimTenant.ToString()));

        var status = await GetStatus(fixture, "/api/v1/customers");

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(string.Empty, fixture.Observed.UserId);
    }

    [Fact]
    public async Task Authenticated_request_uses_the_tenant_claim()
    {
        await using var fixture = ApiFixture.Start(
            environment: "Production",
            authenticationEnabled: true,
            developmentIdentityEnabled: false,
            configuredTenant: ConfiguredTenant,
            configuredUserId: "local-developer",
            user: AuthenticatedUser("user-1", tenantClaim: ClaimTenant.ToString()));

        var status = await GetStatus(fixture, "/api/v1/customers");

        Assert.NotEqual(HttpStatusCode.Forbidden, status);
        Assert.Equal(ClaimTenant, fixture.Observed.TenantId);
        Assert.Equal("user-1", fixture.Observed.UserId);
    }

    [Fact]
    public async Task Development_with_authentication_ignores_the_configured_tenant()
    {
        await using var fixture = ApiFixture.Start(
            environment: "Development",
            authenticationEnabled: true,
            developmentIdentityEnabled: true,
            configuredTenant: ConfiguredTenant,
            configuredUserId: "local-developer",
            user: AuthenticatedUser("user-1"));

        var status = await GetStatus(fixture, "/api/v1/customers");

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(Guid.Empty, fixture.Observed.TenantId);
    }

    [Fact]
    public async Task Development_without_authentication_uses_the_configured_identity()
    {
        await using var fixture = ApiFixture.Start(
            environment: "Development",
            authenticationEnabled: false,
            developmentIdentityEnabled: true,
            configuredTenant: ConfiguredTenant,
            configuredUserId: "local-developer");

        var status = await GetStatus(fixture, "/api/v1/customers");

        Assert.NotEqual(HttpStatusCode.Forbidden, status);
        Assert.Equal(ConfiguredTenant, fixture.Observed.TenantId);
        Assert.Equal("local-developer", fixture.Observed.UserId);
    }

    [Fact]
    public async Task Disabled_authentication_outside_development_uses_an_explicit_development_identity()
    {
        await using var fixture = ApiFixture.Start(
            environment: "Production",
            authenticationEnabled: false,
            developmentIdentityEnabled: true,
            configuredTenant: ConfiguredTenant,
            configuredUserId: "local-developer");

        var status = await GetStatus(fixture, "/api/v1/customers");

        Assert.NotEqual(HttpStatusCode.Forbidden, status);
        Assert.Equal(ConfiguredTenant, fixture.Observed.TenantId);
        Assert.Equal("local-developer", fixture.Observed.UserId);
    }

    [Fact]
    public async Task Configured_tenant_is_ignored_when_development_identity_is_disabled()
    {
        await using var fixture = ApiFixture.Start(
            environment: "Production",
            authenticationEnabled: false,
            developmentIdentityEnabled: false,
            configuredTenant: ConfiguredTenant,
            configuredUserId: "local-developer");

        var status = await GetStatus(fixture, "/api/v1/customers");

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(Guid.Empty, fixture.Observed.TenantId);
    }

    [Fact]
    public async Task Health_check_stays_available_without_a_tenant()
    {
        await using var fixture = ApiFixture.Start(
            environment: "Production",
            authenticationEnabled: true,
            developmentIdentityEnabled: false,
            user: new ClaimsPrincipal(new ClaimsIdentity()));

        var status = await GetStatus(fixture, "/health");

        Assert.Equal(HttpStatusCode.OK, status);
    }

    private static async Task<HttpStatusCode?> GetStatus(ApiFixture fixture, string path)
    {
        try
        {
            using var response = await fixture.Client.GetAsync(path);
            return response.StatusCode;
        }
        catch (Exception exception) when (exception.ToString().Contains("Failed to connect", StringComparison.Ordinal))
        {
            return null;
        }
    }

    [Fact]
    public void Enabled_identity_fails_startup_when_authentication_is_on_outside_development()
    {
        using var factory = ApiFixture.CreateFactory(
            environment: "Production",
            authenticationEnabled: true,
            developmentIdentityEnabled: true,
            configuredTenant: ConfiguredTenant,
            configuredUserId: "local-developer",
            user: AuthenticatedUser("user-1", ClaimTenant.ToString()));

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("DevelopmentIdentity:Enabled", exception.ToString(), StringComparison.Ordinal);
    }

    private static ClaimsPrincipal AuthenticatedUser(string? userId, string? tenantClaim = null)
    {
        var claims = new List<Claim>();
        if (userId is not null)
            claims.Add(new Claim("sub", userId));
        if (tenantClaim is not null)
            claims.Add(new Claim("custom:tenant_id", tenantClaim));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }
}

internal sealed class ApiFixture : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    public HttpClient Client { get; }
    public ObservedTenant Observed { get; }

    private ApiFixture(WebApplicationFactory<Program> factory, ObservedTenant observed)
    {
        _factory = factory;
        Observed = observed;
        Client = factory.CreateClient();
    }

    public static ApiFixture Start(
        string environment,
        bool authenticationEnabled,
        bool developmentIdentityEnabled,
        ClaimsPrincipal? user = null,
        Guid? configuredTenant = null,
        string? configuredUserId = null)
    {
        var observed = new ObservedTenant();
        var factory = CreateFactory(
            environment,
            authenticationEnabled,
            developmentIdentityEnabled,
            configuredTenant,
            configuredUserId,
            user,
            observed);
        return new ApiFixture(factory, observed);
    }

    public static WebApplicationFactory<Program> CreateFactory(
        string environment,
        bool authenticationEnabled,
        bool developmentIdentityEnabled,
        Guid? configuredTenant = null,
        string? configuredUserId = null,
        ClaimsPrincipal? user = null,
        ObservedTenant? observed = null)
    {
        observed ??= new ObservedTenant();
        user ??= new ClaimsPrincipal(new ClaimsIdentity());
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Erp",
            "Host=127.0.0.1;Port=1;Timeout=1;Database=erp_test;Username=erp;Password=erp");
        Environment.SetEnvironmentVariable(
            "Authentication__Authority",
            authenticationEnabled ? "https://example.invalid/" : "");
        Environment.SetEnvironmentVariable("Authentication__Audience", "erp-api");
        Environment.SetEnvironmentVariable(
            "DevelopmentIdentity__Enabled",
            developmentIdentityEnabled ? "true" : "false");
        Environment.SetEnvironmentVariable("DevelopmentIdentity__TenantId", configuredTenant?.ToString() ?? "");
        Environment.SetEnvironmentVariable("DevelopmentIdentity__UserId", configuredUserId ?? "");

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(observed);
                services.AddSingleton(new TestPrincipal(user));
                services.AddSingleton<IStartupFilter, TenantObservationFilter>();
                if (authenticationEnabled)
                {
                    services.AddAuthentication()
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                    services.PostConfigure<AuthenticationOptions>(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                    });
                }
            });
        });
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
    }
}

internal sealed class ObservedTenant
{
    public Guid TenantId { get; private set; }
    public string UserId { get; private set; } = string.Empty;

    public void Record(Guid tenantId, string userId)
    {
        TenantId = tenantId;
        UserId = userId;
    }
}

internal sealed class TestPrincipal(ClaimsPrincipal user)
{
    public ClaimsPrincipal User { get; } = user;
}

internal sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    TestPrincipal principal)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var ticket = new AuthenticationTicket(principal.User, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

internal sealed class TenantObservationFilter(ObservedTenant observed) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                try
                {
                    await nextMiddleware();
                }
                finally
                {
                    if (context.Request.Path.StartsWithSegments("/api"))
                    {
                        var tenant = context.RequestServices.GetService<RequestTenantContext>();
                        if (tenant is not null)
                            observed.Record(tenant.TenantId, tenant.UserId);
                    }
                }
            });
            next(app);
        };
    }
}
