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

var options = new DbContextOptionsBuilder<ErpDbContext>()
    .UseNpgsql(connectionString)
    .AddInterceptors(new TenantConnectionInterceptor(tenant))
    .Options;
await using var db = new ErpDbContext(options, tenant);
await db.Database.MigrateAsync();

if (!await db.Customers.AnyAsync())
{
    db.Customers.Add(Customer.Create(tenantId, "10000", "Nordrad GmbH"));
}

if (!await db.Articles.AnyAsync())
{
    var article = Article.Create(tenantId, "BIKE-001", "Cityrad Nord", 799m);
    db.Articles.Add(article);
    db.StockBalances.Add(StockBalance.Create(tenantId, article.Id, "MAIN", 25));
}

await db.SaveChangesAsync();
Console.WriteLine("Database migrated and development tenant seeded.");

static string Require(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"{name} is required.");
