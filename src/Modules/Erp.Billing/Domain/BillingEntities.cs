using Erp.BuildingBlocks;

namespace Erp.Billing.Domain;

public sealed class Delivery : ITenantEntity
{
    private Delivery()
    {
    }

    public Delivery(Guid tenantId, Guid salesOrderId, string number)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        SalesOrderId = salesOrderId;
        Number = number;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SalesOrderId { get; private set; }
    public string Number { get; private set; } = string.Empty;
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
