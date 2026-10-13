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
}
