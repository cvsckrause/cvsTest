using Erp.BuildingBlocks;
using Erp.Inventory.Domain;
using Erp.Sales.Domain;

namespace Erp.Domain.Tests;

public sealed class SalesOrderCancellationTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CustomerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ArticleId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ShipToAddressId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static AddressSnapshot SampleShipTo =>
        new("Nordrad GmbH", "Hafenstraße 12", "20095", "Hamburg", "DE", null);

    [Fact]
    public void Cancelling_a_released_order_keeps_lines_and_rejects_a_second_cancel()
    {
        var order = SalesOrder.Create(TenantId, CustomerId, "SO-20001", ShipToAddressId, SampleShipTo);
        order.AddLine(ArticleId, "BIKE-001", "Cityrad", TaxCode.Standard19, 2, 10m);
        order.Release();

        var cancelled = order.Cancel();

        Assert.Equal(SalesOrderStatus.Cancelled, order.Status);
        Assert.Equal(order.Id, cancelled.SalesOrderId);
        Assert.Equal(20m, order.NetAmount);
        Assert.Equal(2, order.Lines.Single().Quantity);
        Assert.Throws<InvalidOperationException>(() => order.Cancel());
    }

    [Fact]
    public void Cancelling_a_draft_leaves_released_at_unset()
    {
        var order = SalesOrder.Create(TenantId, CustomerId, "SO-20002", ShipToAddressId, SampleShipTo);
        order.AddLine(ArticleId, "BIKE-001", "Cityrad", TaxCode.Standard19, 1, 10m);

        order.Cancel();

        Assert.Equal(SalesOrderStatus.Cancelled, order.Status);
        Assert.Null(order.ReleasedAt);
    }

    [Fact]
    public void Releasing_a_cancelled_order_is_rejected()
    {
        var order = SalesOrder.Create(TenantId, CustomerId, "SO-20003", ShipToAddressId, SampleShipTo);
        order.AddLine(ArticleId, "BIKE-001", "Cityrad", TaxCode.Standard19, 1, 10m);
        order.Cancel();

        Assert.Throws<InvalidOperationException>(() => order.Release());
    }

    [Fact]
    public void Blank_warehouse_defaults_to_main_and_codes_are_normalized()
    {
        var blank = SalesOrder.Create(TenantId, CustomerId, "SO-20004", ShipToAddressId, SampleShipTo, " ");
        var named = SalesOrder.Create(TenantId, CustomerId, "SO-20005", ShipToAddressId, SampleShipTo, " neben ");

        Assert.Equal("MAIN", blank.WarehouseCode);
        Assert.Equal("NEBEN", named.WarehouseCode);
    }

    [Fact]
    public void Unreserve_returns_reserved_quantity_without_changing_on_hand()
    {
        var stock = StockBalance.Create(TenantId, ArticleId, "MAIN", 10);
        stock.Reserve(4);

        stock.ReleaseReservation(4);

        Assert.Equal(10, stock.OnHand);
        Assert.Equal(0, stock.Reserved);
    }

    [Fact]
    public void Unreserve_rejects_non_positive_and_excess_quantity()
    {
        var stock = StockBalance.Create(TenantId, ArticleId, "MAIN", 10);
        stock.Reserve(4);

        Assert.Throws<ArgumentOutOfRangeException>(() => stock.ReleaseReservation(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => stock.ReleaseReservation(-1));
        Assert.Throws<InvalidOperationException>(() => stock.ReleaseReservation(5));
        Assert.Equal(4, stock.Reserved);
    }
}
