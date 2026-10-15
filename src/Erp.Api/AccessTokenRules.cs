using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Erp.Api;

internal static class AccessTokenRules
{
    public static string? Reject(ClaimsPrincipal? user, string clientId)
    {
        var tokenUse = user?.FindFirst("token_use")?.Value;
        if (!string.Equals(tokenUse, "access", StringComparison.Ordinal))
        {
            return "An access token is required.";
        }

        var tokenClient = user?.FindFirst("client_id")?.Value;
        if (!string.Equals(tokenClient, clientId, StringComparison.Ordinal))
        {
            return "The access token is for a different client.";
        }

        return null;
    }
}

internal static class CognitoAuthentication
{
    public static void Configure(JwtBearerOptions options, string authority, string clientId)
    {
        var issuer = authority.TrimEnd('/');
        options.Authority = issuer;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = false,
            ValidateLifetime = true,
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var failure = AccessTokenRules.Reject(context.Principal, clientId);
                if (failure is not null)
                {
                    context.Fail(failure);
                }

                return Task.CompletedTask;
            }
        };
    }
}
