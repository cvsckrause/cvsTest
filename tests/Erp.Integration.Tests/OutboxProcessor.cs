extern alias WorkerHost;

using Worker = WorkerHost::Erp.Worker.Worker;
using Erp.BuildingBlocks;
using Erp.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Erp.Integration.Tests;

internal static class OutboxProcessor
{
    public static async Task ProcessOnceAsync(string connectionString, Guid tenantId)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(new TenantContext(tenantId, "erp-worker"));
        services.AddDbContextFactory<ErpDbContext>((provider, options) => options
            .UseNpgsql(connectionString)
            .AddInterceptors(new TenantConnectionInterceptor(provider.GetRequiredService<ITenantContext>())));
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));

        await using var provider = services.BuildServiceProvider();
        var worker = new Worker(
            provider.GetRequiredService<IDbContextFactory<ErpDbContext>>(),
            provider.GetRequiredService<ILogger<Worker>>());
        await worker.ProcessBatch(CancellationToken.None);
    }
}
