using Erp.BuildingBlocks;

namespace Erp.Sales.Domain;

public enum SalesOrderStatus
{
    Draft = 0,
    Released = 1,
    Cancelled = 2
}

public sealed class SalesOrder : ITenantEntity
{
    private readonly List<SalesOrderLine> _lines = [];

    private SalesOrder()
    {
    }

    private SalesOrder(
        Guid tenantId,
        Guid customerId,
        string orderNumber,
        Guid shipToAddressId,
        AddressSnapshot shipTo,
        string warehouseCode)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        if (customerId == Guid.Empty) throw new ArgumentException("Customer is required.", nameof(customerId));
        if (shipToAddressId == Guid.Empty) throw new ArgumentException("Ship-to address is required.", nameof(shipToAddressId));
        if (string.IsNullOrWhiteSpace(orderNumber)) throw new ArgumentException("Order number is required.", nameof(orderNumber));
        ArgumentNullException.ThrowIfNull(shipTo);
        if (string.IsNullOrWhiteSpace(shipTo.Name)) throw new ArgumentException("Ship-to name is required.", nameof(shipTo));
        if (string.IsNullOrWhiteSpace(shipTo.Street)) throw new ArgumentException("Ship-to street is required.", nameof(shipTo));
        if (string.IsNullOrWhiteSpace(shipTo.PostalCode)) throw new ArgumentException("Ship-to postal code is required.", nameof(shipTo));
        if (string.IsNullOrWhiteSpace(shipTo.City)) throw new ArgumentException("Ship-to city is required.", nameof(shipTo));
        if (string.IsNullOrWhiteSpace(shipTo.CountryCode)) throw new ArgumentException("Ship-to country is required.", nameof(shipTo));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        CustomerId = customerId;
        OrderNumber = orderNumber.Trim().ToUpperInvariant();
        ShipToAddressId = shipToAddressId;
        ShipToName = shipTo.Name;
        ShipToStreet = shipTo.Street;
        ShipToPostalCode = shipTo.PostalCode;
        ShipToCity = shipTo.City;
        ShipToCountryCode = shipTo.CountryCode;
        ShipToAdditionalLine = shipTo.AdditionalLine;
        WarehouseCode = NormalizeWarehouse(warehouseCode);
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid ShipToAddressId { get; private set; }
    public string ShipToName { get; private set; } = string.Empty;
    public string ShipToStreet { get; private set; } = string.Empty;
    public string ShipToPostalCode { get; private set; } = string.Empty;
    public string ShipToCity { get; private set; } = string.Empty;
    public string ShipToCountryCode { get; private set; } = string.Empty;
    public string? ShipToAdditionalLine { get; private set; }
    public string OrderNumber { get; private set; } = string.Empty;
    public string WarehouseCode { get; private set; } = string.Empty;
    public SalesOrderStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReleasedAt { get; private set; }
    // Loaded rows arrive in no particular order; Position is the order the lines were captured in.
    public IReadOnlyCollection<SalesOrderLine> Lines => _lines.OrderBy(line => line.Position).ToArray();
    public decimal NetAmount => _lines.Sum(line => line.NetAmount);

    public AddressSnapshot ShipToSnapshot =>
        new(ShipToName, ShipToStreet, ShipToPostalCode, ShipToCity, ShipToCountryCode, ShipToAdditionalLine);

    public static SalesOrder Create(
        Guid tenantId,
        Guid customerId,
        string orderNumber,
        Guid shipToAddressId,
        AddressSnapshot shipTo,
        string warehouseCode = "MAIN") =>
        new(tenantId, customerId, orderNumber, shipToAddressId, shipTo, warehouseCode);

    public void AddLine(Guid articleId, string sku, string description, TaxCode taxCode, decimal quantity, decimal unitPrice)
    {
        EnsureDraft();
        _lines.Add(SalesOrderLine.Create(Id, TenantId, _lines.Count + 1, articleId, sku, description, taxCode, quantity, unitPrice));
    }

