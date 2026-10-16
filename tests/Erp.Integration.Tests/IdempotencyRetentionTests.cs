extern alias WorkerHost;

using System.Net;
using WorkerOptions = WorkerHost::Erp.Worker.WorkerOptions;

namespace Erp.Integration.Tests;

public sealed class WorkerOptionsDefaultsTests
{
    [Fact]
    public void Idempotency_retention_defaults_to_thirty_days()
    {
        Assert.Equal(30, new WorkerOptions().IdempotencyRetentionDays);
        Assert.Equal(30, new WorkerOptions { MaxAttempts = 1 }.IdempotencyRetentionDays);
    }
}

[Collection(PostgresCollection.Name)]
public sealed class IdempotencyRetentionTests(PostgresFixture fixture)
{
    [Fact]
    public async Task The_worker_deletes_expired_keys_in_every_tenant()
    {
        var oldA = Guid.NewGuid().ToString("N");
        var oldB = Guid.NewGuid().ToString("N");
        var fresh = Guid.NewGuid().ToString("N");
        await fixture.Probe.InsertIdempotencyAsync(fixture.TenantA, oldA, 31);
        await fixture.Probe.InsertIdempotencyAsync(fixture.TenantB, oldB, 31);
        await fixture.Probe.InsertIdempotencyAsync(fixture.TenantA, fresh, 0);

        await OutboxProcessor.ProcessOnceAsync(fixture.WorkerConnectionString);

        Assert.Equal(0, await fixture.Probe.CountIdempotencyAsync(fixture.TenantA, oldA));
        Assert.Equal(0, await fixture.Probe.CountIdempotencyAsync(fixture.TenantB, oldB));
        Assert.Equal(1, await fixture.Probe.CountIdempotencyAsync(fixture.TenantA, fresh));
    }

    [Fact]
    public async Task A_deleted_key_can_be_used_for_a_new_write()
    {
        var key = Guid.NewGuid().ToString("N");
        await fixture.Probe.InsertIdempotencyAsync(fixture.TenantA, key, 31);
        await OutboxProcessor.ProcessOnceAsync(fixture.WorkerConnectionString);

        using var api = fixture.OpenSession(fixture.TenantA);
        var reference = "RET-" + Guid.NewGuid().ToString("N")[..8];
        var created = await api.PostAsync(
            "/api/v1/stock/receipts",
            new { articleId = fixture.SeedA.ArticleId, quantity = 1m, warehouseCode = "MAIN", reference },
            key);

        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.False(created.Replayed);
        Assert.Equal(1, await fixture.Probe.CountIdempotencyAsync(fixture.TenantA, key));
    }

    [Fact]
    public async Task Disabling_retention_keeps_expired_keys()
    {
        var key = Guid.NewGuid().ToString("N");
        await fixture.Probe.InsertIdempotencyAsync(fixture.TenantA, key, 31);

        await OutboxProcessor.ProcessOnceAsync(
            fixture.WorkerConnectionString,
            new WorkerOptions { IdempotencyRetentionDays = 0 });

        Assert.Equal(1, await fixture.Probe.CountIdempotencyAsync(fixture.TenantA, key));
    }
}
