using Erp.BuildingBlocks;
using Erp.Persistence;
using Erp.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.Api;

internal interface ITenantMemberships
{
    Task<IReadOnlyList<TenantMembership>> ListAsync(string userId, CancellationToken cancellationToken);

    Task EnsureDefaultAsync(string userId, Guid tenantId, CancellationToken cancellationToken);
}

internal sealed record ErpConnection(string Value)
{
    // Registry tables are read before a tenant is verified, so this context skips the tenant interceptor.
    public ErpDbContext OpenRegistry(string purpose)
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql(Value)
            .Options;
        return new ErpDbContext(options, new TenantContext(Guid.Empty, purpose));
    }
}

internal sealed class EfTenantMemberships(ErpConnection connection) : ITenantMemberships
{
    public async Task<IReadOnlyList<TenantMembership>> ListAsync(string userId, CancellationToken cancellationToken)
    {
        await using var db = Create();
        return await db.TenantMemberships.AsNoTracking()
            .Where(membership => membership.UserId == userId)
            .ToListAsync(cancellationToken);
    }

    public async Task EnsureDefaultAsync(string userId, Guid tenantId, CancellationToken cancellationToken)
    {
        await using var db = Create();
        var exists = await db.TenantMemberships.AsNoTracking()
            .AnyAsync(membership => membership.UserId == userId, cancellationToken);
        if (exists)
        {
            return;
        }

        db.TenantMemberships.Add(TenantMembership.Grant(userId, tenantId, isDefault: true));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
        }
    }

    private ErpDbContext Create() => connection.OpenRegistry("membership");
}
