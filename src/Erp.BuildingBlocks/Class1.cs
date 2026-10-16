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

public enum TaxCode
{
    Standard19 = 0,
    Reduced7 = 1,
    Zero = 2
}

public static class Money
{
    // Commercial rounding: .NET's default MidpointRounding.ToEven would turn 0.125 into 0.12.
    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}

public sealed record AddressSnapshot(
    string Name,
    string Street,
    string PostalCode,
    string City,
    string CountryCode,
    string? AdditionalLine);
