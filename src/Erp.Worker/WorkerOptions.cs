using Erp.BuildingBlocks;

namespace Erp.Worker;

public sealed class WorkerOptions
{
    public int BatchSize { get; set; } = 20;
    public int MaxAttempts { get; set; } = 5;
    public int BaseDelaySeconds { get; set; } = 5;
}

public sealed record WorkerDatabase(string ConnectionString);

public sealed class MutableTenantContext : ITenantContext
{
    public Guid TenantId { get; set; }
    public string UserId { get; set; } = "erp-worker";
}
