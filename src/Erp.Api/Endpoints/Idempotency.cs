namespace Erp.Api;

internal static class Idempotency
{
    public static string? RequireKey(HttpContext context)
    {
        var value = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
