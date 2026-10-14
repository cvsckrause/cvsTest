using Erp.Inventory.Domain;
using Erp.Sales.Domain;

namespace Erp.Domain.Tests;

public sealed class TradingFlowTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CustomerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ArticleId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void Releasing_order_emits_stable_event_and_locks_order()
    {
        var order = SalesOrder.Create(TenantId, CustomerId, "SO-10001");
        order.AddLine(ArticleId, "BIKE-001", 2, 799m);

        var released = order.Release();

        Assert.Equal(SalesOrderStatus.Released, order.Status);
        Assert.Equal(order.Id, released.SalesOrderId);
        Assert.Equal(TenantId, released.TenantId);
        Assert.Equal("SO-10001", released.SalesOrderNumber);
        Assert.Equal(1598m, order.NetAmount);
        Assert.Throws<InvalidOperationException>(() => order.AddLine(ArticleId, "BIKE-001", 1, 799m));
    }

    [Fact]
    public void Releasing_order_twice_is_rejected()
    {
        var order = SalesOrder.Create(TenantId, CustomerId, "SO-10002");
        order.AddLine(ArticleId, "BIKE-001", 1, 799m);
        order.Release();

        Assert.Throws<InvalidOperationException>(() => order.Release());
    }

    [Fact]
    public void Stock_reservation_reduces_available_not_on_hand()
    {
        var stock = StockBalance.Create(TenantId, ArticleId, "MAIN", 10);

        stock.Reserve(4);

        Assert.Equal(10, stock.OnHand);
        Assert.Equal(4, stock.Reserved);
        Assert.Equal(6, stock.Available);
    }

    [Fact]
    public void Stock_reservation_cannot_exceed_available_quantity()
    {
        var stock = StockBalance.Create(TenantId, ArticleId, "MAIN", 3);

        Assert.Throws<InvalidOperationException>(() => stock.Reserve(4));
    }

    [Fact]
    public void Goods_receipt_increases_on_hand_without_changing_reserved()
    {
        var stock = StockBalance.Create(TenantId, ArticleId, "MAIN", 10);
        stock.Reserve(3);

        stock.Receive(5);

        Assert.Equal(15, stock.OnHand);
        Assert.Equal(3, stock.Reserved);
        Assert.Equal(12, stock.Available);
    }

    [Fact]
    public void Goods_receipt_rejects_non_positive_quantity()
    {
        var stock = StockBalance.Create(TenantId, ArticleId, "MAIN", 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => stock.Receive(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => stock.Receive(-1));
    }

    [Fact]
    public void Goods_receipt_movement_records_immutable_trail_fields()
    {
        var occurredAt = DateTimeOffset.Parse("2026-10-14T10:00:00Z");
        var movement = StockMovement.GoodsReceipt(
            TenantId,
            ArticleId,
            "main",
            5,
            "WE-1001",
            "local-developer",
            occurredAt);

        Assert.Equal(TenantId, movement.TenantId);
        Assert.Equal(ArticleId, movement.ArticleId);
        Assert.Equal("MAIN", movement.WarehouseCode);
        Assert.Equal(5, movement.Quantity);
        Assert.Equal(StockMovementType.GoodsReceipt, movement.MovementType);
        Assert.Equal("WE-1001", movement.Reference);
        Assert.Equal("local-developer", movement.CreatedBy);
        Assert.Equal(occurredAt, movement.OccurredAt);
    }
}
