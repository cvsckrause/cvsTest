using Erp.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api;

internal sealed record MandantSummary(Guid TenantId, long Number, string Firmenname);

internal interface IMandantDirectory
{
    Task<IReadOnlyDictionary<Guid, MandantSummary>> FindActiveAsync(
        IReadOnlyCollection<Guid> tenantIds,
        CancellationToken cancellationToken);
}

internal sealed class EfMandantDirectory(ErpConnection connection) : IMandantDirectory
{
    public async Task<IReadOnlyDictionary<Guid, MandantSummary>> FindActiveAsync(
        IReadOnlyCollection<Guid> tenantIds,
        CancellationToken cancellationToken)
    {
        if (tenantIds.Count == 0)
        {
            return new Dictionary<Guid, MandantSummary>();
        }

        await using var db = connection.OpenRegistry("mandant-directory");
        return await db.Mandanten.AsNoTracking()
            .Where(mandant => tenantIds.Contains(mandant.Id) && mandant.Status == MandantStatus.Active)
            .Select(mandant => new MandantSummary(mandant.Id, mandant.Number, mandant.Firmenname))
            .ToDictionaryAsync(mandant => mandant.TenantId, cancellationToken);
    }
}
