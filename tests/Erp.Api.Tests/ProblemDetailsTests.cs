using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.Api.Tests;

[CollectionDefinition("ErpApi", DisableParallelization = true)]
public sealed class ErpApiCollection;

[Collection("ErpApi")]
public sealed class ProblemDetailsTests
{
    [Fact]
    public void Duplicate_sku_is_a_conflict()
    {
        var decision = ApiProblemDecision.From(UniqueViolation("articles", "IX_articles_TenantId_Sku"));

        Assert.Equal(StatusCodes.Status409Conflict, decision.Status);
        Assert.Equal("An article with this SKU already exists.", decision.Detail);
        Assert.False(decision.Replay);
    }

    [Fact]
    public void Second_release_is_a_conflict()
    {
        var decision = ApiProblemDecision.From(new InvalidOperationException("Released orders cannot be changed."));

        Assert.Equal(StatusCodes.Status409Conflict, decision.Status);
        Assert.Equal("Released orders cannot be changed.", decision.Detail);
    }

    [Fact]
    public void Concurrent_idempotency_key_is_a_replay()
    {
        var decision = ApiProblemDecision.From(UniqueViolation("idempotency_records", "IX_idempotency_records_TenantId_Operation_Key"));

        Assert.True(decision.Replay);
    }

    [Fact]
    public void Concurrent_stock_update_asks_for_a_retry()
    {
        var decision = ApiProblemDecision.From(new DbUpdateConcurrencyException("conflict"));

        Assert.Equal(StatusCodes.Status409Conflict, decision.Status);
        Assert.Equal(ApiProblemDecision.RetryDetail, decision.Detail);
    }

    [Fact]
    public void Validation_failures_are_bad_requests()
    {
        var decision = ApiProblemDecision.From(new ArgumentException("SKU is required.", "sku"));

        Assert.Equal(StatusCodes.Status400BadRequest, decision.Status);
        Assert.Contains("SKU is required", decision.Detail);
    }

    [Fact]
    public async Task Invalid_article_returns_problem_details()
    {
        await using var fixture = ApiFixture.Start(
            environment: "Development",
            authenticationEnabled: false,
            developmentIdentityEnabled: true,
            configuredTenant: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            configuredUserId: "local-developer");

        using var content = new StringContent(
            """{"sku":" ","name":"Bike","salesPrice":10,"openingStock":0}""",
            Encoding.UTF8,
            "application/json");
        var response = await fixture.Client.PostAsync("/api/v1/articles", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":400", body, StringComparison.Ordinal);
        Assert.Contains("SKU is required", body, StringComparison.Ordinal);
    }

    private static DbUpdateException UniqueViolation(string table, string constraint) =>
        new(
            "duplicate key",
            new PostgresException(
                "duplicate key value violates unique constraint",
                "ERROR",
                "ERROR",
                PostgresErrorCodes.UniqueViolation,
                detail: null,
                hint: null,
                position: 0,
                internalPosition: 0,
                internalQuery: null,
                where: null,
                schemaName: "erp",
                tableName: table,
                columnName: null,
                dataTypeName: null,
                constraintName: constraint,
                file: null,
                line: null,
                routine: null));
}
