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
