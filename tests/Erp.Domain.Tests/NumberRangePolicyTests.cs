using Erp.Platform.Domain;

namespace Erp.Domain.Tests;

public sealed class NumberRangePolicyTests
{
    [Fact]
    public void Invoice_numbers_are_the_bare_sequence()
    {
        Assert.Equal("", NumberRangePolicy.Defaults(NumberDocumentType.Invoice).Prefix);
        Assert.Equal("1", NumberRangePolicy.Format("", 1));
        Assert.Equal("50", NumberRangePolicy.Format("", 50));
    }

    [Fact]
    public void Yearly_reset_uses_the_utc_year_and_a_stable_range_uses_zero()
    {
        var allocatedAt = new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.Zero);

        Assert.Equal(2026, NumberRangePolicy.YearBucket(resetYearly: true, allocatedAt));
        Assert.Equal(0, NumberRangePolicy.YearBucket(resetYearly: false, allocatedAt));
    }
}
