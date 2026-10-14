using Erp.BuildingBlocks;

namespace Erp.Inventory.Domain;

public enum StockMovementType
{
    GoodsReceipt = 1
}

public sealed class StockMovement : ITenantEntity
{
    private StockMovement()
    {
    }

    private StockMovement(
        Guid tenantId,
        Guid articleId,
        string warehouseCode,
        decimal quantity,
        StockMovementType movementType,
        string? reference,
        string createdBy,
        DateTimeOffset occurredAt)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        if (articleId == Guid.Empty) throw new ArgumentException("Article is required.", nameof(articleId));
        if (string.IsNullOrWhiteSpace(warehouseCode)) throw new ArgumentException("Warehouse is required.", nameof(warehouseCode));
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (string.IsNullOrWhiteSpace(createdBy)) throw new ArgumentException("Actor is required.", nameof(createdBy));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        ArticleId = articleId;
        WarehouseCode = warehouseCode.Trim().ToUpperInvariant();
        Quantity = quantity;
        MovementType = movementType;
        Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        CreatedBy = createdBy.Trim();
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ArticleId { get; private set; }
    public string WarehouseCode { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public StockMovementType MovementType { get; private set; }
    public string? Reference { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }

    public static StockMovement GoodsReceipt(
        Guid tenantId,
        Guid articleId,
        string warehouseCode,
        decimal quantity,
        string? reference,
        string createdBy,
        DateTimeOffset occurredAt) =>
        new(tenantId, articleId, warehouseCode, quantity, StockMovementType.GoodsReceipt, reference, createdBy, occurredAt);
}
