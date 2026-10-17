using System.Reflection;
using Erp.Billing.Domain;
using Erp.BuildingBlocks;

namespace Erp.Domain.Tests;

public sealed class InvoiceTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OrderId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly IssuedOn = new(2026, 10, 16);

    private static readonly SellerSnapshot Seller = new(
        "Northstar Demo GmbH", "Speicherstadt 12", "20457", "Hamburg", "DE", null, "22/123/45678", "DE123456789");

    private static readonly InvoiceBuyer Buyer = new(
        "Nordrad GmbH", "10042", "Hafenstraße 12", "20095", "Hamburg", "DE", null);

    [Fact]
    public void Three_cent_lines_round_tax_once_per_rate()
    {
        var invoice = Issue(
            Line("A", 1, 0.03m, TaxCode.Standard19),
            Line("B", 1, 0.03m, TaxCode.Standard19),
            Line("C", 1, 0.03m, TaxCode.Standard19));

        var total = Assert.Single(invoice.TaxTotals);
        Assert.Equal(19m, total.Rate);
        Assert.Equal("S", total.Category);
        Assert.Equal(0.09m, total.NetAmount);
        Assert.Equal(0.02m, total.TaxAmount);
        Assert.Equal(0.09m, invoice.NetAmount);
        Assert.Equal(0.02m, invoice.TaxAmount);
        Assert.Equal(0.11m, invoice.GrossAmount);
        Assert.True(invoice.ContentComplete);
    }

    [Fact]
    public void Line_net_and_reduced_rate_round_half_away_from_zero()
    {
        var invoice = Issue(
            Line("BOLT", 1, 0.125m, TaxCode.Standard19),
            Line("BOOK", 1, 1.50m, TaxCode.Reduced7));

        var lines = invoice.Lines.ToArray();
        Assert.Equal(0.13m, lines[0].NetAmount);
        var reduced = Assert.Single(invoice.TaxTotals, total => total.Rate == 7m);
        Assert.Equal(1.50m, reduced.NetAmount);
        Assert.Equal(0.11m, reduced.TaxAmount);
        Assert.Equal("S", reduced.Category);
    }

    [Fact]
    public void Mixed_rates_match_the_worked_example()
    {
        var invoice = Issue(
            Line("BIKE", 2, 799.00m, TaxCode.Standard19),
            Line("BOOK", 3, 12.99m, TaxCode.Reduced7));

        var standard = Assert.Single(invoice.TaxTotals, total => total.Rate == 19m);
        var reduced = Assert.Single(invoice.TaxTotals, total => total.Rate == 7m);
        Assert.Equal(1598.00m, standard.NetAmount);
        Assert.Equal(303.62m, standard.TaxAmount);
        Assert.Equal(38.97m, reduced.NetAmount);
        Assert.Equal(2.73m, reduced.TaxAmount);
        Assert.Equal(1636.97m, invoice.NetAmount);
        Assert.Equal(306.35m, invoice.TaxAmount);
        Assert.Equal(1943.32m, invoice.GrossAmount);
    }

    [Fact]
    public void Zero_rate_adds_no_tax_and_a_zero_price_invoice_stays_zero()
    {
        var zero = Issue(Line("FREE", 2, 10m, TaxCode.Zero));
        var total = Assert.Single(zero.TaxTotals);
        Assert.Equal(0m, total.Rate);
        Assert.Equal("Z", total.Category);
        Assert.Equal(0.00m, total.TaxAmount);
        Assert.Equal(20.00m, zero.GrossAmount);

        var empty = Issue(Line("GIFT", 1, 0m, TaxCode.Standard19));
        Assert.Equal(0.00m, empty.NetAmount);
        Assert.Equal(0.00m, empty.TaxAmount);
        Assert.Equal(0.00m, empty.GrossAmount);
    }

    [Fact]
    public void Positions_follow_input_order_and_keep_the_issued_rate()
    {
        var invoice = Issue(
            Line("A", 1, 10m, TaxCode.Reduced7),
            Line("B", 1, 10m, TaxCode.Standard19));

        var lines = invoice.Lines.ToArray();
        Assert.Equal([1, 2], lines.Select(line => line.Position));
        Assert.Equal(7m, lines[0].Rate);
        Assert.Equal(19m, lines[1].Rate);
        Assert.Equal(IssuedOn, invoice.InvoiceDate);
        Assert.Equal(IssuedOn, invoice.DeliveryDate);
    }

    [Fact]
    public void Issue_rejects_an_empty_list_a_seller_without_a_tax_id_and_an_undefined_tax_code()
    {
        Assert.Throws<ArgumentException>(() => Issue());
        var noTaxId = Seller with { Steuernummer = null, UstIdNr = "  " };
        Assert.Throws<ArgumentException>(() => Invoice.Issue(
            TenantId, OrderId, "R-1", IssuedOn, IssuedOn, noTaxId, Buyer, [Line("A", 1, 1m, TaxCode.Standard19)]));
        Assert.Throws<ArgumentException>(() => Issue(Line("A", 1, 1m, (TaxCode)9)));
    }

    [Fact]
    public void Issued_invoices_expose_no_mutators()
    {
        foreach (var type in new[] { typeof(Invoice), typeof(InvoiceLine), typeof(InvoiceTaxTotal) })
        {
            Assert.DoesNotContain(type.GetProperties(), property => property.SetMethod?.IsPublic == true);
            Assert.DoesNotContain(
                type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
                method => !method.IsSpecialName);
        }
    }

    [Fact]
    public void Intra_eu_flag_maps_every_line_to_category_k_at_zero()
    {
        var invoice = Invoice.Issue(
            TenantId,
            OrderId,
            "R-1",
            IssuedOn,
            IssuedOn,
            Seller,
            Buyer,
            [Line("BIKE", 2, 799m, TaxCode.Standard19), Line("BOOK", 3, 12.99m, TaxCode.Reduced7)],
            intraEuSupply: true);

        var total = Assert.Single(invoice.TaxTotals);
        Assert.Equal(0m, total.Rate);
        Assert.Equal("K", total.Category);
        Assert.Equal(0.00m, total.TaxAmount);
        Assert.Equal(1636.97m, invoice.GrossAmount);
        Assert.All(invoice.Lines, line => Assert.Equal(0m, line.Rate));
    }

    private static Invoice Issue(params InvoiceLineInput[] lines) =>
        Invoice.Issue(TenantId, OrderId, "R-1", IssuedOn, IssuedOn, Seller, Buyer, lines);

    private static InvoiceLineInput Line(string sku, decimal quantity, decimal unitPrice, TaxCode taxCode) =>
        new(sku, sku, quantity, unitPrice, taxCode);
}
