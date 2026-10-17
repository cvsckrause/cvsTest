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

public static class TaxCodes
{
    public const string InvalidMessage = "Tax code must be Standard19, Reduced7, or Zero.";

    public static void RequireDefined(TaxCode taxCode, string parameter)
    {
        if (!Enum.IsDefined(taxCode)) throw new ArgumentException(InvalidMessage, parameter);
    }
}

public static class Money
{
    // Commercial rounding: .NET's default MidpointRounding.ToEven would turn 0.125 into 0.12.
    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}

public static class BusinessDate
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    public static DateOnly Berlin(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime);
}

public sealed record AddressSnapshot(
    string Name,
    string Street,
    string PostalCode,
    string City,
    string CountryCode,
    string? AdditionalLine);

public sealed record SellerSnapshot(
    string Name,
    string Street,
    string PostalCode,
    string City,
    string CountryCode,
    string? AdditionalLine,
    string? Steuernummer,
    string? UstIdNr);
