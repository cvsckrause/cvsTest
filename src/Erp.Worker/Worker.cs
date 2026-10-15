using System.Text.Json;
using Erp.Billing.Domain;
using Erp.BuildingBlocks;
using Erp.Persistence;
using Erp.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Worker;

public sealed class Worker(
    IDbContextFactory<ErpDbContext> dbContextFactory,
    ILogger<Worker> logger) : BackgroundService
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var messages = await db.OutboxMessages
            .Where(message => message.ProcessedAt == null)
            .OrderBy(message => message.OccurredAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            if (await db.InboxMessages.AnyAsync(inbox => inbox.MessageId == message.Id, cancellationToken))
            {
                message.MarkProcessed();
                continue;
            }

            try
            {
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

                    db.Deliveries.Add(new Delivery(
                        released.TenantId,
                        released.SalesOrderId,
                        $"DEL-{released.SalesOrderNumber}",
                        released.ShipTo));
                    db.Invoices.Add(new Invoice(
                        released.TenantId,
                        released.SalesOrderId,
                        $"INV-{released.SalesOrderNumber}",
                        released.NetAmount));
                }

                db.InboxMessages.Add(new InboxMessage(message.Id));
                message.MarkProcessed();
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Processed outbox event {EventId} ({EventType}).", message.Id, message.EventType);
            }
            catch (Exception exception)
            {
                message.MarkFailed(exception.Message);
                await db.SaveChangesAsync(cancellationToken);
                logger.LogWarning(exception, "Failed outbox event {EventId}.", message.Id);
            }
        }
    }

    private sealed record ReleasedOrderPayload(
        Guid EventId,
        Guid TenantId,
        Guid SalesOrderId,
        string SalesOrderNumber,
        decimal NetAmount,
        AddressSnapshot? ShipTo);
}
