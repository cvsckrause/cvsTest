using Erp.BuildingBlocks;

namespace Erp.Sales.Domain;

public enum SalesOrderStatus
{
    Draft = 0,
    Released = 1
}

public sealed class SalesOrder : ITenantEntity
{
    private readonly List<SalesOrderLine> _lines = [];

    private SalesOrder()
    {
    }

    private SalesOrder(Guid tenantId, Guid customerId, string orderNumber)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        if (customerId == Guid.Empty) throw new ArgumentException("Customer is required.", nameof(customerId));
        if (string.IsNullOrWhiteSpace(orderNumber)) throw new ArgumentException("Order number is required.", nameof(orderNumber));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        CustomerId = customerId;
        OrderNumber = orderNumber.Trim().ToUpperInvariant();
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string OrderNumber { get; private set; } = string.Empty;
    public SalesOrderStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReleasedAt { get; private set; }
    public IReadOnlyCollection<SalesOrderLine> Lines => _lines;
    public decimal NetAmount => _lines.Sum(line => line.Quantity * line.UnitPrice);

    public static SalesOrder Create(Guid tenantId, Guid customerId, string orderNumber) =>
        new(tenantId, customerId, orderNumber);

    public void AddLine(Guid articleId, string sku, decimal quantity, decimal unitPrice)
    {
        EnsureDraft();
        _lines.Add(SalesOrderLine.Create(Id, TenantId, articleId, sku, quantity, unitPrice));
    }

    public SalesOrderReleased Release()
    {
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
            _lines.Select(line => new ReleasedSalesOrderLine(line.ArticleId, line.Sku, line.Quantity)).ToArray());
    }

    private void EnsureDraft()
    {
        if (Status != SalesOrderStatus.Draft)
        {
            throw new InvalidOperationException("Released orders cannot be changed.");
        }
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
        Guid articleId,
        string sku,
        decimal quantity,
        decimal unitPrice)
    {
        if (articleId == Guid.Empty) throw new ArgumentException("Article is required.", nameof(articleId));
        if (string.IsNullOrWhiteSpace(sku)) throw new ArgumentException("SKU is required.", nameof(sku));
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (unitPrice < 0) throw new ArgumentOutOfRangeException(nameof(unitPrice));

        Id = Guid.NewGuid();
        SalesOrderId = salesOrderId;
        TenantId = tenantId;
        ArticleId = articleId;
        Sku = sku.Trim().ToUpperInvariant();
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public Guid Id { get; private set; }
    public Guid SalesOrderId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ArticleId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }

    internal static SalesOrderLine Create(
        Guid salesOrderId,
        Guid tenantId,
        Guid articleId,
        string sku,
        decimal quantity,
        decimal unitPrice) =>
        new(salesOrderId, tenantId, articleId, sku, quantity, unitPrice);
}

public sealed record ReleasedSalesOrderLine(Guid ArticleId, string Sku, decimal Quantity);

public sealed record SalesOrderReleased(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid TenantId,
    Guid SalesOrderId,
    string SalesOrderNumber,
    IReadOnlyCollection<ReleasedSalesOrderLine> Lines) : IDomainEvent;
