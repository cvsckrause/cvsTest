using Erp.BuildingBlocks;

namespace Erp.MasterData.Domain;

public sealed class Customer : ITenantEntity
{
    private Customer()
    {
    }

    private Customer(Guid tenantId, string number, string name)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(number)) throw new ArgumentException("Customer number is required.", nameof(number));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Customer name is required.", nameof(name));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        Number = number.Trim().ToUpperInvariant();
        Name = name.Trim();
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;

    public static Customer Create(Guid tenantId, string number, string name) => new(tenantId, number, name);
}

public sealed class Article : ITenantEntity
{
    private Article()
    {
    }

    private Article(Guid tenantId, string sku, string name, decimal salesPrice)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(sku)) throw new ArgumentException("SKU is required.", nameof(sku));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Article name is required.", nameof(name));
        if (salesPrice < 0) throw new ArgumentOutOfRangeException(nameof(salesPrice));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        Sku = sku.Trim().ToUpperInvariant();
        Name = name.Trim();
        SalesPrice = salesPrice;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public decimal SalesPrice { get; private set; }

    public static Article Create(Guid tenantId, string sku, string name, decimal salesPrice) =>
        new(tenantId, sku, name, salesPrice);
}
