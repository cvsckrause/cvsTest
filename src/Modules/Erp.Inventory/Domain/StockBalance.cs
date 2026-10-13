using Erp.BuildingBlocks;

namespace Erp.Inventory.Domain;

public sealed class StockBalance : ITenantEntity
{
    private StockBalance()
    {
    }

    private StockBalance(Guid tenantId, Guid articleId, string warehouseCode, decimal onHand)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        if (articleId == Guid.Empty) throw new ArgumentException("Article is required.", nameof(articleId));
        if (string.IsNullOrWhiteSpace(warehouseCode)) throw new ArgumentException("Warehouse is required.", nameof(warehouseCode));
        if (onHand < 0) throw new ArgumentOutOfRangeException(nameof(onHand));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        ArticleId = articleId;
        WarehouseCode = warehouseCode.Trim().ToUpperInvariant();
        OnHand = onHand;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ArticleId { get; private set; }
    public string WarehouseCode { get; private set; } = string.Empty;
    public decimal OnHand { get; private set; }
    public decimal Reserved { get; private set; }
    public decimal Available => OnHand - Reserved;

    public static StockBalance Create(Guid tenantId, Guid articleId, string warehouseCode, decimal onHand) =>
        new(tenantId, articleId, warehouseCode, onHand);

    public void Reserve(decimal quantity)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (quantity > Available) throw new InvalidOperationException("Insufficient available stock.");

        Reserved += quantity;
    }
}
