using System.Text.Json;
using Erp.BuildingBlocks;
using Erp.Persistence;
using Erp.Platform.Domain;
using Erp.Sales.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api;

internal static class SalesEndpoints
{
    public static RouteGroupBuilder MapSalesEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/sales-orders", async (
            CreateSalesOrderRequest request,
            HttpContext httpContext,
            ITenantContext tenant,
            ErpDbContext db,
            CancellationToken cancellationToken) =>
        {
            var idempotencyKey = Idempotency.RequireKey(httpContext);
            if (idempotencyKey is null) return Results.BadRequest(new { error = "Idempotency-Key header is required." });

            var existing = await db.IdempotencyRecords.AsNoTracking()
                .SingleOrDefaultAsync(record => record.Operation == Idempotency.CreateSalesOrder && record.Key == idempotencyKey, cancellationToken);
            if (existing is not null)
            {
                return Results.Ok(new { id = existing.ResourceId, replayed = true });
            }

            if (!await db.Customers.AnyAsync(customer => customer.Id == request.CustomerId, cancellationToken))
            {
                return Results.BadRequest(new { error = "Customer does not exist." });
            }

            var linked = await db.CustomerAddresses.AnyAsync(
                link => link.CustomerId == request.CustomerId && link.AddressId == request.ShipToAddressId,
                cancellationToken);
            if (!linked)
            {
                return Results.BadRequest(new { error = "Ship-to address must be linked to the customer." });
            }

            var shipToAddress = await db.Addresses.AsNoTracking()
                .SingleOrDefaultAsync(address => address.Id == request.ShipToAddressId, cancellationToken);
            if (shipToAddress is null)
            {
                return Results.BadRequest(new { error = "Ship-to address does not exist." });
            }

            var articleIds = request.Lines.Select(line => line.ArticleId).Distinct().ToArray();
            var articles = await db.Articles
                .Where(article => articleIds.Contains(article.Id))
                .ToDictionaryAsync(article => article.Id, cancellationToken);
            if (articles.Count != articleIds.Length) return Results.BadRequest(new { error = "One or more articles do not exist." });

            var order = SalesOrder.Create(
                tenant.TenantId,
                request.CustomerId,
                request.OrderNumber,
                request.ShipToAddressId,
                shipToAddress.ToSnapshot());
            foreach (var line in request.Lines)
            {
                var article = articles[line.ArticleId];
                order.AddLine(article.Id, article.Sku, line.Quantity, article.SalesPrice);
            }

            db.SalesOrders.Add(order);
            db.IdempotencyRecords.Add(IdempotencyRecord.Create(tenant.TenantId, idempotencyKey, Idempotency.CreateSalesOrder, order.Id));
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v1/sales-orders/{order.Id}", ToResponse(order));
        });

        api.MapGet("/sales-orders/{id:guid}", async (Guid id, ErpDbContext db, CancellationToken cancellationToken) =>
        {
            var order = await db.SalesOrders.AsNoTracking().Include(item => item.Lines)
                .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            return order is null ? Results.NotFound() : Results.Ok(ToResponse(order));
        });

        api.MapPost("/sales-orders/{id:guid}/release", async (
            Guid id,
            HttpContext httpContext,
            ITenantContext tenant,
            ErpDbContext db,
            CancellationToken cancellationToken) =>
        {
            var idempotencyKey = Idempotency.RequireKey(httpContext);
            if (idempotencyKey is null) return Results.BadRequest(new { error = "Idempotency-Key header is required." });

            var existing = await db.IdempotencyRecords.AsNoTracking()
                .SingleOrDefaultAsync(record => record.Operation == Idempotency.ReleaseSalesOrder && record.Key == idempotencyKey, cancellationToken);
            if (existing is not null) return Results.Ok(new { id = existing.ResourceId, replayed = true });

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var order = await db.SalesOrders.Include(item => item.Lines)
                .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (order is null) return Results.NotFound();

            foreach (var line in order.Lines)
            {
                var stock = await db.StockBalances.SingleOrDefaultAsync(
                    balance => balance.ArticleId == line.ArticleId && balance.WarehouseCode == "MAIN",
                    cancellationToken);
                if (stock is null || stock.Available < line.Quantity)
                {
                    return Results.Conflict(new { error = $"Insufficient stock for article {line.Sku}." });
                }
                stock.Reserve(line.Quantity);
            }

            var released = order.Release();
            const string eventType = "erp.sales-order.released.v1";
            var payload = JsonSerializer.Serialize(new
            {
                eventId = released.EventId,
                eventType,
                timestamp = released.OccurredAt,
                tenantId = released.TenantId,
                salesOrderId = released.SalesOrderId,
                salesOrderNumber = released.SalesOrderNumber,
                netAmount = released.NetAmount,
                shipTo = released.ShipTo,
                lines = released.Lines
            });

            db.OutboxMessages.Add(OutboxMessage.From(tenant.TenantId, eventType, payload, released.EventId, released.OccurredAt));
            db.AuditEntries.Add(AuditEntry.Create(
                tenant.TenantId,
                tenant.UserId,
                "SalesOrder.Released",
                nameof(SalesOrder),
                order.Id,
                payload,
                httpContext.TraceIdentifier));
            db.IdempotencyRecords.Add(IdempotencyRecord.Create(tenant.TenantId, idempotencyKey, Idempotency.ReleaseSalesOrder, order.Id));

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(order));
        });

        return api;
    }

    private static SalesOrderResponse ToResponse(SalesOrder order) =>
        new(
            order.Id,
            order.OrderNumber,
            order.CustomerId,
            order.ShipToAddressId,
            order.ShipToSnapshot,
            order.Status.ToString(),
            order.NetAmount,
            order.Lines.Select(line => new SalesOrderLineResponse(line.ArticleId, line.Sku, line.Quantity, line.UnitPrice)).ToArray());
}

public sealed record CreateSalesOrderRequest(
    Guid CustomerId,
    Guid ShipToAddressId,
    string OrderNumber,
    IReadOnlyCollection<CreateSalesOrderLineRequest> Lines);
public sealed record CreateSalesOrderLineRequest(Guid ArticleId, decimal Quantity);
public sealed record SalesOrderResponse(
    Guid Id,
    string OrderNumber,
    Guid CustomerId,
    Guid ShipToAddressId,
    AddressSnapshot ShipTo,
    string Status,
    decimal NetAmount,
    IReadOnlyCollection<SalesOrderLineResponse> Lines);
public sealed record SalesOrderLineResponse(Guid ArticleId, string Sku, decimal Quantity, decimal UnitPrice);
