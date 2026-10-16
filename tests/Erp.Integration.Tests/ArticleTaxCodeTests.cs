using System.Net;
using System.Text.Json;

namespace Erp.Integration.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ArticleTaxCodeTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Tax_code_is_set_on_create_returned_on_read_and_changed_on_update()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var sku = $"TAX-{Guid.NewGuid():N}"[..16];

        var created = await api.PostOkAsync("/api/v1/articles", new { sku, name = "Fahrradbuch", salesPrice = 19.90m, openingStock = 0m, taxCode = "Reduced7" });
        Assert.Equal("Reduced7", TaxCodeOf(created.Body));

        var read = await api.GetAsync($"/api/v1/articles/{created.Id}");
        Assert.Equal("Reduced7", TaxCodeOf(read.Body));

        var updated = await api.PutAsync($"/api/v1/articles/{created.Id}", new { name = "Fahrradbuch", salesPrice = 19.90m, taxCode = "Zero" });
        Assert.Equal(HttpStatusCode.OK, updated.Status);
        Assert.Equal("Zero", TaxCodeOf(updated.Body));

        var renamed = await api.PutAsync($"/api/v1/articles/{created.Id}", new { name = "Fahrradbuch 2", salesPrice = 21m });
        Assert.Equal("Zero", TaxCodeOf(renamed.Body));

        var blank = await api.PutAsync($"/api/v1/articles/{created.Id}", new { name = "Fahrradbuch 2", salesPrice = 21m, taxCode = " " });
        Assert.Equal("Zero", TaxCodeOf(blank.Body));
    }

    [Fact]
    public async Task Article_created_without_tax_code_is_standard_rated()
    {
        using var api = fixture.OpenSession(fixture.TenantA);

        var read = await api.GetAsync($"/api/v1/articles/{fixture.SeedA.ArticleId}");

        Assert.Equal("Standard19", TaxCodeOf(read.Body));
    }

    [Theory]
    [InlineData("Foo")]
    [InlineData("1")]
    public async Task Unknown_tax_code_is_bad_request(string taxCode)
    {
        using var api = fixture.OpenSession(fixture.TenantA);

        var result = await api.PostAsync("/api/v1/articles", new { sku = $"BAD-{Guid.NewGuid():N}"[..16], name = "X", salesPrice = 1m, openingStock = 0m, taxCode });

        Assert.Equal(HttpStatusCode.BadRequest, result.Status);
    }

    private static string? TaxCodeOf(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("taxCode").GetString();
    }
}
