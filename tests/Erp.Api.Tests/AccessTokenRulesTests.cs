using System.Security.Claims;
using Erp.Platform.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Erp.Api.Tests;

public sealed class AccessTokenRulesTests
{
    [Fact]
    public async Task Access_token_for_the_configured_client_is_accepted()
    {
        var context = await Validate(("token_use", "access"), ("client_id", "spa-client"));

        Assert.Null(context.Result?.Failure);
    }

    [Fact]
    public async Task Id_token_is_rejected()
    {
        var context = await Validate(("token_use", "id"), ("client_id", "spa-client"));

        Assert.NotNull(context.Result?.Failure);
    }

    [Fact]
    public async Task Access_token_for_another_client_is_rejected()
    {
        var context = await Validate(("token_use", "access"), ("client_id", "other-client"));

        Assert.NotNull(context.Result?.Failure);
    }

    [Fact]
    public void Issuer_is_validated_and_audience_is_not()
    {
        var options = new JwtBearerOptions();
        CognitoAuthentication.Configure(options, "https://cognito-idp.eu-central-1.amazonaws.com/pool/", "spa-client");

        Assert.Equal("https://cognito-idp.eu-central-1.amazonaws.com/pool", options.Authority);
        Assert.True(options.TokenValidationParameters.ValidateIssuer);
        Assert.Equal("https://cognito-idp.eu-central-1.amazonaws.com/pool", options.TokenValidationParameters.ValidIssuer);
        Assert.False(options.TokenValidationParameters.ValidateAudience);
    }

    private static async Task<TokenValidatedContext> Validate(params (string Type, string Value)[] claims)
    {
        var options = new JwtBearerOptions();
        CognitoAuthentication.Configure(options, "https://cognito-idp.eu-central-1.amazonaws.com/pool", "spa-client");
        var scheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        var context = new TokenValidatedContext(new DefaultHttpContext(), scheme, options)
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(
                claims.Select(claim => new Claim(claim.Type, claim.Value)),
                authenticationType: "Bearer"))
        };
        await options.Events.OnTokenValidated(context);
        return context;
    }
}

public sealed class TenantMembershipTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void Missing_header_uses_the_default_membership()
    {
        var choice = TenantMembershipSelection.Choose(null,
        [
            TenantMembership.Grant("user-1", TenantB, isDefault: false),
            TenantMembership.Grant("user-1", TenantA, isDefault: true)
        ]);

        Assert.Equal(TenantA, choice.TenantId);
    }

    [Fact]
    public void Member_can_select_another_allowed_tenant()
    {
        var choice = TenantMembershipSelection.Choose(TenantB.ToString(),
        [
            TenantMembership.Grant("user-1", TenantA, isDefault: true),
            TenantMembership.Grant("user-1", TenantB, isDefault: false)
        ]);

        Assert.Equal(TenantB, choice.TenantId);
    }

    [Fact]
    public void Non_member_tenant_is_rejected()
    {
        var choice = TenantMembershipSelection.Choose(TenantB.ToString(),
            [TenantMembership.Grant("user-1", TenantA, isDefault: true)]);

        Assert.Null(choice.TenantId);
    }

    [Fact]
    public void Seed_text_grants_the_synthetic_users()
    {
        var grants = TenantMembership.ParseSeed(
            $"synthetic-a:{TenantA}:default;synthetic-b:{TenantB}:default");

        Assert.Equal(["synthetic-a", "synthetic-b"], grants.Select(grant => grant.UserId));
        Assert.Equal([TenantA, TenantB], grants.Select(grant => grant.TenantId));
        Assert.All(grants, grant => Assert.True(grant.IsDefault));
    }
}
