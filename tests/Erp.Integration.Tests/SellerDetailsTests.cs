using System.Net;
using System.Text.Json;
using Erp.BuildingBlocks;
using Erp.Persistence;
using Erp.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.Integration.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SellerDetailsTests(PostgresFixture fixture)
{
    private static readonly Guid TenantWithoutMandant = Guid.Parse("0000000c-0000-0000-0000-000000000016");
    private const string UserWithoutMandant = "integration-no-mandant";

    [Fact]
    public async Task Seller_details_accept_either_tax_identifier_and_normalize_the_UstIdNr()
    {
        using var api = fixture.OpenSession(fixture.TenantA);

        var steuernummerOnly = await api.PutAsync("/api/v1/mandant/seller", Seller(steuernummer: "22/111/11111", ustIdNr: null));
        Assert.Equal(HttpStatusCode.OK, steuernummerOnly.Status);
        using (var body = JsonDocument.Parse(steuernummerOnly.Body))
        {
            Assert.Equal("22/111/11111", body.RootElement.GetProperty("steuernummer").GetString());
            Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("ustIdNr").ValueKind);
            Assert.Equal(0, body.RootElement.GetProperty("missingFields").GetArrayLength());
        }

        var ustIdNrOnly = await api.PutAsync("/api/v1/mandant/seller", Seller(steuernummer: null, ustIdNr: "de 111 111 111"));
        Assert.Equal(HttpStatusCode.OK, ustIdNrOnly.Status);

        var read = await api.GetAsync("/api/v1/mandant/seller");
        using var stored = JsonDocument.Parse(read.Body);
        Assert.Equal(HttpStatusCode.OK, read.Status);
        Assert.Equal("A Handels GmbH", stored.RootElement.GetProperty("firmenname").GetString());
        Assert.Equal(PostgresFixture.MandantNumberA, stored.RootElement.GetProperty("mandantNumber").GetInt64());
        Assert.Equal("DE111111111", stored.RootElement.GetProperty("ustIdNr").GetString());
        Assert.Equal(JsonValueKind.Null, stored.RootElement.GetProperty("steuernummer").ValueKind);
        Assert.Equal("Alsterweg 1", stored.RootElement.GetProperty("street").GetString());
    }

    [Theory]
    [InlineData("DE", null, null)]
    [InlineData("D", "22/111/11111", null)]
    [InlineData("DE", null, "DE-111")]
    public async Task Incomplete_or_malformed_seller_details_return_400_and_change_nothing(
        string countryCode, string? steuernummer, string? ustIdNr)
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var before = (await api.GetAsync("/api/v1/mandant/seller")).Body;

        var result = await api.PutAsync("/api/v1/mandant/seller", Seller(steuernummer, ustIdNr, countryCode, street: "Anderswo 9"));

        Assert.Equal(HttpStatusCode.BadRequest, result.Status);
        Assert.Contains("\"detail\"", result.Body);
        Assert.Equal(before, (await api.GetAsync("/api/v1/mandant/seller")).Body);
    }

    [Fact]
    public async Task Each_tenant_reads_and_writes_only_its_own_Mandant_and_every_change_is_audited()
    {
        using var apiA = fixture.OpenSession(fixture.TenantA);
        using var apiB = fixture.OpenSession(fixture.TenantB);
        var bBefore = (await apiB.GetAsync("/api/v1/mandant/seller")).Body;
        var auditsBefore = await AuditCountAsync(fixture.TenantA);

        var put = await apiA.PutAsync("/api/v1/mandant/seller", Seller("22/111/11111", "DE111111111", street: "Alsterweg 3"));

        Assert.Equal(HttpStatusCode.OK, put.Status);
        Assert.Equal(auditsBefore + 1, await AuditCountAsync(fixture.TenantA));
        var bAfter = await apiB.GetAsync("/api/v1/mandant/seller");
        Assert.Equal(bBefore, bAfter.Body);
        using var b = JsonDocument.Parse(bAfter.Body);
        Assert.Equal("B Vertrieb GmbH", b.RootElement.GetProperty("firmenname").GetString());
        Assert.Equal("Brückenstraße 2", b.RootElement.GetProperty("street").GetString());
    }

    [Fact]
    public async Task Tenant_without_a_Mandant_row_gets_404_and_cannot_release()
    {
        await GrantMembershipWithoutMandantAsync();
        using var api = fixture.OpenSession(UserWithoutMandant, TenantWithoutMandant);

        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync("/api/v1/mandant/seller")).Status);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await api.PutAsync("/api/v1/mandant/seller", Seller("22/111/11111", null))).Status);

        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var customer = await api.PostOkAsync("/api/v1/customers", new { number = $"NM-{suffix}", name = "No Mandant Customer" });
        var address = await api.PostOkAsync("/api/v1/addresses", new
        {
            name = "No Mandant",
            street = "1 Street",
            postalCode = "10115",
            city = "Berlin",
            countryCode = "DE"
        });
        await api.PostOkAsync($"/api/v1/customers/{customer.Id}/addresses", new { addressId = address.Id, role = "Shipping" });
        var article = await api.PostOkAsync("/api/v1/articles", new { sku = $"NM-{suffix}", name = "No Mandant Article", salesPrice = 5m, openingStock = 3m });
        var order = await api.PostOkAsync(
            "/api/v1/sales-orders",
            new { customerId = customer.Id, shipToAddressId = address.Id, lines = new[] { new { articleId = article.Id, quantity = 1m } } },
            $"nm-{suffix}-order");

        var release = await api.PostAsync($"/api/v1/sales-orders/{order.Id}/release", new { }, $"nm-{suffix}-release");

        Assert.Equal(HttpStatusCode.Conflict, release.Status);
        using var body = JsonDocument.Parse(release.Body);
        Assert.Equal(
            Mandant.SellerFields,
            body.RootElement.GetProperty("missingSellerFields").EnumerateArray().Select(item => item.GetString()));
    }

    private static object Seller(string? steuernummer, string? ustIdNr, string countryCode = "DE", string street = "Alsterweg 1") =>
        new
        {
            street,
            postalCode = "20095",
            city = "Hamburg",
            countryCode,
            additionalLine = (string?)null,
            steuernummer,
            ustIdNr
        };

    private async Task<long> AuditCountAsync(Guid tenantId)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """select count(*) from erp.audit_entries where "TenantId" = @tenant and "Action" = 'Mandant.SellerDetailsChanged'""",
            connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task GrantMembershipWithoutMandantAsync()
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql(fixture.AdminConnectionString)
            .Options;
        await using var db = new ErpDbContext(options, new TenantContext(Guid.NewGuid(), "seller-test"));
        if (await db.TenantMemberships.AnyAsync(item => item.UserId == UserWithoutMandant)) return;
        db.TenantMemberships.Add(TenantMembership.Grant(UserWithoutMandant, TenantWithoutMandant, isDefault: true));
        await db.SaveChangesAsync();
    }
}
