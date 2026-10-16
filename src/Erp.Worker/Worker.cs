using System.Diagnostics;
using System.Text.Json;
using Erp.Billing.Domain;
using Erp.BuildingBlocks;
using Erp.Inventory.Domain;
using Erp.Observability;
using Erp.Persistence;
using Erp.Platform.Domain;
using Erp.Sales.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Erp.Worker;

public sealed class Worker(
    IDbContextFactory<ErpDbContext> dbContextFactory,
    ILogger<Worker> logger,
    WorkerDatabase database,
    MutableTenantContext tenant,
    IOptions<WorkerOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatch(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Outbox batch failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    internal async Task ProcessBatch(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var maxAttempts = Math.Max(1, settings.MaxAttempts);
        foreach (var claimed in await ClaimAsync(Math.Max(1, settings.BatchSize), cancellationToken))
        {
            tenant.TenantId = claimed.TenantId;
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var message = await db.OutboxMessages.SingleAsync(item => item.Id == claimed.Id, cancellationToken);
            using var scope = logger.BeginScope(RequestLog.Scope(message.CorrelationId, message.TenantId, message.UserId));
            using var activity = ErpActivities.Worker.StartActivity("outbox.process", ActivityKind.Consumer);
            activity?.SetTag("correlation.id", message.CorrelationId);
            activity?.SetTag("tenant.id", message.TenantId.ToString());
            activity?.SetTag("enduser.id", message.UserId);
            try
            {
                if (await db.InboxMessages.AnyAsync(inbox => inbox.MessageId == message.Id, cancellationToken))
                {
                    message.MarkProcessed();
                    await db.SaveChangesAsync(cancellationToken);
                    continue;
                }

                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                if (message.EventType == "erp.sales-order.released.v1")
                {
                    var released = JsonSerializer.Deserialize<ReleasedOrderPayload>(
                        message.Payload,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web))
                        ?? throw new InvalidOperationException("Released-order payload is invalid.");

                    if (released.ShipTo is null)
                    {
                        throw new InvalidOperationException("Released-order payload is missing ship-to snapshot.");
                    }

                    if (!await SalesOrderRowLock.TryLockAsync(db, released.SalesOrderId, cancellationToken))
                    {
                        throw new InvalidOperationException("Released-order payload refers to a missing sales order.");
                    }

                    var order = await db.SalesOrders.Include(item => item.Lines)
                        .SingleAsync(item => item.Id == released.SalesOrderId, cancellationToken);
                    if (order.Status != SalesOrderStatus.Cancelled)
                    {
                        var deliveryNumber = await NumberRangeAllocator.AllocateAsync(
                            db,
                            released.TenantId,
                            NumberDocumentType.Delivery,
                            DateTimeOffset.UtcNow,
                            cancellationToken);
                        var invoiceNumber = await NumberRangeAllocator.AllocateAsync(
                            db,
                            released.TenantId,
                            NumberDocumentType.Invoice,
                            DateTimeOffset.UtcNow,
                            cancellationToken);
                        db.Deliveries.Add(new Delivery(
                            released.TenantId,
                            released.SalesOrderId,
                            deliveryNumber,
                            released.ShipTo));
                        db.Invoices.Add(new Invoice(
                            released.TenantId,
                            released.SalesOrderId,
                            invoiceNumber,
                            released.NetAmount));
                        var occurredAt = DateTimeOffset.UtcNow;
                        foreach (var line in order.Lines)
                        {
                            var stock = await db.StockBalances.SingleOrDefaultAsync(
                                balance => balance.ArticleId == line.ArticleId && balance.WarehouseCode == order.WarehouseCode,
                                cancellationToken)
                                ?? throw new InvalidOperationException(
                                    $"No stock balance for article {line.ArticleId} in warehouse {order.WarehouseCode}.");
                            stock.Issue(line.Quantity);
                            db.StockMovements.Add(StockMovement.GoodsIssue(
                                released.TenantId,
                                line.ArticleId,
                                order.WarehouseCode,
                                line.Quantity,
                                deliveryNumber,
                                tenant.UserId,
                                occurredAt));
                        }
                    }
                }

                db.InboxMessages.Add(new InboxMessage(message.Id));
                message.MarkProcessed();
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                logger.LogInformation("Processed outbox event {EventId} ({EventType}).", message.Id, message.EventType);
            }
            catch (Exception exception)
            {
                db.ChangeTracker.Clear();
                var failed = await db.OutboxMessages.SingleAsync(item => item.Id == claimed.Id, cancellationToken);
                failed.MarkFailed(exception.Message, NextAttemptAt(failed.AttemptCount + 1, settings.BaseDelaySeconds), maxAttempts);
                await db.SaveChangesAsync(cancellationToken);
                logger.LogWarning(exception, "Failed outbox event {EventId}.", claimed.Id);
            }
        }

        try
        {
            OutboxTelemetry.Publish(await OutboxMetrics.ReadAsync(database.ConnectionString, cancellationToken));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Outbox metrics failed.");
        }
    }

    private async Task<IReadOnlyList<ClaimedMessage>> ClaimAsync(int batchSize, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("select id, tenant_id from erp.claim_outbox(@batch_size)", connection);
        command.Parameters.AddWithValue("batch_size", batchSize);
        var claimed = new List<ClaimedMessage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            claimed.Add(new ClaimedMessage(reader.GetGuid(0), reader.GetGuid(1)));
        }

        return claimed;
    }

    private static DateTimeOffset NextAttemptAt(int attempt, int baseDelaySeconds)
    {
        if (baseDelaySeconds <= 0)
        {
            return DateTimeOffset.UnixEpoch;
        }

        var multiplier = Math.Pow(2, Math.Max(0, attempt - 1));
        return DateTimeOffset.UtcNow.AddSeconds(baseDelaySeconds * multiplier);
    }

    private sealed record ClaimedMessage(Guid Id, Guid TenantId);

    private sealed record ReleasedOrderPayload(
        Guid EventId,
        Guid TenantId,
        Guid SalesOrderId,
        string SalesOrderNumber,
        decimal NetAmount,
        AddressSnapshot? ShipTo);
}
