namespace Erp.BuildingBlocks;

public interface ITenantEntity
{
    Guid TenantId { get; }
}

public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}

public interface ITenantContext
{
    Guid TenantId { get; }
    string UserId { get; }
}

public sealed record TenantContext(Guid TenantId, string UserId) : ITenantContext;
