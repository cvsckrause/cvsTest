using Erp.BuildingBlocks;
using Erp.Inventory.Domain;
using Erp.MasterData.Domain;
using Erp.Sales.Domain;

namespace Erp.Domain.Tests;

public sealed class TradingFlowTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CustomerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ArticleId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ShipToAddressId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static AddressSnapshot SampleShipTo =>
        new("Nordrad GmbH", "Hafenstraße 12", "20095", "Hamburg", "DE", null);

    [Fact]
    public void Releasing_order_emits_stable_event_and_locks_order()
    {
        var order = SalesOrder.Create(TenantId, CustomerId, "SO-10001", ShipToAddressId, SampleShipTo);
        order.AddLine(ArticleId, "BIKE-001", "Cityrad", TaxCode.Standard19, 2, 799m);

        var released = order.Release();

        Assert.Equal(SalesOrderStatus.Released, order.Status);
        Assert.Equal(order.Id, released.SalesOrderId);
        Assert.Equal(TenantId, released.TenantId);
        Assert.Equal("SO-10001", released.SalesOrderNumber);
        Assert.Equal(1598m, order.NetAmount);
        Assert.Equal(SampleShipTo, released.ShipTo);
        Assert.Throws<InvalidOperationException>(() => order.AddLine(ArticleId, "BIKE-001", "Cityrad", TaxCode.Standard19, 1, 799m));
    }

    [Fact]
    public void Releasing_order_twice_is_rejected()
    {
        var order = SalesOrder.Create(TenantId, CustomerId, "SO-10002", ShipToAddressId, SampleShipTo);
        order.AddLine(ArticleId, "BIKE-001", "Cityrad", TaxCode.Standard19, 1, 799m);
        order.Release();

        Assert.Throws<InvalidOperationException>(() => order.Release());
    }

    [Fact]
    public void Sales_order_requires_ship_to_snapshot()
    {
        Assert.Throws<ArgumentException>(() =>
            SalesOrder.Create(TenantId, CustomerId, "SO-10003", Guid.Empty, SampleShipTo));
        Assert.Throws<ArgumentException>(() =>
            SalesOrder.Create(TenantId, CustomerId, "SO-10004", ShipToAddressId, SampleShipTo with { Name = " " }));
    }

    [Fact]
    public void Order_snapshot_is_independent_of_later_address_edits()
    {
        var address = Address.Create(TenantId, "Nordrad GmbH", "Hafenstraße 12", "20095", "Hamburg");
        var order = SalesOrder.Create(TenantId, CustomerId, "SO-10005", address.Id, address.ToSnapshot());
        order.AddLine(ArticleId, "BIKE-001", "Cityrad", TaxCode.Standard19, 1, 799m);

        address.Update("Nordrad GmbH", "Neue Straße 1", "20099", "Hamburg", "DE", null);

        Assert.Equal("Hafenstraße 12", order.ShipToStreet);
        Assert.Equal("20095", order.ShipToPostalCode);
        Assert.Equal("Neue Straße 1", address.Street);
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

    [Fact]
    public void Goods_issue_reduces_on_hand_and_reserved()
    {
        var stock = StockBalance.Create(TenantId, ArticleId, "MAIN", 10);
        stock.Reserve(3);

        stock.Issue(3);

        Assert.Equal(7, stock.OnHand);
        Assert.Equal(0, stock.Reserved);
    }

    [Fact]
    public void Goods_issue_can_consume_part_of_the_reservation()
    {
        var stock = StockBalance.Create(TenantId, ArticleId, "MAIN", 10);
        stock.Reserve(3);

        stock.Issue(1);

        Assert.Equal(9, stock.OnHand);
        Assert.Equal(2, stock.Reserved);
    }

    [Fact]
    public void Goods_issue_rejects_non_positive_or_unreserved_quantity()
    {
        var stock = StockBalance.Create(TenantId, ArticleId, "MAIN", 10);
        stock.Reserve(3);

        Assert.Throws<ArgumentOutOfRangeException>(() => stock.Issue(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => stock.Issue(-1));
        Assert.Throws<InvalidOperationException>(() => stock.Issue(4));
        Assert.Equal(10, stock.OnHand);
        Assert.Equal(3, stock.Reserved);
    }

    [Fact]
    public void Goods_issue_movement_records_immutable_trail_fields()
    {
        var occurredAt = DateTimeOffset.Parse("2026-10-15T11:00:00Z");
        var movement = StockMovement.GoodsIssue(
            TenantId,
            ArticleId,
            "main",
            3,
            "DEL-SO-1",
            "erp-worker",
            occurredAt);

        Assert.Equal("MAIN", movement.WarehouseCode);
        Assert.Equal(3, movement.Quantity);
        Assert.Equal(StockMovementType.GoodsIssue, movement.MovementType);
        Assert.Equal("DEL-SO-1", movement.Reference);
        Assert.Equal("erp-worker", movement.CreatedBy);
        Assert.Equal(occurredAt, movement.OccurredAt);
    }
}

public sealed class MasterDataTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Address_create_rejects_incomplete_postal_fields()
    {
        Assert.Throws<ArgumentException>(() => Address.Create(TenantId, "", "Street 1", "20095", "Hamburg"));
        Assert.Throws<ArgumentException>(() => Address.Create(TenantId, "Name", "", "20095", "Hamburg"));
        Assert.Throws<ArgumentException>(() => Address.Create(TenantId, "Name", "Street 1", "", "Hamburg"));
        Assert.Throws<ArgumentException>(() => Address.Create(TenantId, "Name", "Street 1", "20095", ""));
        Assert.Throws<ArgumentException>(() => Address.Create(TenantId, "Name", "Street 1", "20095", "Hamburg", "D"));
    }

    [Fact]
    public void Customer_address_link_requires_ids()
    {
        var customerId = Guid.NewGuid();
        var addressId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() =>
            CustomerAddress.Link(TenantId, Guid.Empty, addressId, AddressRole.Shipping));
        Assert.Throws<ArgumentException>(() =>
            CustomerAddress.Link(TenantId, customerId, Guid.Empty, AddressRole.Billing));

        var link = CustomerAddress.Link(TenantId, customerId, addressId, AddressRole.Shipping);
        Assert.Equal(AddressRole.Shipping, link.Role);
        Assert.Equal(customerId, link.CustomerId);
        Assert.Equal(addressId, link.AddressId);
    }

    [Fact]
    public void Article_update_changes_name_and_price_but_not_sku()
    {
        var article = Article.Create(TenantId, "BIKE-001", "Cityrad Nord", 799m);
        var sku = article.Sku;

        article.Update("Cityrad Süd", 849m, TaxCode.Standard19);

        Assert.Equal(sku, article.Sku);
        Assert.Equal("Cityrad Süd", article.Name);
        Assert.Equal(849m, article.SalesPrice);
        Assert.Throws<ArgumentException>(() => article.Update(" ", 10m, TaxCode.Standard19));
        Assert.Throws<ArgumentOutOfRangeException>(() => article.Update("Valid", -1m, TaxCode.Standard19));
    }
}
