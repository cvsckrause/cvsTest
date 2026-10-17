using Erp.BuildingBlocks;
using Erp.MasterData.Domain;
using Erp.Sales.Domain;

namespace Erp.Domain.Tests;

public sealed class SalesOrderBillToTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly AddressSnapshot ShipTo = new("Lager Nord", "Kai 3", "20457", "Hamburg", "DE", null);

    [Fact]
    public void Bill_to_keeps_the_buyer_and_address_at_creation()
    {
        var billing = Address.Create(TenantId, "Buchhaltung", "Rathausmarkt 1", "20095", "Hamburg");
        var order = Create(new BillToSnapshot(" Nordrad GmbH ", "10000", billing.Id, billing.ToSnapshot()));

        billing.Update("Buchhaltung", "Neue Straße 9", "20099", "Hamburg", "DE", null);

        Assert.Equal(
            new BillToSnapshot("Nordrad GmbH", "10000", billing.Id, new AddressSnapshot("Buchhaltung", "Rathausmarkt 1", "20095", "Hamburg", "DE", null)),
            order.BillTo);
    }

    [Fact]
    public void Bill_to_without_a_billing_address_has_no_address_id()
    {
        var order = Create(new BillToSnapshot("Nordrad GmbH", "10000", null, ShipTo));

        Assert.Null(order.BillToAddressId);
        Assert.Equal(ShipTo, order.BillTo.Address);
    }

    [Fact]
    public void Bill_to_requires_buyer_and_a_complete_address()
    {
        Assert.Throws<ArgumentException>(() => Create(new BillToSnapshot(" ", "10000", null, ShipTo)));
        Assert.Throws<ArgumentException>(() => Create(new BillToSnapshot("Nordrad GmbH", " ", null, ShipTo)));
        Assert.Throws<ArgumentException>(() => Create(new BillToSnapshot("Nordrad GmbH", "10000", Guid.Empty, ShipTo)));
        Assert.Throws<ArgumentException>(() => Create(new BillToSnapshot("Nordrad GmbH", "10000", null, ShipTo with { Street = " " })));
    }

    private static SalesOrder Create(BillToSnapshot billTo) =>
        SalesOrder.Create(TenantId, Guid.NewGuid(), "SO-BILL", Guid.NewGuid(), ShipTo, billTo);
}
