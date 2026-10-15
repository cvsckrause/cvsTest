using System.Security.Claims;
using Microsoft.Extensions.Configuration;

namespace Erp.Api;

internal sealed record DevelopmentIdentity(bool Enabled, Guid? TenantId, string? UserId)
{
    public static DevelopmentIdentity FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("DevelopmentIdentity");
        return new DevelopmentIdentity(
            section.GetValue("Enabled", false),
            Guid.TryParse(section["TenantId"], out var tenantId) ? tenantId : null,
            section["UserId"]);
    }

    public TenantIdentity? Resolve(ClaimsPrincipal user, bool authenticationEnabled)
    {
        if (authenticationEnabled)
        {
            var claim = user.FindFirstValue("custom:tenant_id") ?? user.FindFirstValue("tenant_id");
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
            if (!Guid.TryParse(claim, out var tenantId) || string.IsNullOrWhiteSpace(userId))
                return null;

            return new TenantIdentity(tenantId, userId);
        }

        if (!Enabled || TenantId is not Guid configuredTenant || string.IsNullOrWhiteSpace(UserId))
            return null;

        return new TenantIdentity(configuredTenant, UserId);
    }
}

internal readonly record struct TenantIdentity(Guid TenantId, string UserId);
