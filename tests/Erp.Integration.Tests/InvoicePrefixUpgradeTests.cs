using Erp.BuildingBlocks;
using Erp.Persistence;
using Erp.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Erp.Integration.Tests;

[Collection(PostgresCollection.Name)]
public sealed class InvoicePrefixUpgradeTests(PostgresFixture fixture)
{
    private const string PrefixMigration = "20261016091520_PrefixInvoiceNumbers";
    private const string OwnerRole = "erp_migration_owner";

    // Earlier migrations run as postgres: the test role owns only what PrefixInvoiceNumbers touches.
    private static readonly string PreviousMigration = MigrationBefore(PrefixMigration);

    private static readonly Guid TenantX = Guid.Parse("0000000a-0000-0000-0000-000000000022");
    private static readonly Guid TenantY = Guid.Parse("0000000b-0000-0000-0000-000000000022");
    private static readonly Guid TenantZ = Guid.Parse("0000000c-0000-0000-0000-000000000022");
    private static readonly Guid MigratorTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Upgrade_moves_every_tenants_empty_invoice_prefix_to_r()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(fixture.AdminConnectionString);
        await PrepareNumberRangesSchemaAsync(scratch);

        await MigrateAsOwnerAsync(scratch, PrefixMigration);

        Assert.Equal(("R-", 4L), await RangeAsync(scratch, TenantX, "Invoice", 0));
        Assert.Equal(("R-", 2L), await RangeAsync(scratch, TenantY, "Invoice", 0));
        Assert.Equal(("R-", 3L), await RangeAsync(scratch, TenantZ, "Invoice", 2026));
        Assert.Equal(("LS-", 7L), await RangeAsync(scratch, TenantX, "Delivery", 0));
        Assert.Equal(("AU-", 9L), await RangeAsync(scratch, TenantX, "SalesOrder", 0));
        Assert.Equal(["1", "2", "3"], await InvoiceNumbersAsync(scratch, TenantX));
        Assert.Equal("R-4", await AllocateInvoiceAsOwnerAsync(scratch, TenantX));

