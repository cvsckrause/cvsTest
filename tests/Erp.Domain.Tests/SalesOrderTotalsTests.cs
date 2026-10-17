using Erp.BuildingBlocks;
using Erp.Sales.Domain;

namespace Erp.Domain.Tests;

public sealed class SalesOrderTotalsTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly AddressSnapshot ShipTo = new("Nordrad GmbH", "Hafenstraße 12", "20095", "Hamburg", "DE", null);

    [Fact]
    public void Order_net_is_the_sum_of_rounded_line_nets()
    {
        var order = NewOrder();
        order.AddLine(Guid.NewGuid(), "BOLT-1", "Schraube", TaxCode.Standard19, 3, 0.3335m);
        order.AddLine(Guid.NewGuid(), "NUT-1", "Mutter", TaxCode.Standard19, 1, 0.125m);

        var lines = order.Lines.ToArray();
        Assert.Equal(1.00m, lines[0].NetAmount);
        Assert.Equal(0.13m, lines[1].NetAmount);
        Assert.Equal(1.13m, order.NetAmount);
    }

    [Fact]
    public void Line_keeps_description_and_tax_code_from_when_it_was_added()
    {
        var order = NewOrder();
        order.AddLine(Guid.NewGuid(), "book-1", "  Fahrradbuch ", TaxCode.Reduced7, 2, 19.90m);

        var line = Assert.Single(order.Lines);
        Assert.Equal("BOOK-1", line.Sku);
        Assert.Equal("Fahrradbuch", line.Description);
        Assert.Equal(TaxCode.Reduced7, line.TaxCode);
    }

    [Fact]
    public void Lines_are_numbered_from_one_in_capture_order()
    {
        var order = NewOrder();
        order.AddLine(Guid.NewGuid(), "B-1", "Zweite Wahl", TaxCode.Standard19, 1, 1m);
        order.AddLine(Guid.NewGuid(), "A-1", "Erste Wahl", TaxCode.Standard19, 1, 1m);

        Assert.Equal(new[] { (1, "B-1"), (2, "A-1") }, order.Lines.Select(line => (line.Position, line.Sku)));
        Assert.Equal(new[] { "B-1", "A-1" }, order.Release().Lines.Select(line => line.Sku));
    }

    [Fact]
    public void Line_requires_a_description_and_a_defined_tax_code()
    {
        var order = NewOrder();

        Assert.Throws<ArgumentException>(() => order.AddLine(Guid.NewGuid(), "X-1", " ", TaxCode.Standard19, 1, 1m));
        Assert.Throws<ArgumentException>(() => order.AddLine(Guid.NewGuid(), "X-1", "X", (TaxCode)42, 1, 1m));
    }

    private static SalesOrder NewOrder() =>
        SalesOrder.Create(TenantId, Guid.NewGuid(), "SO-TOTALS", Guid.NewGuid(), ShipTo, new BillToSnapshot("Kunde", "10000", null, ShipTo));
}
