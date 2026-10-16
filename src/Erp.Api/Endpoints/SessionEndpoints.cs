using System.Security.Claims;
using Erp.BuildingBlocks;

namespace Erp.Api;

internal static class SessionEndpoints
{
    public static RouteGroupBuilder MapSessionEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/session", async (
            HttpContext context,
            ITenantContext tenant,
            IMandantDirectory mandanten,
            CancellationToken cancellationToken) =>
        {
            var known = await mandanten.FindActiveAsync([tenant.TenantId], cancellationToken);
            known.TryGetValue(tenant.TenantId, out var mandant);
            return Results.Ok(new SessionResponse(
                tenant.TenantId,
                mandant?.Number,
                mandant?.Firmenname,
                UserName(context.User, tenant.UserId, mandant)));
        }).Produces<SessionResponse>();

        return api;
    }

    // Cognito usernames are "{Mandantennummer}.{User}"; the status bar shows only the user part.
    private static string UserName(ClaimsPrincipal user, string userId, MandantSummary? mandant)
    {
        var name = user.FindFirstValue("username");
        if (string.IsNullOrWhiteSpace(name))
        {
            return userId;
        }

        var prefix = mandant is null ? null : $"{mandant.Number}.";
        return prefix is not null && name.StartsWith(prefix, StringComparison.Ordinal) && name.Length > prefix.Length
            ? name[prefix.Length..]
            : name;
    }
}

public sealed record SessionResponse(Guid TenantId, long? MandantNumber, string? Firmenname, string UserName);
