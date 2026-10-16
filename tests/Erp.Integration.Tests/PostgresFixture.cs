using Erp.BuildingBlocks;
using Erp.Persistence;
using Erp.Platform.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Erp.Integration.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}

public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private WebApplicationFactory<Program>? _factory;

    public Guid TenantA { get; } = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public Guid TenantB { get; } = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public TenantGraph SeedA { get; private set; }
    public TenantGraph SeedB { get; private set; }
    public string AdminConnectionString { get; private set; } = string.Empty;
    public string AppConnectionString { get; private set; } = string.Empty;
    public string WorkerConnectionString { get; private set; } = string.Empty;
    internal DatabaseProbe Probe { get; private set; } = null!;
    internal CapturedLogs Logs { get; } = new();

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("erp_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await _container.StartAsync();

        var adminConnectionString = _container.GetConnectionString();
        AdminConnectionString = adminConnectionString;
        await MigrateAsync(adminConnectionString);
        await SeedMembershipsAsync(adminConnectionString);
        await CreateAppRoleAsync(adminConnectionString);

        var appBuilder = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Username = "erp_app",
            Password = "erp_app"
        };
        AppConnectionString = appBuilder.ConnectionString;
        var workerBuilder = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Username = "erp_worker",
            Password = "erp_worker"
        };
        WorkerConnectionString = workerBuilder.ConnectionString;
        Probe = new DatabaseProbe(AppConnectionString, adminConnectionString);
        _factory = CreateFactory(AppConnectionString);

        SeedA = await SeedTenantAsync(TenantA, "A");
        SeedB = await SeedTenantAsync(TenantB, "B");
        await OutboxProcessor.ProcessOnceAsync(WorkerConnectionString);
    }

    internal ApiSession OpenSession(Guid tenantId) =>
        OpenSession(tenantId == TenantA ? "integration-a" : "integration-b", tenantId);

    internal ApiSession OpenSession(string userId, Guid? tenantId) =>
        new(_factory ?? throw new InvalidOperationException("The API host is not started."), userId, tenantId);

    internal HttpClient CreateAnonymousClient() =>
        (_factory ?? throw new InvalidOperationException("The API host is not started.")).CreateClient();

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    private WebApplicationFactory<Program> CreateFactory(string connectionString)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Erp", connectionString);
        Environment.SetEnvironmentVariable("Authentication__Authority", "https://example.invalid/");
        Environment.SetEnvironmentVariable("Authentication__ClientId", "test-client");
        Environment.SetEnvironmentVariable(
            "Authentication__HostedUiDomain",
            "erp-test.auth.eu-north-1.amazoncognito.com");
        Environment.SetEnvironmentVariable("Authentication__Audience", "erp-api");
        Environment.SetEnvironmentVariable("DevelopmentIdentity__Enabled", "false");
        Environment.SetEnvironmentVariable("DevelopmentIdentity__TenantId", "");
        Environment.SetEnvironmentVariable("DevelopmentIdentity__UserId", "");

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureLogging(logging => logging.AddProvider(Logs));
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, TestTenantHandler>(TestTenantHandler.SchemeName, _ => { });
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = TestTenantHandler.SchemeName;
                    options.DefaultChallengeScheme = TestTenantHandler.SchemeName;
                });
            });
        });
    }

    private async Task<TenantGraph> SeedTenantAsync(Guid tenantId, string label)
    {
        using var api = OpenSession(tenantId);
        var customer = await api.PostOkAsync("/api/v1/customers", new { number = $"{label}-100", name = $"{label} Customer" });
        var address = await api.PostOkAsync("/api/v1/addresses", new
        {
            name = $"{label} Warehouse",
            street = "1 Test Street",
            postalCode = "10115",
            city = "Berlin",
            countryCode = "DE"
        });
        await api.PostOkAsync($"/api/v1/customers/{customer.Id}/addresses", new { addressId = address.Id, role = "Shipping" });
        var article = await api.PostOkAsync("/api/v1/articles", new
        {
            sku = $"{label}-SKU",
            name = $"{label} Article",
            salesPrice = 10m,
            openingStock = 10m,
            warehouseCode = "MAIN"
        });
        var receipt = await api.PostOkAsync(
            "/api/v1/stock/receipts",
            new { articleId = article.Id, quantity = 1m, warehouseCode = "MAIN", reference = $"{label}-RECEIPT" },
            $"{label}-receipt");
        var order = await api.PostOkAsync(
            "/api/v1/sales-orders",
            new
            {
                customerId = customer.Id,
                shipToAddressId = address.Id,
                orderNumber = $"{label}-SO-1",
                lines = new[] { new { articleId = article.Id, quantity = 1m } }
            },
            $"{label}-order");
        await api.PostOkAsync($"/api/v1/sales-orders/{order.Id}/release", new { }, $"{label}-release");

        return new TenantGraph(customer.Id, address.Id, article.Id, order.Id, receipt.Id);
    }

    private async Task SeedMembershipsAsync(string adminConnectionString)
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql(adminConnectionString)
            .Options;
        await using var db = new ErpDbContext(options, new TenantContext(Guid.NewGuid(), "membership-seed"));
        db.TenantMemberships.Add(TenantMembership.Grant("integration-a", TenantA, isDefault: true));
        db.TenantMemberships.Add(TenantMembership.Grant("integration-b", TenantB, isDefault: true));
        await db.SaveChangesAsync();
    }

    private static async Task MigrateAsync(string adminConnectionString)
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql(adminConnectionString)
            .Options;
        await using var db = new ErpDbContext(options, new TenantContext(Guid.NewGuid(), "migrate"));
        await db.Database.MigrateAsync();
    }

    private static async Task CreateAppRoleAsync(string adminConnectionString)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            alter role erp_app with login password 'erp_app';
            alter role erp_worker with login password 'erp_worker';
            """,
            connection);
        await command.ExecuteNonQueryAsync();
    }
}

public readonly record struct TenantGraph(
    Guid CustomerId,
    Guid AddressId,
    Guid ArticleId,
    Guid SalesOrderId,
    Guid MovementId);
