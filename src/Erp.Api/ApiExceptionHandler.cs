using Erp.Persistence;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api;

internal sealed class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var decision = ApiProblemDecision.From(exception);
        if (decision.Replay)
        {
            if (await TryWriteReplayAsync(httpContext, cancellationToken))
                return true;

            decision = ApiProblemDecision.Conflict(ApiProblemDecision.RetryDetail);
        }

        if (decision.Status is null)
        {
            decision = new ApiProblemDecision(
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred.",
                "The request could not be completed.",
                false);
        }

        await Results.Problem(
            title: decision.Title,
            detail: decision.Detail,
            statusCode: decision.Status).ExecuteAsync(httpContext);
        return true;
    }

    private static async Task<bool> TryWriteReplayAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var key = Idempotency.RequireKey(httpContext);
        var operation = Idempotency.OperationFor(httpContext.Request);
        if (key is null || operation is null) return false;

        var currentTenant = httpContext.RequestServices.GetRequiredService<RequestTenantContext>();
        await using var scope = httpContext.RequestServices.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestTenantContext>().Set(currentTenant.TenantId, currentTenant.UserId);
        var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var existing = await db.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(record => record.Operation == operation && record.Key == key, cancellationToken);
        if (existing is null) return false;

        if (Idempotency.Conflicts(existing.Fingerprint, Idempotency.Stashed(httpContext)))
        {
            await Idempotency.Mismatch().ExecuteAsync(httpContext);
            return true;
        }

        if (operation == Idempotency.StockGoodsReceipt)
        {
            var movement = await db.StockMovements.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == existing.ResourceId, cancellationToken);
            if (movement is null)
            {
                await Results.Ok(new { id = existing.ResourceId, replayed = true }).ExecuteAsync(httpContext);
                return true;
            }

            var article = await db.Articles.AsNoTracking()
                .SingleAsync(item => item.Id == movement.ArticleId, cancellationToken);
            await Results.Ok(new StockReceiptResponse(
                movement.Id,
                movement.ArticleId,
                article.Sku,
                movement.WarehouseCode,
                movement.Quantity,
                movement.Reference,
                movement.OccurredAt,
                Replayed: true)).ExecuteAsync(httpContext);
            return true;
        }

        await Results.Ok(new { id = existing.ResourceId, replayed = true }).ExecuteAsync(httpContext);
        return true;
    }
}
