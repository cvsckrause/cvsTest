using Erp.BuildingBlocks;

namespace Erp.Domain.Tests;

public sealed class MoneyRoundingTests
{
    [Theory]
    [InlineData("0.125", "0.13")]
    [InlineData("0.135", "0.14")]
    [InlineData("2.675", "2.68")]
    [InlineData("-0.125", "-0.13")]
    [InlineData("0.0049", "0.00")]
    [InlineData("0.105", "0.11")]
    [InlineData("1.0005", "1.00")]
    public void Rounds_to_cents_half_away_from_zero(string input, string expected)
    {
        Assert.Equal(decimal.Parse(expected), Money.Round(decimal.Parse(input)));
    }
}
