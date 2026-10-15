using System.Text.Json;
using Erp.BuildingBlocks;
using Erp.Inventory.Domain;
using Erp.Persistence;
using Erp.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api;

internal static class InventoryEndpoints
{
    public static RouteGroupBuilder MapInventoryEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/stock", async (ErpDbContext db, CancellationToken cancellationToken) =>
        {
            var stock = await (
                from balance in db.StockBalances.AsNoTracking()
                join article in db.Articles.AsNoTracking() on balance.ArticleId equals article.Id
                orderby article.Sku, balance.WarehouseCode
                select new StockResponse(
                    balance.ArticleId,
                    article.Sku,
                    article.Name,
                    balance.WarehouseCode,
                    balance.OnHand,
                    balance.Reserved,
                    balance.OnHand - balance.Reserved))
                .ToListAsync(cancellationToken);
            return Results.Ok(stock);
        }).Produces<List<StockResponse>>();

        api.MapGet("/stock/movements", async (ErpDbContext db, int? limit, CancellationToken cancellationToken) =>
        {
            var pageSize = Math.Clamp(limit ?? 50, 1, 200);
            var movements = await (
                from movement in db.StockMovements.AsNoTracking()
                join article in db.Articles.AsNoTracking() on movement.ArticleId equals article.Id
                orderby movement.OccurredAt descending
                select new StockMovementResponse(
                    movement.Id,
                    movement.ArticleId,
                    article.Sku,
                    article.Name,
                    movement.WarehouseCode,
                    movement.Quantity,
                    movement.MovementType.ToString(),
                    movement.Reference,
                    movement.CreatedBy,
                    movement.OccurredAt))
                .Take(pageSize)
                .ToListAsync(cancellationToken);
            return Results.Ok(movements);
        }).Produces<List<StockMovementResponse>>();

        api.MapPost("/stock/receipts", async (
            CreateStockReceiptRequest request,
            HttpContext httpContext,
            ITenantContext tenant,
            ErpDbContext db,
            CancellationToken cancellationToken) =>
        {
            var idempotencyKey = Idempotency.RequireKey(httpContext);
            if (idempotencyKey is null) return Results.BadRequest(new { error = "Idempotency-Key header is required." });
            if (request.Quantity <= 0) return Results.BadRequest(new { error = "Quantity must be positive." });

            var existing = await db.IdempotencyRecords.AsNoTracking()
                .SingleOrDefaultAsync(record => record.Operation == Idempotency.StockGoodsReceipt && record.Key == idempotencyKey, cancellationToken);
            if (existing is not null)
            {
                var prior = await db.StockMovements.AsNoTracking()
                    .SingleOrDefaultAsync(movement => movement.Id == existing.ResourceId, cancellationToken);
                if (prior is null) return Results.Ok(new { id = existing.ResourceId, replayed = true });

                var priorArticle = await db.Articles.AsNoTracking()
                    .SingleAsync(article => article.Id == prior.ArticleId, cancellationToken);
                return Results.Ok(new StockReceiptResponse(
                    prior.Id,
                    prior.ArticleId,
                    priorArticle.Sku,
                    prior.WarehouseCode,
                    prior.Quantity,
                    prior.Reference,
                    prior.OccurredAt,
                    Replayed: true));
            }

            var article = await db.Articles.SingleOrDefaultAsync(item => item.Id == request.ArticleId, cancellationToken);
            if (article is null) return Results.BadRequest(new { error = "Article does not exist." });

            var warehouseCode = string.IsNullOrWhiteSpace(request.WarehouseCode) ? "MAIN" : request.WarehouseCode.Trim().ToUpperInvariant();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var stock = await db.StockBalances.SingleOrDefaultAsync(
                balance => balance.ArticleId == request.ArticleId && balance.WarehouseCode == warehouseCode,
                cancellationToken);
            if (stock is null)
            {
                stock = StockBalance.Create(tenant.TenantId, request.ArticleId, warehouseCode, 0);
                db.StockBalances.Add(stock);
            }

            stock.Receive(request.Quantity);
            var occurredAt = DateTimeOffset.UtcNow;
            var movement = StockMovement.GoodsReceipt(
                tenant.TenantId,
                request.ArticleId,
                warehouseCode,
                request.Quantity,
                request.Reference,
                tenant.UserId,
                occurredAt);
            db.StockMovements.Add(movement);

            var auditPayload = JsonSerializer.Serialize(new
            {
                movementId = movement.Id,
                articleId = movement.ArticleId,
                sku = article.Sku,
                warehouseCode = movement.WarehouseCode,
                quantity = movement.Quantity,
                reference = movement.Reference
            });
            db.AuditEntries.Add(AuditEntry.Create(
                tenant.TenantId,
                tenant.UserId,
                "Stock.GoodsReceipt",
                nameof(StockMovement),
                movement.Id,
                auditPayload,
                httpContext.TraceIdentifier));
            db.IdempotencyRecords.Add(IdempotencyRecord.Create(tenant.TenantId, idempotencyKey, Idempotency.StockGoodsReceipt, movement.Id));

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Results.Created(
                $"/api/v1/stock/movements/{movement.Id}",
                new StockReceiptResponse(
                    movement.Id,
                    movement.ArticleId,
                    article.Sku,
                    movement.WarehouseCode,
                    movement.Quantity,
                    movement.Reference,
                    movement.OccurredAt,
                    Replayed: false));
        }).Produces<StockReceiptResponse>(StatusCodes.Status201Created);

        return api;
    }
}

public sealed record StockResponse(Guid ArticleId, string Sku, string Name, string WarehouseCode, decimal OnHand, decimal Reserved, decimal Available);
public sealed record CreateStockReceiptRequest(Guid ArticleId, decimal Quantity, string? WarehouseCode, string? Reference);
public sealed record StockReceiptResponse(Guid Id, Guid ArticleId, string Sku, string WarehouseCode, decimal Quantity, string? Reference, DateTimeOffset OccurredAt, bool Replayed);
public sealed record StockMovementResponse(Guid Id, Guid ArticleId, string Sku, string Name, string WarehouseCode, decimal Quantity, string MovementType, string? Reference, string CreatedBy, DateTimeOffset OccurredAt);
