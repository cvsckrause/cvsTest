using Erp.BuildingBlocks;
using Erp.Persistence;
using Erp.Worker;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
var tenantId = builder.Configuration.GetValue<Guid?>("Worker:TenantId")
    ?? throw new InvalidOperationException("Worker:TenantId is required.");
var connectionString = builder.Configuration.GetConnectionString("Erp")
    ?? throw new InvalidOperationException("ConnectionStrings:Erp is required.");

builder.Services.AddSingleton<ITenantContext>(new TenantContext(tenantId, "erp-worker"));
builder.Services.AddDbContextFactory<ErpDbContext>((services, options) => options
    .UseNpgsql(connectionString)
    .AddInterceptors(new TenantConnectionInterceptor(services.GetRequiredService<ITenantContext>())));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
