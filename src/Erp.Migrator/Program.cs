using Erp.BuildingBlocks;
using Erp.Inventory.Domain;
using Erp.MasterData.Domain;
using Erp.Persistence;
using Erp.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var seedDemo = args is ["--seed-demo"];
if (args.Length > 0 && !seedDemo)
{
    throw new InvalidOperationException("Supported arguments: --seed-demo");
}

var tenantId = Guid.TryParse(Environment.GetEnvironmentVariable("Worker__TenantId"), out var configuredTenant)
    ? configuredTenant
    : Guid.Parse("11111111-1111-1111-1111-111111111111");
var tenant = new TenantContext(tenantId, "erp-migrator");
var connectionString = await ResolveConnectionStringAsync();

var options = new DbContextOptionsBuilder<ErpDbContext>()
    .UseNpgsql(connectionString)
    .AddInterceptors(new TenantConnectionInterceptor(tenant))
    .Options;
await using var db = new ErpDbContext(options, tenant);
await db.Database.MigrateAsync();
await RevokeDefinerMembershipAsync();

if (!seedDemo)
{
    Console.WriteLine("Database migrated.");
    return;
}

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

await SeedMembershipsAsync(db, tenantId);
await db.SaveChangesAsync();
Console.WriteLine("Database migrated and development tenant seeded.");

static async Task<string> ResolveConnectionStringAsync()
{
    var configured = Environment.GetEnvironmentVariable("ConnectionStrings__Erp");
    var adminPassword = Environment.GetEnvironmentVariable("Database__AdminPassword");
    if (string.IsNullOrWhiteSpace(adminPassword))
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return BuildConnectionString(
            Require("Database__Username"),
            Require("Database__Password"));
    }

    var ownerPassword = Require("Database__Password");
    var adminConnectionString = BuildConnectionString(
        Environment.GetEnvironmentVariable("Database__AdminUsername") ?? "erp",
        adminPassword);
    await EnsureApplicationRolesAsync(
        adminConnectionString,
        ownerPassword,
        Require("Database__AppPassword"),
        Require("Database__WorkerPassword"));

    return BuildConnectionString(
        Environment.GetEnvironmentVariable("Database__Username") ?? "erp_owner",
        ownerPassword);
}

static string BuildConnectionString(string username, string password) =>
    new NpgsqlConnectionStringBuilder
    {
        Host = Require("Database__Host"),
        Database = Environment.GetEnvironmentVariable("Database__Name") ?? "erp_dev",
        Username = username,
        Password = password,
        SslMode = SslMode.Require
    }.ConnectionString;

static async Task EnsureApplicationRolesAsync(
    string adminConnectionString,
    string ownerPassword,
    string appPassword,
    string workerPassword)
{
    await using var connection = new NpgsqlConnection(adminConnectionString);
    await connection.OpenAsync();

    // Each statement is its own auto-commit command. CREATE ROLE cannot run inside a transaction.
    await ExecuteAsync(connection, """
        DO $roles$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'erp_outbox_definer') THEN
                CREATE ROLE erp_outbox_definer NOLOGIN BYPASSRLS;
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'erp_owner') THEN
                CREATE ROLE erp_owner NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'erp_app') THEN
                CREATE ROLE erp_app NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'erp_worker') THEN
                CREATE ROLE erp_worker NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
            END IF;
        END
        $roles$;
        """);

    // RDS master is not a superuser, so it cannot mention SUPERUSER or BYPASSRLS here.
    // CREATE ROLE above already sets those attributes.
    await ExecuteAsync(connection, $"""
        ALTER ROLE erp_owner WITH LOGIN PASSWORD {QuoteLiteral(ownerPassword)};
        """);
    await ExecuteAsync(connection, $"""
        ALTER ROLE erp_app WITH LOGIN PASSWORD {QuoteLiteral(appPassword)};
        """);
    await ExecuteAsync(connection, $"""
        ALTER ROLE erp_worker WITH LOGIN PASSWORD {QuoteLiteral(workerPassword)};
        """);

    await ExecuteAsync(connection, """
        GRANT erp_owner TO CURRENT_USER;
        GRANT erp_outbox_definer TO erp_owner;
        GRANT erp_worker TO erp_owner WITH ADMIN OPTION;
        """);

    await ExecuteAsync(connection, """
        DO $db$
        BEGIN
            EXECUTE format('GRANT CONNECT, CREATE ON DATABASE %I TO erp_owner', current_database());
            EXECUTE format('GRANT CONNECT ON DATABASE %I TO erp_app, erp_worker', current_database());
            -- EF creates __EFMigrationsHistory in public. PostgreSQL 15 revoked CREATE there from PUBLIC.
            GRANT CREATE ON SCHEMA public TO erp_owner;
        END
        $db$;
        """);

    await ExecuteAsync(connection, """
        DO $own$
        DECLARE
            obj record;
        BEGIN
            IF to_regnamespace('erp') IS NULL THEN
                RETURN;
            END IF;

            EXECUTE 'ALTER SCHEMA erp OWNER TO erp_owner';

            FOR obj IN
                SELECT c.relname, c.relkind
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                JOIN pg_roles owner_role ON owner_role.oid = c.relowner
                WHERE n.nspname = 'erp'
                  AND c.relkind IN ('r', 'p', 'S', 'v', 'm')
                  AND owner_role.rolname <> 'erp_outbox_definer'
            LOOP
                IF obj.relkind = 'S' THEN
                    EXECUTE format('ALTER SEQUENCE erp.%I OWNER TO erp_owner', obj.relname);
                ELSIF obj.relkind = 'm' THEN
                    EXECUTE format('ALTER MATERIALIZED VIEW erp.%I OWNER TO erp_owner', obj.relname);
                ELSIF obj.relkind = 'v' THEN
                    EXECUTE format('ALTER VIEW erp.%I OWNER TO erp_owner', obj.relname);
                ELSE
                    EXECUTE format('ALTER TABLE erp.%I OWNER TO erp_owner', obj.relname);
                END IF;
            END LOOP;
        END
        $own$;
        """);
}

static async Task RevokeDefinerMembershipAsync()
{
    var adminPassword = Environment.GetEnvironmentVariable("Database__AdminPassword");
    if (string.IsNullOrWhiteSpace(adminPassword))
    {
        return;
    }

    var adminConnectionString = BuildConnectionString(
        Environment.GetEnvironmentVariable("Database__AdminUsername") ?? "erp",
        adminPassword);
    await using var connection = new NpgsqlConnection(adminConnectionString);
    await connection.OpenAsync();
    await ExecuteAsync(connection, """
        REVOKE erp_outbox_definer FROM erp_owner;
        REVOKE CREATE ON SCHEMA public FROM erp_owner;
        """);
}

static async Task SeedMembershipsAsync(ErpDbContext db, Guid tenantId)
{
    var seed = Environment.GetEnvironmentVariable("Authentication__SeedMemberships");
    if (string.IsNullOrWhiteSpace(seed))
    {
        var secondTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
        seed = $"synthetic-a:{tenantId}:default;synthetic-b:{secondTenant}:default";
    }

    foreach (var membership in TenantMembership.ParseSeed(seed))
    {
        var exists = await db.TenantMemberships.AnyAsync(item =>
            item.UserId == membership.UserId && item.TenantId == membership.TenantId);
        if (!exists)
        {
            db.TenantMemberships.Add(membership);
        }
    }
}

static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
{
    await using var command = new NpgsqlCommand(sql, connection);
    await command.ExecuteNonQueryAsync();
}

static string QuoteLiteral(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

static string Require(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"{name} is required.");
