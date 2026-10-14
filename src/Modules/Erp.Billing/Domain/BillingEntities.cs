using Erp.BuildingBlocks;

namespace Erp.Billing.Domain;

public sealed class Delivery : ITenantEntity
{
    private Delivery()
    {
    }

    public Delivery(Guid tenantId, Guid salesOrderId, string number, AddressSnapshot shipTo)
    {
        ArgumentNullException.ThrowIfNull(shipTo);
        if (string.IsNullOrWhiteSpace(shipTo.Name)) throw new ArgumentException("Ship-to name is required.", nameof(shipTo));
        if (string.IsNullOrWhiteSpace(shipTo.Street)) throw new ArgumentException("Ship-to street is required.", nameof(shipTo));
        if (string.IsNullOrWhiteSpace(shipTo.PostalCode)) throw new ArgumentException("Ship-to postal code is required.", nameof(shipTo));
        if (string.IsNullOrWhiteSpace(shipTo.City)) throw new ArgumentException("Ship-to city is required.", nameof(shipTo));
        if (string.IsNullOrWhiteSpace(shipTo.CountryCode)) throw new ArgumentException("Ship-to country is required.", nameof(shipTo));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        SalesOrderId = salesOrderId;
        Number = number;
        ShipToName = shipTo.Name;
        ShipToStreet = shipTo.Street;
        ShipToPostalCode = shipTo.PostalCode;
        ShipToCity = shipTo.City;
        ShipToCountryCode = shipTo.CountryCode;
        ShipToAdditionalLine = shipTo.AdditionalLine;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SalesOrderId { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public string ShipToName { get; private set; } = string.Empty;
    public string ShipToStreet { get; private set; } = string.Empty;
    public string ShipToPostalCode { get; private set; } = string.Empty;
    public string ShipToCity { get; private set; } = string.Empty;
    public string ShipToCountryCode { get; private set; } = string.Empty;
    public string? ShipToAdditionalLine { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class Invoice : ITenantEntity
{
    private Invoice()
    {
    }

    public Invoice(Guid tenantId, Guid salesOrderId, string number, decimal netAmount)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        SalesOrderId = salesOrderId;
        Number = number;
        NetAmount = netAmount;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SalesOrderId { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public decimal NetAmount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
