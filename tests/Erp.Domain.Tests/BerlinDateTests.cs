using Erp.BuildingBlocks;

namespace Erp.Domain.Tests;

public sealed class BerlinDateTests
{
    [Theory]
    [InlineData("2026-12-31T23:30:00Z", "2027-01-01")]
    [InlineData("2026-06-30T22:30:00Z", "2026-07-01")]
    [InlineData("2026-01-01T00:30:00Z", "2026-01-01")]
    [InlineData("2026-07-01T21:30:00Z", "2026-07-01")]
    public void Berlin_date_follows_central_european_time(string instant, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), BusinessDate.Berlin(DateTimeOffset.Parse(instant)));
    }
}
