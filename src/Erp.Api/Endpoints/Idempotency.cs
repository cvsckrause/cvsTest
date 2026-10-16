using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Erp.Api;

internal static class Idempotency
{
    public const string FingerprintItem = "Erp.IdempotencyFingerprint";

    private static readonly JsonSerializerOptions FingerprintJson = new(JsonSerializerDefaults.Web);
    public const string CreateSalesOrder = "CreateSalesOrder";
    public const string ReleaseSalesOrder = "ReleaseSalesOrder";
    public const string CancelSalesOrder = "CancelSalesOrder";
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
        if (path.EndsWith("/cancel", StringComparison.OrdinalIgnoreCase)
            && path.Contains("/sales-orders/", StringComparison.OrdinalIgnoreCase))
            return CancelSalesOrder;
        if (path.EndsWith("/stock/receipts", StringComparison.OrdinalIgnoreCase))
            return StockGoodsReceipt;

        return null;
    }

    public static string EmptyFingerprint { get; } = FingerprintOf(new EmptyDocument());

    public static string FingerprintOf(object body)
    {
        var json = JsonSerializer.Serialize(body, FingerprintJson);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static void Stash(HttpContext httpContext, string fingerprint) =>
        httpContext.Items[FingerprintItem] = fingerprint;

    public static string? Stashed(HttpContext httpContext) =>
        httpContext.Items.TryGetValue(FingerprintItem, out var value) ? value as string : null;

    public static bool Conflicts(string? stored, string? incoming) =>
        stored is not null && !string.Equals(stored, incoming, StringComparison.Ordinal);

    public static IResult Mismatch() =>
        Results.Problem(
            title: "The idempotency key was reused with a different request.",
            detail: "This Idempotency-Key was already used for a different request.",
            statusCode: StatusCodes.Status422UnprocessableEntity);

    private sealed class EmptyDocument;
}