    public SalesOrderReleased Release()
    {
        if (Status == SalesOrderStatus.Cancelled)
        {
            throw new InvalidOperationException("Cancelled orders cannot be released.");
        }

        EnsureDraft();
        if (_lines.Count == 0) throw new InvalidOperationException("An order requires at least one line.");

        Status = SalesOrderStatus.Released;
        ReleasedAt = DateTimeOffset.UtcNow;

        return new SalesOrderReleased(
            Guid.NewGuid(),
            ReleasedAt.Value,
            TenantId,
            Id,
            OrderNumber,
            NetAmount,
            ShipToSnapshot,
            Lines.Select(line => new ReleasedSalesOrderLine(line.ArticleId, line.Sku, line.Quantity)).ToArray());
    }

    public SalesOrderCancelled Cancel()
    {
        if (Status == SalesOrderStatus.Cancelled)
        {
            throw new InvalidOperationException("Cancelled orders cannot be changed.");
        }

        Status = SalesOrderStatus.Cancelled;
        var occurredAt = DateTimeOffset.UtcNow;
        return new SalesOrderCancelled(
            Guid.NewGuid(),
            occurredAt,
            TenantId,
            Id,
            OrderNumber,
            WarehouseCode,
            Lines.Select(line => new ReleasedSalesOrderLine(line.ArticleId, line.Sku, line.Quantity)).ToArray());
    }

    private void EnsureDraft()
    {
        if (Status != SalesOrderStatus.Draft)
        {
            throw new InvalidOperationException("Released orders cannot be changed.");
        }
    }

    private static string NormalizeWarehouse(string warehouseCode)
    {
        var normalized = string.IsNullOrWhiteSpace(warehouseCode)
            ? "MAIN"
            : warehouseCode.Trim().ToUpperInvariant();
        if (normalized.Length > 40)
        {
            throw new ArgumentException("Warehouse code is too long.", nameof(warehouseCode));
        }

        return normalized;
    }
}

public sealed class SalesOrderLine : ITenantEntity
{
    private SalesOrderLine()
    {
    }

    private SalesOrderLine(
        Guid salesOrderId,
        Guid tenantId,
        int position,
        Guid articleId,
        string sku,
        string description,
        TaxCode taxCode,
        decimal quantity,
        decimal unitPrice)
    {
        if (articleId == Guid.Empty) throw new ArgumentException("Article is required.", nameof(articleId));
        if (string.IsNullOrWhiteSpace(sku)) throw new ArgumentException("SKU is required.", nameof(sku));
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Line description is required.", nameof(description));
        TaxCodes.RequireDefined(taxCode, nameof(taxCode));
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (unitPrice < 0) throw new ArgumentOutOfRangeException(nameof(unitPrice));

        Id = Guid.NewGuid();
        SalesOrderId = salesOrderId;
        TenantId = tenantId;
        Position = position;
        ArticleId = articleId;
        Sku = sku.Trim().ToUpperInvariant();
        Description = description.Trim();
        TaxCode = taxCode;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public Guid Id { get; private set; }
    public Guid SalesOrderId { get; private set; }
    public Guid TenantId { get; private set; }
    public int Position { get; private set; }
    public Guid ArticleId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public TaxCode TaxCode { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal NetAmount => Money.Round(Quantity * UnitPrice);

    internal static SalesOrderLine Create(
        Guid salesOrderId,
        Guid tenantId,
        int position,
        Guid articleId,
        string sku,
        string description,
        TaxCode taxCode,
        decimal quantity,
        decimal unitPrice) =>
        new(salesOrderId, tenantId, position, articleId, sku, description, taxCode, quantity, unitPrice);
}

public sealed record ReleasedSalesOrderLine(Guid ArticleId, string Sku, decimal Quantity);

public sealed record SalesOrderCancelled(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid TenantId,
    Guid SalesOrderId,
    string SalesOrderNumber,
    string WarehouseCode,
    IReadOnlyCollection<ReleasedSalesOrderLine> Lines) : IDomainEvent;

public sealed record SalesOrderReleased(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid TenantId,
    Guid SalesOrderId,
    string SalesOrderNumber,
    decimal NetAmount,
    AddressSnapshot ShipTo,
    IReadOnlyCollection<ReleasedSalesOrderLine> Lines) : IDomainEvent;
