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
    private const string BillToMigration = "20261016203055_FreezeOrderBillTo";
    private const string InvoiceContentMigration = "20261017105417_AddInvoiceContent";
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

    [Fact]
    public async Task Bill_to_upgrade_bills_every_tenants_customer_at_the_ship_to_and_keeps_forced_rls()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(fixture.AdminConnectionString, OwnerRole);
        await MigrateAsAdminAsync(scratch, MigrationBefore(BillToMigration));
        var orderX = await SeedOrderWithCustomerAsync(scratch, TenantX, "Kunde X", "K-X", "Haus B");
        var orderY = await SeedOrderWithCustomerAsync(scratch, TenantY, "Kunde Y", "K-Y", null);
        await HandOverAsync(scratch, "sales_orders", "customers");

        await MigrateAsOwnerAsync(scratch, BillToMigration);

        Assert.Equal(
            ("Kunde X", "K-X", true, "Ship K-X", "1 Street", "10115", "Berlin", "DE", "Haus B"),
            await BillToAsync(scratch, orderX));
        Assert.Equal(
            ("Kunde Y", "K-Y", true, "Ship K-Y", "1 Street", "10115", "Berlin", "DE", null),
            await BillToAsync(scratch, orderY));

        var probe = new DatabaseProbe(scratch.AdminConnectionString, scratch.AdminConnectionString);
        Assert.Equal((true, true, true), await probe.IsolationAsync(new TenantTable("erp", "sales_orders")));
        Assert.Equal((true, true, true), await probe.IsolationAsync(new TenantTable("erp", "customers")));
    }

    [Fact]
    public async Task Legacy_invoices_keep_number_and_net_and_stay_incomplete()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(fixture.AdminConnectionString, OwnerRole);
        await MigrateAsAdminAsync(scratch, MigrationBefore(InvoiceContentMigration));
        var invoiceX = await SeedLegacyInvoiceAsync(scratch, TenantX, "R-1001", 12.50m);
        var invoiceY = await SeedLegacyInvoiceAsync(scratch, TenantY, "R-2002", 7.00m);
        await HandOverAsync(scratch, "invoices");

        await MigrateAsOwnerAsync(scratch, InvoiceContentMigration);

        Assert.Equal(("R-1001", 12.50m, false), await LegacyInvoiceAsync(scratch, invoiceX));
        Assert.Equal(("R-2002", 7.00m, false), await LegacyInvoiceAsync(scratch, invoiceY));
        var probe = new DatabaseProbe(scratch.AdminConnectionString, scratch.AdminConnectionString);
        Assert.Equal((true, true, true), await probe.IsolationAsync(new TenantTable("erp", "invoice_lines")));
        Assert.Equal((true, true, true), await probe.IsolationAsync(new TenantTable("erp", "invoice_tax_totals")));
    }

    [Fact]
    public async Task App_role_can_select_invoice_content_but_not_change_it()
    {
        Assert.Equal("ok", await SqlStateAsync(fixture.AppConnectionString, fixture.TenantA, """select count(*) from erp.invoices"""));
        Assert.Equal("ok", await SqlStateAsync(fixture.AppConnectionString, fixture.TenantA, """select count(*) from erp.invoice_lines"""));
        Assert.Equal("ok", await SqlStateAsync(fixture.AppConnectionString, fixture.TenantA, """select count(*) from erp.invoice_tax_totals"""));

        foreach (var table in new[] { "invoices", "invoice_lines", "invoice_tax_totals" })
        {
            Assert.Equal("42501", await SqlStateAsync(fixture.AppConnectionString, fixture.TenantA, InsertSql(table)));
            Assert.Equal("42501", await SqlStateAsync(fixture.AppConnectionString, fixture.TenantA, $"update erp.{table} set \"TenantId\" = \"TenantId\""));
            Assert.Equal("42501", await SqlStateAsync(fixture.AppConnectionString, fixture.TenantA, $"delete from erp.{table}"));
        }
    }

    [Fact]
    public async Task Worker_can_insert_invoice_content_but_not_update_or_delete_it()
    {
        var invoiceId = Guid.NewGuid();
        Assert.Equal("ok", await SqlStateAsync(fixture.WorkerConnectionString, fixture.TenantA, """select count(*) from erp.invoices"""));
        Assert.Equal("ok", await SqlStateAsync(
            fixture.WorkerConnectionString,
            fixture.TenantA,
            $"""
            insert into erp.invoices ("Id", "TenantId", "SalesOrderId", "Number", "NetAmount", "CreatedAt")
            values ('{invoiceId}', '{fixture.TenantA}', '{Guid.NewGuid()}', 'R-GRANT', 1, now())
            """));
        Assert.Equal("ok", await SqlStateAsync(fixture.WorkerConnectionString, fixture.TenantA, InsertLineSql(invoiceId, fixture.TenantA)));
        Assert.Equal("ok", await SqlStateAsync(fixture.WorkerConnectionString, fixture.TenantA, InsertTaxSql(invoiceId, fixture.TenantA)));
        Assert.Equal("42501", await SqlStateAsync(
            fixture.WorkerConnectionString, fixture.TenantA, $"update erp.invoices set \"NetAmount\" = \"NetAmount\" where \"Id\" = '{invoiceId}'"));
        Assert.Equal("42501", await SqlStateAsync(
            fixture.WorkerConnectionString, fixture.TenantA, $"delete from erp.invoice_lines where \"InvoiceId\" = '{invoiceId}'"));

        await ExecuteAsync(fixture.AdminConnectionString, $"""
            delete from erp.invoice_tax_totals where "InvoiceId" = '{invoiceId}';
            delete from erp.invoice_lines where "InvoiceId" = '{invoiceId}';
            delete from erp.invoices where "Id" = '{invoiceId}';
            """);
    }

    [Fact]
    public async Task Other_tenant_cannot_read_invoice_lines_or_tax_totals()
    {
        var lines = await CountForTenantAsync(fixture.AppConnectionString, fixture.TenantB, "invoice_lines", fixture.TenantA);
        var totals = await CountForTenantAsync(fixture.AppConnectionString, fixture.TenantB, "invoice_tax_totals", fixture.TenantA);
        var storedLines = await CountForTenantAsync(fixture.AdminConnectionString, fixture.TenantA, "invoice_lines", fixture.TenantA);
        var storedTotals = await CountForTenantAsync(fixture.AdminConnectionString, fixture.TenantA, "invoice_tax_totals", fixture.TenantA);

        Assert.True(storedLines > 0);
        Assert.True(storedTotals > 0);
        Assert.Equal(0, lines);
        Assert.Equal(0, totals);
    }

    private static async Task<Guid> SeedOrderWithCustomerAsync(
        ScratchDatabase scratch, Guid tenantId, string customerName, string customerNumber, string? additionalLine)
    {
        var orderId = Guid.NewGuid();
        await ExecuteAsync(scratch, """
            with customer as (
                insert into erp.customers ("Id", "TenantId", "Number", "Name")
                values (gen_random_uuid(), @tenant, @number, @name)
                returning "Id"
            )
            insert into erp.sales_orders (
                "Id", "TenantId", "CustomerId", "OrderNumber", "ShipToAddressId", "ShipToName", "ShipToStreet",
                "ShipToPostalCode", "ShipToCity", "ShipToCountryCode", "ShipToAdditionalLine", "Status", "CreatedAt")
            select @order, @tenant, customer."Id", 'SO-' || @number, gen_random_uuid(), 'Ship ' || @number, '1 Street',
                '10115', 'Berlin', 'DE', @additional, 1, now()
            from customer
            """,
            ("tenant", tenantId), ("number", customerNumber), ("name", customerName), ("order", orderId),
            ("additional", (object?)additionalLine ?? DBNull.Value));
        return orderId;
    }

    private static async Task<(string BuyerName, string BuyerNumber, bool NoAddressId, string Name, string Street,
        string PostalCode, string City, string CountryCode, string? AdditionalLine)> BillToAsync(ScratchDatabase scratch, Guid orderId)
    {
        await using var connection = new NpgsqlConnection(scratch.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select "BuyerName", "BuyerNumber", "BillToAddressId" is null, "BillToName", "BillToStreet",
                   "BillToPostalCode", "BillToCity", "BillToCountryCode", "BillToAdditionalLine"
            from erp.sales_orders where "Id" = @id
            """,
            connection);
        command.Parameters.AddWithValue("id", orderId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (
            reader.GetString(0),
            reader.GetString(1),
            reader.GetBoolean(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8));
    }

    private static async Task<Guid> SeedLegacyInvoiceAsync(
        ScratchDatabase scratch, Guid tenantId, string number, decimal netAmount)
    {
        var invoiceId = Guid.NewGuid();
        await ExecuteAsync(scratch, """
            insert into erp.invoices ("Id", "TenantId", "SalesOrderId", "Number", "NetAmount", "CreatedAt")
            values (@id, @tenant, gen_random_uuid(), @number, @net, now())
            """,
            ("id", invoiceId), ("tenant", tenantId), ("number", number), ("net", netAmount));
        return invoiceId;
    }

    private static async Task<(string Number, decimal NetAmount, bool ContentComplete)> LegacyInvoiceAsync(
        ScratchDatabase scratch, Guid invoiceId)
    {
        await using var connection = new NpgsqlConnection(scratch.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """select "Number", "NetAmount", "ContentComplete" from erp.invoices where "Id" = @id""", connection);
        command.Parameters.AddWithValue("id", invoiceId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetDecimal(1), reader.GetBoolean(2));
    }

    private static async Task<string> SqlStateAsync(string connectionString, Guid tenantId, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var tenant = new NpgsqlCommand("select set_config('app.tenant_id', @tenant, false)", connection);
        tenant.Parameters.AddWithValue("tenant", tenantId.ToString());
        await tenant.ExecuteNonQueryAsync();
        try
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
            return "ok";
        }
        catch (PostgresException exception)
        {
            return exception.SqlState;
        }
    }

    private static async Task<int> CountForTenantAsync(string connectionString, Guid actingTenant, string table, Guid ownerTenant)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var tenant = new NpgsqlCommand("select set_config('app.tenant_id', @tenant, false)", connection);
        tenant.Parameters.AddWithValue("tenant", actingTenant.ToString());
        await tenant.ExecuteNonQueryAsync();
        await using var command = new NpgsqlCommand(
            $"select count(*)::int from erp.{table} where \"TenantId\" = @owner", connection);
        command.Parameters.AddWithValue("owner", ownerTenant);
        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static string InsertSql(string table) => table switch
    {
        "invoices" => """
            insert into erp.invoices ("Id", "TenantId", "SalesOrderId", "Number", "NetAmount", "CreatedAt")
            values (gen_random_uuid(), 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', gen_random_uuid(), 'R-APP', 1, now())
            """,
        "invoice_lines" => InsertLineSql(Guid.NewGuid(), Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
        "invoice_tax_totals" => InsertTaxSql(Guid.NewGuid(), Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
        _ => throw new ArgumentOutOfRangeException(nameof(table)),
    };

    private static string InsertLineSql(Guid invoiceId, Guid tenantId) => $"""
        insert into erp.invoice_lines (
            "Id", "TenantId", "InvoiceId", "Position", "Sku", "Description", "Quantity", "UnitPrice", "NetAmount", "Rate", "Category", "TaxCode")
        values (gen_random_uuid(), '{tenantId}', '{invoiceId}', 1, 'SKU', 'Probe', 1, 1, 1, 19, 'S', 'Standard19')
        """;

    private static string InsertTaxSql(Guid invoiceId, Guid tenantId) => $"""
        insert into erp.invoice_tax_totals ("Id", "TenantId", "InvoiceId", "Rate", "Category", "NetAmount", "TaxAmount")
        values (gen_random_uuid(), '{tenantId}', '{invoiceId}', 19, 'S', 1, 0.19)
        """;

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
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
