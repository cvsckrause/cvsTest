using Erp.BuildingBlocks;
using Erp.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Erp.Integration.Tests;

[Collection(PostgresCollection.Name)]
public sealed class InvoiceContentUpgradeTests(PostgresFixture fixture)
{
    private const string OrderLineMigration = "20261016193922_FreezeOrderLineContent";
    private const string OwnerRole = "erp_invoice_content_owner";

    private static readonly Guid TenantX = Guid.Parse("0000000a-0000-0000-0000-000000000016");
    private static readonly Guid TenantY = Guid.Parse("0000000b-0000-0000-0000-000000000016");
    private static readonly Guid MigratorTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Order_line_upgrade_copies_every_tenants_article_name_and_keeps_forced_rls()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(fixture.AdminConnectionString, OwnerRole);
        await MigrateAsAdminAsync(scratch, MigrationBefore(OrderLineMigration));
        var lineX = await SeedOrderLineAsync(scratch, TenantX, "Cityrad X");
        var lineY = await SeedOrderLineAsync(scratch, TenantY, "Lastenrad Y");
        var secondLineX = await SeedSecondLineAsync(scratch, lineX);
        await HandOverAsync(scratch, "sales_order_lines", "articles");

        await MigrateAsOwnerAsync(scratch, OrderLineMigration);

        Assert.Equal(("Cityrad X", "Standard19", 1), await LineAsync(scratch, lineX));
        Assert.Equal(("Cityrad X", "Standard19", 2), await LineAsync(scratch, secondLineX));
        Assert.Equal(("Lastenrad Y", "Standard19", 1), await LineAsync(scratch, lineY));
        Assert.Equal("Standard19", await ScalarAsync(scratch, """select string_agg(distinct "TaxCode", ',') from erp.articles"""));

        var probe = new DatabaseProbe(scratch.AdminConnectionString, scratch.AdminConnectionString);
        Assert.Equal((true, true, true), await probe.IsolationAsync(new TenantTable("erp", "articles")));
        Assert.Equal((true, true, true), await probe.IsolationAsync(new TenantTable("erp", "sales_order_lines")));
        Assert.Equal((OwnerRole, false, false), await probe.RoleAsync(scratch.OwnerConnectionString));
    }

    private static async Task<Guid> SeedOrderLineAsync(ScratchDatabase scratch, Guid tenantId, string articleName)
    {
        var lineId = Guid.NewGuid();
        await ExecuteAsync(scratch, """
            with article as (
                insert into erp.articles ("Id", "TenantId", "Sku", "Name", "SalesPrice")
                values (gen_random_uuid(), @tenant, 'SKU-' || left(@tenant::text, 8), @name, 10)
                returning "Id"
            ), sales_order as (
                insert into erp.sales_orders (
                    "Id", "TenantId", "CustomerId", "OrderNumber", "ShipToAddressId", "ShipToName", "ShipToStreet",
                    "ShipToPostalCode", "ShipToCity", "ShipToCountryCode", "Status", "CreatedAt")
                values (gen_random_uuid(), @tenant, gen_random_uuid(), 'SO-' || left(@tenant::text, 8), gen_random_uuid(),
                    'Ship', '1 Street', '10115', 'Berlin', 'DE', 1, now())
                returning "Id"
            )
            insert into erp.sales_order_lines ("Id", "SalesOrderId", "TenantId", "ArticleId", "Sku", "Quantity", "UnitPrice")
            select @line, sales_order."Id", @tenant, article."Id", 'SKU', 1, 10
            from article, sales_order
            """,
            ("tenant", tenantId), ("name", articleName), ("line", lineId));
        return lineId;
    }

    private static async Task<Guid> SeedSecondLineAsync(ScratchDatabase scratch, Guid firstLineId)
    {
        var lineId = Guid.NewGuid();
        await ExecuteAsync(scratch, """
            insert into erp.sales_order_lines ("Id", "SalesOrderId", "TenantId", "ArticleId", "Sku", "Quantity", "UnitPrice")
            select @line, "SalesOrderId", "TenantId", "ArticleId", 'SKU', 2, 10
            from erp.sales_order_lines where "Id" = @first
            """,
            ("line", lineId), ("first", firstLineId));
        return lineId;
    }

    private static async Task<(string Description, string TaxCode, int Position)> LineAsync(ScratchDatabase scratch, Guid lineId)
    {
        await using var connection = new NpgsqlConnection(scratch.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """select "Description", "TaxCode", "Position" from erp.sales_order_lines where "Id" = @id""", connection);
        command.Parameters.AddWithValue("id", lineId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1), reader.GetInt32(2));
    }

    // Earlier migrations run as postgres. The test role owns only what the migration under test touches,
    // so a backfill that forgets to lift FORCE on one of them sees no other tenant's rows.
    private static Task HandOverAsync(ScratchDatabase scratch, params string[] tables) =>
        ExecuteAsync(scratch, $"""
            do $$
            begin
                create role {OwnerRole} login password '{OwnerRole}' nosuperuser nobypassrls;
            exception
                when duplicate_object then null;
            end
            $$;
            grant usage, create on schema erp to {OwnerRole};
            alter table erp."__EFMigrationsHistory" owner to {OwnerRole};
            {string.Join(Environment.NewLine, tables.Select(table => $"alter table erp.\"{table}\" owner to {OwnerRole};"))}
            """);

    private static async Task MigrateAsAdminAsync(ScratchDatabase scratch, string target)
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql(scratch.AdminConnectionString)
            .Options;
        await using var db = new ErpDbContext(options, new TenantContext(Guid.NewGuid(), "migrate"));
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task MigrateAsOwnerAsync(ScratchDatabase scratch, string target)
    {
        var tenant = new TenantContext(MigratorTenant, "erp-migrator");
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql(scratch.OwnerConnectionString)
            .AddInterceptors(new TenantConnectionInterceptor(tenant))
            .Options;
        await using var db = new ErpDbContext(options, tenant);
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    private static string MigrationBefore(string migrationId)
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused")
            .Options;
        using var db = new ErpDbContext(options, new TenantContext(Guid.NewGuid(), "model"));
        return db.GetService<IMigrationsAssembly>().Migrations.Keys
            .Where(id => string.CompareOrdinal(id, migrationId) < 0)
            .OrderBy(id => id, StringComparer.Ordinal)
            .Last();
    }

    private static async Task<string?> ScalarAsync(ScratchDatabase scratch, string sql)
    {
        await using var connection = new NpgsqlConnection(scratch.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (string?)await command.ExecuteScalarAsync();
    }

    private static async Task ExecuteAsync(ScratchDatabase scratch, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(scratch.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }
}
