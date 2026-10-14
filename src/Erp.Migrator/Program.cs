using Erp.BuildingBlocks;
using Erp.Inventory.Domain;
using Erp.MasterData.Domain;
using Erp.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var tenantId = Guid.TryParse(Environment.GetEnvironmentVariable("Worker__TenantId"), out var configuredTenant)
    ? configuredTenant
    : Guid.Parse("11111111-1111-1111-1111-111111111111");
var tenant = new TenantContext(tenantId, "erp-migrator");
var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Erp");

if (string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = new NpgsqlConnectionStringBuilder
    {
        Host = Require("Database__Host"),
        Database = Environment.GetEnvironmentVariable("Database__Name") ?? "erp_dev",
        Username = Require("Database__Username"),
        Password = Require("Database__Password"),
        SslMode = SslMode.Require
    }.ConnectionString;
}

await BaselineExistingSchemaAsync(connectionString);

var options = new DbContextOptionsBuilder<ErpDbContext>()
    .UseNpgsql(connectionString)
    .AddInterceptors(new TenantConnectionInterceptor(tenant))
    .Options;
await using var db = new ErpDbContext(options, tenant);
await db.Database.MigrateAsync();

Customer? customer = null;
if (!await db.Customers.AnyAsync())
{
    customer = Customer.Create(tenantId, "10000", "Nordrad GmbH");
    db.Customers.Add(customer);
}
else
{
    customer = await db.Customers.SingleAsync(item => item.Number == "10000");
}

if (!await db.Articles.AnyAsync())
{
    var article = Article.Create(tenantId, "BIKE-001", "Cityrad Nord", 799m);
    db.Articles.Add(article);
    db.StockBalances.Add(StockBalance.Create(tenantId, article.Id, "MAIN", 25));
}

if (!await db.Addresses.AnyAsync())
{
    var address = Address.Create(
        tenantId,
        "Nordrad GmbH",
        "Hafenstraße 12",
        "20095",
        "Hamburg",
        "DE");
    db.Addresses.Add(address);
    db.CustomerAddresses.Add(CustomerAddress.Link(tenantId, customer.Id, address.Id, AddressRole.Shipping));
}

await db.SaveChangesAsync();
Console.WriteLine("Database migrated and development tenant seeded.");

static string Require(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"{name} is required.");

static async Task BaselineExistingSchemaAsync(string connectionString)
{
    // The AWS cell was first created with tables already present while the EF history
    // table lived in public. With HasDefaultSchema("erp"), MigrateAsync looks in erp
    // and would try to recreate InitialTradingSchema. Seed the history row when the
    // base tables already exist so later migrations can apply cleanly.
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = """
        CREATE SCHEMA IF NOT EXISTS erp;

        CREATE TABLE IF NOT EXISTS erp."__EFMigrationsHistory" (
            "MigrationId" character varying(150) NOT NULL,
            "ProductVersion" character varying(32) NOT NULL,
            CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
        );

        INSERT INTO erp."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        SELECT '20261013130016_InitialTradingSchema', '10.0.4'
        WHERE EXISTS (
            SELECT 1
            FROM information_schema.tables
            WHERE table_schema = 'erp' AND table_name = 'articles'
        )
        AND NOT EXISTS (
            SELECT 1
            FROM erp."__EFMigrationsHistory"
            WHERE "MigrationId" = '20261013130016_InitialTradingSchema'
        );

        DO $sync$
        BEGIN
            IF to_regclass('public."__EFMigrationsHistory"') IS NOT NULL THEN
                INSERT INTO erp."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                SELECT h."MigrationId", h."ProductVersion"
                FROM public."__EFMigrationsHistory" h
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM erp."__EFMigrationsHistory" e
                    WHERE e."MigrationId" = h."MigrationId"
                );
            END IF;
        END
        $sync$;
        """;
    await command.ExecuteNonQueryAsync();
}
