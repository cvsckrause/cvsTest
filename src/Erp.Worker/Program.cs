using Erp.BuildingBlocks;
using Erp.Observability;
using Erp.Persistence;
using Erp.Worker;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddErpJsonLogging();
var connectionString = builder.Configuration.GetConnectionString("Erp");
if (string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = new NpgsqlConnectionStringBuilder
    {
        Host = builder.Configuration["Database:Host"] ?? throw new InvalidOperationException("Database:Host is required."),
        Database = builder.Configuration["Database:Name"] ?? "erp_dev",
        Username = builder.Configuration["Database:Username"] ?? throw new InvalidOperationException("Database:Username is required."),
        Password = builder.Configuration["Database:Password"] ?? throw new InvalidOperationException("Database:Password is required."),
        SslMode = SslMode.Require
    }.ConnectionString;
}

if (args is ["replay", var rawMessageId])
{
    if (!Guid.TryParse(rawMessageId, out var messageId))
    {
        throw new InvalidOperationException("Replay requires a message id.");
    }

    var replayed = await OutboxReplay.ReplayAsync(connectionString, messageId);
    Console.WriteLine(replayed
        ? $"Replayed outbox message {messageId}."
        : $"Outbox message {messageId} is not dead-lettered.");
    return;
}

var tenant = new MutableTenantContext();
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection("Worker"));
builder.Services.AddSingleton(tenant);
builder.Services.AddSingleton<ITenantContext>(tenant);
builder.Services.AddSingleton(new WorkerDatabase(connectionString));
builder.Services.AddDbContextFactory<ErpDbContext>((services, options) => options
    .UseNpgsql(connectionString)
    .AddInterceptors(new TenantConnectionInterceptor(services.GetRequiredService<ITenantContext>())));
builder.Services.AddHostedService<Worker>();
builder.Services.AddErpTelemetry("erp-worker", builder.Configuration);

var host = builder.Build();
host.Run();
