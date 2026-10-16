using System.Security.Claims;

namespace Erp.Api;

internal static class AccessTokenSubject
{
    public static string? Read(ClaimsPrincipal user)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return string.IsNullOrWhiteSpace(userId) ? null : userId;
    }
}

internal static class MembershipEndpoints
{
    public const string ApiPrefix = "/api/v1";
    public const string RelativePath = "/me/memberships";
    public const string Path = ApiPrefix + RelativePath;

    public static RouteGroupBuilder MapMembershipEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet(RelativePath, async (
            HttpContext context,
            ITenantMemberships memberships,
            IMandantDirectory mandanten,
            CancellationToken cancellationToken) =>
        {
            var userId = AccessTokenSubject.Read(context.User);
            if (userId is null)
            {
                return Results.Problem(
                    title: "The request is not allowed.",
                    detail: "The access token has no subject.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var rows = await memberships.ListAsync(userId, cancellationToken);
            var choice = TenantMembershipSelection.Choose(null, rows);
            var known = await mandanten.FindActiveAsync(
                rows.Select(row => row.TenantId).ToArray(),
                cancellationToken);
            return Results.Ok(new MembershipListResponse(
                rows.Select(row =>
                {
                    known.TryGetValue(row.TenantId, out var mandant);
                    return new MembershipResponse(row.TenantId, row.IsDefault, mandant?.Number, mandant?.Firmenname);
                }).ToArray(),
                choice.TenantId));
        }).Produces<MembershipListResponse>();

        return api;
    }
}

public sealed record MembershipResponse(Guid TenantId, bool IsDefault, long? MandantNumber, string? Firmenname);

public sealed record MembershipListResponse(
    IReadOnlyList<MembershipResponse> Memberships,
    Guid? DefaultTenantId);
