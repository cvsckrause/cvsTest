using Erp.Platform.Domain;

namespace Erp.Domain.Tests;

public sealed class NumberRangePolicyTests
{
    [Fact]
    public void Invoice_numbers_carry_the_r_prefix()
    {
        Assert.Equal("R-", NumberRangePolicy.Defaults(NumberDocumentType.Invoice).Prefix);
        Assert.Equal("R-1", NumberRangePolicy.Format("R-", 1));
        Assert.Equal("R-50", NumberRangePolicy.Format("R-", 50));
    }

    [Fact]
    public void Delivery_and_order_prefixes_are_unchanged()
    {
        Assert.Equal("LS-", NumberRangePolicy.Defaults(NumberDocumentType.Delivery).Prefix);
        Assert.Equal("AU-", NumberRangePolicy.Defaults(NumberDocumentType.SalesOrder).Prefix);
    }

    [Fact]
    public void Yearly_reset_uses_the_utc_year_and_a_stable_range_uses_zero()
    {
        var allocatedAt = new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.Zero);

        Assert.Equal(2026, NumberRangePolicy.YearBucket(resetYearly: true, allocatedAt));
        Assert.Equal(0, NumberRangePolicy.YearBucket(resetYearly: false, allocatedAt));
    }
}
