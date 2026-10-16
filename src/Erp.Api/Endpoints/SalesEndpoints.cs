using System.Globalization;
using System.Text.Json;
using Erp.Billing.Domain;
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
        api.MapGet("/sales-orders", async (
            ErpDbContext db,
            string? status,
            string? after,
            int? limit,
            CancellationToken cancellationToken) =>
        {
            SalesOrderStatus? statusFilter = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<SalesOrderStatus>(status, ignoreCase: true, out var parsed))
                {
                    return Results.BadRequest(new { error = "Status must be Draft, Released, or Cancelled." });
                }

                statusFilter = parsed;
            }

            var pageSize = Math.Clamp(limit ?? 50, 1, 200);
            var query = db.SalesOrders.AsNoTracking().AsQueryable();
            if (statusFilter is { } filter)
            {
                query = query.Where(order => order.Status == filter);
            }

            if (!string.IsNullOrWhiteSpace(after))
            {
                if (!TryParseOrderCursor(after, out var afterCreatedAt, out var afterId))
                {
                    return Results.BadRequest(new { error = "Invalid after cursor." });
                }

                query = query.Where(order =>
                    order.CreatedAt < afterCreatedAt
                    || (order.CreatedAt == afterCreatedAt && order.Id.CompareTo(afterId) < 0));
            }

            var rows = await query
                .OrderByDescending(order => order.CreatedAt)
                .ThenByDescending(order => order.Id)
                .Take(pageSize)
                .Select(order => new
                {
                    order.Id,
                    order.OrderNumber,
                    order.CustomerId,
                    order.Status,
                    NetAmount = order.Lines.Sum(line => line.Quantity * line.UnitPrice),
                    order.CreatedAt
                })
                .ToListAsync(cancellationToken);

            var response = rows
                .Select(order => new SalesOrderSummaryResponse(
                    order.Id,
                    order.OrderNumber,
                    order.CustomerId,
                    order.Status.ToString(),
                    order.NetAmount,
                    order.CreatedAt))
                .ToList();
            return Results.Ok(response);
        }).Produces<List<SalesOrderSummaryResponse>>();

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

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var orderNumber = string.IsNullOrWhiteSpace(request.OrderNumber)
                ? await NumberRangeAllocator.AllocateAsync(
                    db,
                    tenant.TenantId,
                    NumberDocumentType.SalesOrder,
                    DateTimeOffset.UtcNow,
                    cancellationToken)
                : request.OrderNumber;
            var order = SalesOrder.Create(
                tenant.TenantId,
                request.CustomerId,
                orderNumber,
                request.ShipToAddressId,
                shipToAddress.ToSnapshot(),
                request.WarehouseCode ?? "MAIN");
            foreach (var line in request.Lines)
            {
                var article = articles[line.ArticleId];
                order.AddLine(article.Id, article.Sku, line.Quantity, article.SalesPrice);
            }

            db.SalesOrders.Add(order);
            db.IdempotencyRecords.Add(IdempotencyRecord.Create(tenant.TenantId, idempotencyKey, Idempotency.CreateSalesOrder, order.Id, null));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Created($"/api/v1/sales-orders/{order.Id}", ToResponse(order));
        }).Produces<SalesOrderResponse>(StatusCodes.Status201Created);

        api.MapGet("/sales-orders/{id:guid}", async (Guid id, ErpDbContext db, CancellationToken cancellationToken) =>
        {
            var order = await db.SalesOrders.AsNoTracking().Include(item => item.Lines)
                .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            return order is null ? Results.NotFound() : Results.Ok(ToResponse(order));
        }).Produces<SalesOrderResponse>().Produces(StatusCodes.Status404NotFound);

        api.MapGet("/sales-orders/{id:guid}/deliveries", async (Guid id, ErpDbContext db, CancellationToken cancellationToken) =>
        {
            if (!await db.SalesOrders.AsNoTracking().AnyAsync(order => order.Id == id, cancellationToken))
            {
                return Results.NotFound();
            }

            var deliveries = await db.Deliveries.AsNoTracking()
                .Where(delivery => delivery.SalesOrderId == id)
                .OrderBy(delivery => delivery.CreatedAt)
                .ThenBy(delivery => delivery.Id)
                .ToListAsync(cancellationToken);
            return Results.Ok(deliveries.Select(ToDeliveryResponse).ToList());
        }).Produces<List<DeliveryResponse>>().Produces(StatusCodes.Status404NotFound);

        api.MapGet("/sales-orders/{id:guid}/invoices", async (Guid id, ErpDbContext db, CancellationToken cancellationToken) =>
        {
            if (!await db.SalesOrders.AsNoTracking().AnyAsync(order => order.Id == id, cancellationToken))
            {
                return Results.NotFound();
            }

            var invoices = await db.Invoices.AsNoTracking()
                .Where(invoice => invoice.SalesOrderId == id)
                .OrderBy(invoice => invoice.CreatedAt)
                .ThenBy(invoice => invoice.Id)
                .ToListAsync(cancellationToken);
            return Results.Ok(invoices.Select(ToInvoiceResponse).ToList());
        }).Produces<List<InvoiceResponse>>().Produces(StatusCodes.Status404NotFound);

        api.MapGet("/invoices/{id:guid}", async (Guid id, ErpDbContext db, CancellationToken cancellationToken) =>
        {
            var invoice = await db.Invoices.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            return invoice is null ? Results.NotFound() : Results.Ok(ToInvoiceResponse(invoice));
        }).Produces<InvoiceResponse>().Produces(StatusCodes.Status404NotFound);

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
            if (!await SalesOrderRowLock.TryLockAsync(db, id, cancellationToken))
            {
                return Results.NotFound();
            }

            var order = await db.SalesOrders.Include(item => item.Lines)
                .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (order is null) return Results.NotFound();
            if (order.Status != SalesOrderStatus.Draft)
            {
                throw new InvalidOperationException(
                    order.Status == SalesOrderStatus.Cancelled
                        ? "Cancelled orders cannot be released."
                        : "Released orders cannot be changed.");
            }

            foreach (var line in order.Lines)
            {
                var stock = await db.StockBalances.SingleOrDefaultAsync(
                    balance => balance.ArticleId == line.ArticleId && balance.WarehouseCode == order.WarehouseCode,
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

            db.OutboxMessages.Add(OutboxMessage.From(
                tenant.TenantId,
                eventType,
                payload,
                released.EventId,
                released.OccurredAt,
                httpContext.TraceIdentifier,
                tenant.UserId));
            db.AuditEntries.Add(AuditEntry.Create(
                tenant.TenantId,
                tenant.UserId,
                "SalesOrder.Released",
                nameof(SalesOrder),
                order.Id,
                payload,
                httpContext.TraceIdentifier));
            db.IdempotencyRecords.Add(IdempotencyRecord.Create(tenant.TenantId, idempotencyKey, Idempotency.ReleaseSalesOrder, order.Id, null));

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(order));
        }).Produces<SalesOrderResponse>();

        api.MapPost("/sales-orders/{id:guid}/cancel", async (
            Guid id,
            HttpContext httpContext,
            ITenantContext tenant,
            ErpDbContext db,
            CancellationToken cancellationToken) =>
        {
            var idempotencyKey = Idempotency.RequireKey(httpContext);
            if (idempotencyKey is null) return Results.BadRequest(new { error = "Idempotency-Key header is required." });

            var existing = await db.IdempotencyRecords.AsNoTracking()
                .SingleOrDefaultAsync(record => record.Operation == Idempotency.CancelSalesOrder && record.Key == idempotencyKey, cancellationToken);
            if (existing is not null) return Results.Ok(new { id = existing.ResourceId, replayed = true });

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            if (!await SalesOrderRowLock.TryLockAsync(db, id, cancellationToken))
            {
                return Results.NotFound();
            }

            var order = await db.SalesOrders.Include(item => item.Lines)
                .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (order is null) return Results.NotFound();

            if (await db.Deliveries.AnyAsync(delivery => delivery.SalesOrderId == order.Id, cancellationToken))
            {
                throw new InvalidOperationException("Delivered orders cannot be cancelled.");
            }

            if (order.Status == SalesOrderStatus.Released)
            {
                foreach (var line in order.Lines)
                {
                    var stock = await db.StockBalances.SingleOrDefaultAsync(
                        balance => balance.ArticleId == line.ArticleId && balance.WarehouseCode == order.WarehouseCode,
                        cancellationToken);
                    if (stock is null)
                    {
                        throw new InvalidOperationException($"No stock balance for article {line.Sku} in warehouse {order.WarehouseCode}.");
                    }

                    stock.ReleaseReservation(line.Quantity);
                }
            }

            var cancelled = order.Cancel();
            const string eventType = "erp.sales-order.cancelled.v1";
            var payload = JsonSerializer.Serialize(new
            {
                eventId = cancelled.EventId,
                eventType,
                timestamp = cancelled.OccurredAt,
                tenantId = cancelled.TenantId,
                salesOrderId = cancelled.SalesOrderId,
                salesOrderNumber = cancelled.SalesOrderNumber,
                warehouseCode = cancelled.WarehouseCode,
                lines = cancelled.Lines
            });

            db.OutboxMessages.Add(OutboxMessage.From(
                tenant.TenantId,
                eventType,
                payload,
                cancelled.EventId,
                cancelled.OccurredAt,
                httpContext.TraceIdentifier,
                tenant.UserId));
            db.AuditEntries.Add(AuditEntry.Create(
                tenant.TenantId,
                tenant.UserId,
                "SalesOrder.Cancelled",
                nameof(SalesOrder),
                order.Id,
                payload,
                httpContext.TraceIdentifier));
            db.IdempotencyRecords.Add(IdempotencyRecord.Create(tenant.TenantId, idempotencyKey, Idempotency.CancelSalesOrder, order.Id, null));

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(order));
        }).Produces<SalesOrderResponse>();

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
            order.WarehouseCode,
            order.NetAmount,
            order.Lines.Select(line => new SalesOrderLineResponse(line.ArticleId, line.Sku, line.Quantity, line.UnitPrice)).ToArray());

    private static DeliveryResponse ToDeliveryResponse(Delivery delivery) =>
        new(
            delivery.Id,
            delivery.SalesOrderId,
            delivery.Number,
            new AddressSnapshot(
                delivery.ShipToName,
                delivery.ShipToStreet,
                delivery.ShipToPostalCode,
                delivery.ShipToCity,
                delivery.ShipToCountryCode,
                delivery.ShipToAdditionalLine),
            delivery.CreatedAt);

    private static InvoiceResponse ToInvoiceResponse(Invoice invoice) =>
        new(invoice.Id, invoice.SalesOrderId, invoice.Number, invoice.NetAmount, invoice.CreatedAt);

    private static bool TryParseOrderCursor(string after, out DateTimeOffset createdAt, out Guid id)
    {
        createdAt = default;
        id = default;
        var separator = after.LastIndexOf('/');
        if (separator <= 0 || separator == after.Length - 1) return false;
        return DateTimeOffset.TryParse(
                   after[..separator],
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind,
                   out createdAt)
               && Guid.TryParse(after[(separator + 1)..], out id);
    }
}

