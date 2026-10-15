extern alias WorkerHost;

using Worker = WorkerHost::Erp.Worker.Worker;
using WorkerDatabase = WorkerHost::Erp.Worker.WorkerDatabase;
using WorkerOptions = WorkerHost::Erp.Worker.WorkerOptions;
using MutableTenantContext = WorkerHost::Erp.Worker.MutableTenantContext;
using Erp.BuildingBlocks;
using Erp.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Erp.Integration.Tests;

internal static class OutboxProcessor
{
    public static async Task ProcessOnceAsync(string connectionString, WorkerOptions? options = null)
    {
        var tenant = new MutableTenantContext();
        var services = new ServiceCollection();
        services.AddSingleton(tenant);
        services.AddSingleton<ITenantContext>(tenant);
        services.AddSingleton(new WorkerDatabase(connectionString));
        services.AddSingleton<IOptions<WorkerOptions>>(new FixedWorkerOptions(options ?? new WorkerOptions()));
        services.AddDbContextFactory<ErpDbContext>((provider, dbOptions) => dbOptions
            .UseNpgsql(connectionString)
            .AddInterceptors(new TenantConnectionInterceptor(provider.GetRequiredService<ITenantContext>())));
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));

        await using var provider = services.BuildServiceProvider();
        var worker = new Worker(
            provider.GetRequiredService<IDbContextFactory<ErpDbContext>>(),
            provider.GetRequiredService<ILogger<Worker>>(),
            provider.GetRequiredService<WorkerDatabase>(),
            tenant,
            provider.GetRequiredService<IOptions<WorkerOptions>>());
        await worker.ProcessBatch(CancellationToken.None);
    }

    private sealed class FixedWorkerOptions(WorkerOptions value) : IOptions<WorkerOptions>
    {
        public WorkerOptions Value { get; } = value;
    }
}
