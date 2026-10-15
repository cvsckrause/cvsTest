using Erp.BuildingBlocks;
using Erp.Persistence;
using Erp.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api;

internal interface ITenantMemberships
{
    Task<IReadOnlyList<TenantMembership>> ListAsync(string userId, CancellationToken cancellationToken);
}

internal sealed record ErpConnection(string Value);

internal sealed class EfTenantMemberships(ErpConnection connection) : ITenantMemberships
{
    public async Task<IReadOnlyList<TenantMembership>> ListAsync(string userId, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql(connection.Value)
            .Options;
        await using var db = new ErpDbContext(options, new TenantContext(Guid.Empty, "membership"));
        return await db.TenantMemberships.AsNoTracking()
            .Where(membership => membership.UserId == userId)
            .ToListAsync(cancellationToken);
    }
}
