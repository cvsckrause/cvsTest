namespace Erp.Api;

internal static class Idempotency
{
    public const string CreateSalesOrder = "CreateSalesOrder";
    public const string ReleaseSalesOrder = "ReleaseSalesOrder";
    public const string StockGoodsReceipt = "StockGoodsReceipt";

    public static string? RequireKey(HttpContext context)
    {
        var value = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static string? OperationFor(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method)) return null;

        var path = request.Path.Value ?? string.Empty;
        if (path.EndsWith("/sales-orders", StringComparison.OrdinalIgnoreCase))
            return CreateSalesOrder;
        if (path.EndsWith("/release", StringComparison.OrdinalIgnoreCase)
            && path.Contains("/sales-orders/", StringComparison.OrdinalIgnoreCase))
            return ReleaseSalesOrder;
        if (path.EndsWith("/stock/receipts", StringComparison.OrdinalIgnoreCase))
            return StockGoodsReceipt;

        return null;
    }
}
