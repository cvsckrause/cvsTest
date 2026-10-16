using System.Security.Claims;

namespace Erp.Api;

internal static class MembershipEndpoints
{
    public static RouteGroupBuilder MapMembershipEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me/memberships", async (
            HttpContext context,
            ITenantMemberships memberships,
            CancellationToken cancellationToken) =>
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.User.FindFirstValue("sub");
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Results.Problem(
                    title: "The request is not allowed.",
                    detail: "The access token has no subject.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var rows = await memberships.ListAsync(userId, cancellationToken);
            var choice = TenantMembershipSelection.Choose(null, rows);
            return Results.Ok(new MembershipListResponse(
                rows.Select(row => new MembershipResponse(row.TenantId, row.IsDefault)).ToArray(),
                choice.TenantId));
        }).Produces<MembershipListResponse>();

        return api;
    }
}

public sealed record MembershipResponse(Guid TenantId, bool IsDefault);

public sealed record MembershipListResponse(
    IReadOnlyList<MembershipResponse> Memberships,
    Guid? DefaultTenantId);