public sealed record CreateSalesOrderRequest(
    Guid CustomerId,
    Guid ShipToAddressId,
    string? OrderNumber,
    IReadOnlyCollection<CreateSalesOrderLineRequest> Lines,
    string? WarehouseCode = null);
public sealed record CreateSalesOrderLineRequest(Guid ArticleId, decimal Quantity);
public sealed record SalesOrderSummaryResponse(
    Guid Id,
    string OrderNumber,
    Guid CustomerId,
    string Status,
    decimal NetAmount,
    DateTimeOffset CreatedAt);
public sealed record SalesOrderResponse(
    Guid Id,
    string OrderNumber,
    Guid CustomerId,
    Guid ShipToAddressId,
    AddressSnapshot ShipTo,
    string Status,
    string WarehouseCode,
    decimal NetAmount,
    IReadOnlyCollection<SalesOrderLineResponse> Lines);
public sealed record SalesOrderLineResponse(Guid ArticleId, string Sku, decimal Quantity, decimal UnitPrice);
public sealed record DeliveryResponse(
    Guid Id,
    Guid SalesOrderId,
    string Number,
    AddressSnapshot ShipTo,
    DateTimeOffset CreatedAt);
public sealed record InvoiceResponse(
    Guid Id,
    Guid SalesOrderId,
    string Number,
    decimal NetAmount,
    DateTimeOffset CreatedAt);
