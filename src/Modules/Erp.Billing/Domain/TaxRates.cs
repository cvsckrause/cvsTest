using Erp.BuildingBlocks;

namespace Erp.Billing.Domain;

public sealed record InvoiceBuyer(
    string Name,
    string Number,
    string Street,
    string PostalCode,
    string City,
    string CountryCode,
    string? AdditionalLine);

public sealed record InvoiceLineInput(
    string Sku,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    TaxCode TaxCode);

public static class TaxRates
{
    public static IReadOnlyList<InvoiceTaxTotalDraft> Summarize(
        IReadOnlyList<InvoiceLineDraft> lines,
        bool intraEuSupply)
    {
        return lines
            .GroupBy(line => intraEuSupply ? (Rate: 0m, Category: "K") : (line.Rate, line.Category))
            .Select(group =>
            {
                var net = group.Sum(line => line.NetAmount);
                return new InvoiceTaxTotalDraft(group.Key.Rate, group.Key.Category, net, Money.Round(net * group.Key.Rate / 100m));
            })
            .OrderByDescending(total => total.Rate)
            .ThenBy(total => total.Category, StringComparer.Ordinal)
            .ToArray();
    }

    internal static (decimal Rate, string Category) For(TaxCode taxCode, bool intraEuSupply)
    {
        TaxCodes.RequireDefined(taxCode, nameof(taxCode));
        if (intraEuSupply) return (0m, "K");
        return taxCode switch
        {
            TaxCode.Standard19 => (19m, "S"),
            TaxCode.Reduced7 => (7m, "S"),
            TaxCode.Zero => (0m, "Z"),
            _ => throw new ArgumentException(TaxCodes.InvalidMessage, nameof(taxCode)),
        };
    }
}

public sealed record InvoiceLineDraft(
    int Position,
    string Sku,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal NetAmount,
    decimal Rate,
    string Category,
    TaxCode TaxCode);

public sealed record InvoiceTaxTotalDraft(decimal Rate, string Category, decimal NetAmount, decimal TaxAmount);
