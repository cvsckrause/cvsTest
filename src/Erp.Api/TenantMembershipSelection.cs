using Erp.Platform.Domain;

namespace Erp.Api;

internal readonly record struct TenantChoice(Guid? TenantId)
{
    public static TenantChoice Allow(Guid tenantId) => new(tenantId);
    public static TenantChoice Deny { get; } = new(null);
}

internal static class TenantMembershipSelection
{
    public const string TenantHeader = "X-Tenant-Id";

    public static TenantChoice Choose(string? tenantHeader, IReadOnlyList<TenantMembership> memberships)
    {
        if (memberships.Count == 0)
        {
            return TenantChoice.Deny;
        }

        var ordered = memberships.OrderBy(membership => membership.TenantId).ToArray();
        if (string.IsNullOrWhiteSpace(tenantHeader))
        {
            var fallback = ordered.FirstOrDefault(membership => membership.IsDefault) ?? ordered[0];
            return TenantChoice.Allow(fallback.TenantId);
        }

        if (!Guid.TryParse(tenantHeader, out var requested))
        {
            return TenantChoice.Deny;
        }

        return ordered.Any(membership => membership.TenantId == requested)
            ? TenantChoice.Allow(requested)
            : TenantChoice.Deny;
    }
}
