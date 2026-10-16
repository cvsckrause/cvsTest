using System.Globalization;
using Erp.BuildingBlocks;

namespace Erp.Platform.Domain;

public enum NumberDocumentType
{
    SalesOrder,
    Delivery,
    Invoice
}

public static class NumberRangePolicy
{
    public static (string Prefix, bool ResetYearly) Defaults(NumberDocumentType documentType) => documentType switch
    {
        NumberDocumentType.SalesOrder => ("AU-", false),
        NumberDocumentType.Delivery => ("LS-", false),
        NumberDocumentType.Invoice => ("", false),
        _ => throw new ArgumentOutOfRangeException(nameof(documentType))
    };

    public static int YearBucket(bool resetYearly, DateTimeOffset allocatedAt) =>
        resetYearly ? allocatedAt.UtcDateTime.Year : 0;

    public static string Format(string prefix, long value)
    {
        if (value < 1) throw new ArgumentOutOfRangeException(nameof(value));
        var number = string.Concat(prefix, value.ToString(CultureInfo.InvariantCulture));
        if (number.Length > 40) throw new ArgumentException("Allocated number exceeds 40 characters.", nameof(value));
        return number;
    }
}

public sealed class NumberRange : ITenantEntity
{
    private NumberRange()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string DocumentType { get; private set; } = string.Empty;
    public string Prefix { get; private set; } = string.Empty;
    public long NextValue { get; private set; }
    public int Year { get; private set; }
    public bool ResetYearly { get; private set; }
}