        var probe = new DatabaseProbe(scratch.AdminConnectionString, scratch.AdminConnectionString);
        Assert.Equal((true, true, true), await probe.IsolationAsync(new TenantTable("erp", "number_ranges")));
        Assert.Equal((OwnerRole, false, false), await probe.RoleAsync(scratch.OwnerConnectionString));
    }

    [Fact]
    public async Task Downgrade_restores_the_empty_invoice_prefix_and_keeps_the_counter()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(fixture.AdminConnectionString);
        await PrepareNumberRangesSchemaAsync(scratch);
        await MigrateAsOwnerAsync(scratch, PrefixMigration);

        await MigrateAsOwnerAsync(scratch, PreviousMigration);

        Assert.Equal(("", 4L), await RangeAsync(scratch, TenantX, "Invoice", 0));
        Assert.Equal(("", 2L), await RangeAsync(scratch, TenantY, "Invoice", 0));
        Assert.Equal(("LS-", 7L), await RangeAsync(scratch, TenantX, "Delivery", 0));
        Assert.Equal(["1", "2", "3"], await InvoiceNumbersAsync(scratch, TenantX));

        var probe = new DatabaseProbe(scratch.AdminConnectionString, scratch.AdminConnectionString);
        Assert.Equal((true, true, true), await probe.IsolationAsync(new TenantTable("erp", "number_ranges")));
    }

    private static async Task PrepareNumberRangesSchemaAsync(ScratchDatabase scratch)
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql(scratch.AdminConnectionString)
            .Options;
        await using (var db = new ErpDbContext(options, new TenantContext(Guid.NewGuid(), "migrate")))
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        await using var connection = new NpgsqlConnection(scratch.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
            insert into erp.number_ranges ("Id", "TenantId", "DocumentType", "Prefix", "NextValue", "Year", "ResetYearly")
            values
                (gen_random_uuid(), @x, 'Invoice', '', 4, 0, false),
                (gen_random_uuid(), @x, 'Delivery', 'LS-', 7, 0, false),
                (gen_random_uuid(), @x, 'SalesOrder', 'AU-', 9, 0, false),
                (gen_random_uuid(), @y, 'Invoice', '', 2, 0, false),
                (gen_random_uuid(), @z, 'Invoice', '', 3, 2026, true);

            insert into erp.invoices ("Id", "TenantId", "SalesOrderId", "Number", "NetAmount", "CreatedAt")
            select gen_random_uuid(), @x, gen_random_uuid(), number, 10, now()
            from unnest(array['1', '2', '3']) as number;

            do $$
            begin
                create role {OwnerRole} login password '{OwnerRole}' nosuperuser nobypassrls;
            exception
                when duplicate_object then null;
            end
            $$;
            grant usage, create on schema erp, public to {OwnerRole};
            alter table erp.number_ranges owner to {OwnerRole};
            alter table public."__EFMigrationsHistory" owner to {OwnerRole};
            """,
            connection);
        command.Parameters.AddWithValue("x", TenantX);
        command.Parameters.AddWithValue("y", TenantY);
        command.Parameters.AddWithValue("z", TenantZ);
        await command.ExecuteNonQueryAsync();
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

    private static async Task<string> AllocateInvoiceAsOwnerAsync(ScratchDatabase scratch, Guid tenantId)
    {
        var tenant = new TenantContext(tenantId, "ranges");
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql(scratch.OwnerConnectionString)
            .AddInterceptors(new TenantConnectionInterceptor(tenant))
            .Options;
        await using var db = new ErpDbContext(options, tenant);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var number = await NumberRangeAllocator.AllocateAsync(
            db, tenantId, NumberDocumentType.Invoice, DateTimeOffset.UtcNow, CancellationToken.None);
        await transaction.RollbackAsync();
        return number;
    }

    private static async Task<(string Prefix, long NextValue)> RangeAsync(
        ScratchDatabase scratch,
        Guid tenantId,
        string documentType,
        int year)
    {
        await using var connection = new NpgsqlConnection(scratch.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select "Prefix", "NextValue"
            from erp.number_ranges
            where "TenantId" = @tenant and "DocumentType" = @type and "Year" = @year
            """,
            connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("type", documentType);
        command.Parameters.AddWithValue("year", year);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException($"No {documentType} range for tenant {tenantId}, year {year}.");
        }

        return (reader.GetString(0), reader.GetInt64(1));
    }

    private static async Task<string[]> InvoiceNumbersAsync(ScratchDatabase scratch, Guid tenantId)
    {
        await using var connection = new NpgsqlConnection(scratch.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select "Number"
            from erp.invoices
            where "TenantId" = @tenant
            order by "Number"
            """,
            connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        var numbers = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) numbers.Add(reader.GetString(0));
        return numbers.ToArray();
    }

    private sealed class ScratchDatabase : IAsyncDisposable
    {
        private readonly string _serverConnectionString;
        private readonly string _name;

        private ScratchDatabase(string serverConnectionString, string name)
        {
            _serverConnectionString = serverConnectionString;
            _name = name;
            AdminConnectionString = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = name }.ConnectionString;
            OwnerConnectionString = new NpgsqlConnectionStringBuilder(AdminConnectionString)
            {
                Username = OwnerRole,
                Password = OwnerRole
            }.ConnectionString;
        }

        public string AdminConnectionString { get; }
        public string OwnerConnectionString { get; }

        public static async Task<ScratchDatabase> CreateAsync(string serverConnectionString)
        {
            var scratch = new ScratchDatabase(serverConnectionString, "erp_upgrade_" + Guid.NewGuid().ToString("N"));
            await scratch.ExecuteOnServerAsync($"create database {scratch._name}");
            return scratch;
        }

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearPool(new NpgsqlConnection(AdminConnectionString));
            NpgsqlConnection.ClearPool(new NpgsqlConnection(OwnerConnectionString));
            await ExecuteOnServerAsync($"drop database if exists {_name} with (force)");
        }

        private async Task ExecuteOnServerAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(_serverConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
