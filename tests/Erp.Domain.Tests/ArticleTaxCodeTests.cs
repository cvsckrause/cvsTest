using Erp.BuildingBlocks;
using Erp.MasterData.Domain;

namespace Erp.Domain.Tests;

public sealed class ArticleTaxCodeTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Article_without_tax_code_is_standard_rated()
    {
        var article = Article.Create(TenantId, "BIKE-001", "Cityrad", 799m);

        Assert.Equal(TaxCode.Standard19, article.TaxCode);
    }

    [Fact]
    public void Article_keeps_its_tax_code_and_can_change_it()
    {
        var article = Article.Create(TenantId, "BOOK-001", "Fahrradbuch", 19.90m, TaxCode.Reduced7);
        Assert.Equal(TaxCode.Reduced7, article.TaxCode);

        article.Update("Fahrradbuch", 19.90m, TaxCode.Zero);

        Assert.Equal(TaxCode.Zero, article.TaxCode);
    }

    [Fact]
    public void Undefined_tax_code_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Article.Create(TenantId, "X-1", "X", 1m, (TaxCode)99));
    }
}
