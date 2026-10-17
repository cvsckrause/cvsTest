using Erp.BuildingBlocks;

namespace Erp.Billing.Domain;

public sealed class Delivery : ITenantEntity
{
    private Delivery()
    {
    }

    public Delivery(Guid tenantId, Guid salesOrderId, string number, AddressSnapshot shipTo)
    {
        ArgumentNullException.ThrowIfNull(shipTo);
        if (string.IsNullOrWhiteSpace(shipTo.Name)) throw new ArgumentException("Ship-to name is required.", nameof(shipTo));
        if (string.IsNullOrWhiteSpace(shipTo.Street)) throw new ArgumentException("Ship-to street is required.", nameof(shipTo));
        if (string.IsNullOrWhiteSpace(shipTo.PostalCode)) throw new ArgumentException("Ship-to postal code is required.", nameof(shipTo));
        if (string.IsNullOrWhiteSpace(shipTo.City)) throw new ArgumentException("Ship-to city is required.", nameof(shipTo));
        if (string.IsNullOrWhiteSpace(shipTo.CountryCode)) throw new ArgumentException("Ship-to country is required.", nameof(shipTo));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        SalesOrderId = salesOrderId;
        Number = number;
        ShipToName = shipTo.Name;
        ShipToStreet = shipTo.Street;
        ShipToPostalCode = shipTo.PostalCode;
        ShipToCity = shipTo.City;
        ShipToCountryCode = shipTo.CountryCode;
        ShipToAdditionalLine = shipTo.AdditionalLine;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SalesOrderId { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public string ShipToName { get; private set; } = string.Empty;
    public string ShipToStreet { get; private set; } = string.Empty;
    public string ShipToPostalCode { get; private set; } = string.Empty;
    public string ShipToCity { get; private set; } = string.Empty;
    public string ShipToCountryCode { get; private set; } = string.Empty;
    public string? ShipToAdditionalLine { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class Invoice : ITenantEntity
{
    private readonly List<InvoiceLine> _lines = [];
    private readonly List<InvoiceTaxTotal> _taxTotals = [];

    private Invoice()
    {
    }

    // The worker still numbers a net-only invoice until Phase C replaces this call.
    public Invoice(Guid tenantId, Guid salesOrderId, string number, decimal netAmount)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        SalesOrderId = salesOrderId;
        Number = number;
        NetAmount = netAmount;
        ContentComplete = false;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public static Invoice Issue(
        Guid tenantId,
        Guid salesOrderId,
        string number,
        DateOnly invoiceDate,
        DateOnly deliveryDate,
        SellerSnapshot seller,
        InvoiceBuyer buyer,
        IReadOnlyList<InvoiceLineInput> lines,
        bool intraEuSupply = false)
    {
        ArgumentNullException.ThrowIfNull(seller);
        ArgumentNullException.ThrowIfNull(buyer);
        ArgumentNullException.ThrowIfNull(lines);
        if (string.IsNullOrWhiteSpace(number)) throw new ArgumentException("Invoice number is required.", nameof(number));
        if (lines.Count == 0) throw new ArgumentException("An invoice requires at least one line.", nameof(lines));
        if (string.IsNullOrWhiteSpace(seller.Steuernummer) && string.IsNullOrWhiteSpace(seller.UstIdNr))
        {
            throw new ArgumentException("Seller needs a Steuernummer or USt-IdNr.", nameof(seller));
        }

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SalesOrderId = salesOrderId,
            Number = number.Trim(),
            InvoiceDate = invoiceDate,
            DeliveryDate = deliveryDate,
            SellerName = seller.Name,
            SellerStreet = seller.Street,
            SellerPostalCode = seller.PostalCode,
            SellerCity = seller.City,
            SellerCountryCode = seller.CountryCode,
            SellerAdditionalLine = seller.AdditionalLine,
            SellerSteuernummer = BlankToNull(seller.Steuernummer),
            SellerUstIdNr = BlankToNull(seller.UstIdNr),
            BuyerName = buyer.Name,
            BuyerNumber = buyer.Number,
            BuyerStreet = buyer.Street,
            BuyerPostalCode = buyer.PostalCode,
            BuyerCity = buyer.City,
            BuyerCountryCode = buyer.CountryCode,
            BuyerAdditionalLine = buyer.AdditionalLine,
            ContentComplete = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var drafts = new List<InvoiceLineDraft>(lines.Count);
        var position = 1;
        foreach (var line in lines)
        {
            var (rate, category) = TaxRates.For(line.TaxCode, intraEuSupply);
            var net = Money.Round(line.Quantity * line.UnitPrice);
            drafts.Add(new InvoiceLineDraft(
                position, line.Sku.Trim(), line.Description.Trim(), line.Quantity, line.UnitPrice, net, rate, category, line.TaxCode));
            position++;
        }

        foreach (var draft in drafts)
        {
            invoice._lines.Add(InvoiceLine.From(invoice, draft));
        }

        foreach (var total in TaxRates.Summarize(drafts, intraEuSupply))
        {
            invoice._taxTotals.Add(InvoiceTaxTotal.From(invoice, total));
        }

        invoice.NetAmount = drafts.Sum(line => line.NetAmount);
        invoice.TaxAmount = invoice._taxTotals.Sum(total => total.TaxAmount);
        invoice.GrossAmount = invoice.NetAmount + invoice.TaxAmount;
        return invoice;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SalesOrderId { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public decimal NetAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal GrossAmount { get; private set; }
    public DateOnly? InvoiceDate { get; private set; }
    public DateOnly? DeliveryDate { get; private set; }
    public bool ContentComplete { get; private set; }
    public string? SellerName { get; private set; }
    public string? SellerStreet { get; private set; }
    public string? SellerPostalCode { get; private set; }
    public string? SellerCity { get; private set; }
    public string? SellerCountryCode { get; private set; }
    public string? SellerAdditionalLine { get; private set; }
    public string? SellerSteuernummer { get; private set; }
    public string? SellerUstIdNr { get; private set; }
    public string? BuyerName { get; private set; }
    public string? BuyerNumber { get; private set; }
    public string? BuyerStreet { get; private set; }
    public string? BuyerPostalCode { get; private set; }
    public string? BuyerCity { get; private set; }
    public string? BuyerCountryCode { get; private set; }
    public string? BuyerAdditionalLine { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyCollection<InvoiceLine> Lines => _lines.OrderBy(line => line.Position).ToArray();
    public IReadOnlyCollection<InvoiceTaxTotal> TaxTotals => _taxTotals.OrderByDescending(total => total.Rate).ToArray();

    private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class InvoiceLine : ITenantEntity
{
    private InvoiceLine()
    {
    }

    internal static InvoiceLine From(Invoice invoice, InvoiceLineDraft draft) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = invoice.TenantId,
        InvoiceId = invoice.Id,
        Position = draft.Position,
        Sku = draft.Sku,
        Description = draft.Description,
        Quantity = draft.Quantity,
        UnitPrice = draft.UnitPrice,
        NetAmount = draft.NetAmount,
        Rate = draft.Rate,
        Category = draft.Category,
        TaxCode = draft.TaxCode,
    };

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid InvoiceId { get; private set; }
    public int Position { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal NetAmount { get; private set; }
    public decimal Rate { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public TaxCode TaxCode { get; private set; }
}

public sealed class InvoiceTaxTotal : ITenantEntity
{
    private InvoiceTaxTotal()
    {
    }

    internal static InvoiceTaxTotal From(Invoice invoice, InvoiceTaxTotalDraft draft) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = invoice.TenantId,
        InvoiceId = invoice.Id,
        Rate = draft.Rate,
        Category = draft.Category,
        NetAmount = draft.NetAmount,
        TaxAmount = draft.TaxAmount,
    };

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid InvoiceId { get; private set; }
    public decimal Rate { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public decimal NetAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
}
