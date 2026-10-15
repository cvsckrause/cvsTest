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

    public TenantIdentity? Resolve()
    {
        if (!Enabled || TenantId is not Guid configuredTenant || string.IsNullOrWhiteSpace(UserId))
            return null;

        return new TenantIdentity(configuredTenant, UserId);
    }
}

internal readonly record struct TenantIdentity(Guid TenantId, string UserId);
