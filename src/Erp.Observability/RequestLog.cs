using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Erp.Observability;

public static class RequestLog
{
    public const string CorrelationId = "CorrelationId";
    public const string TenantId = "TenantId";
    public const string UserId = "UserId";

    public static Dictionary<string, object> Scope(string correlationId, Guid tenantId, string userId) =>
        new()
        {
            [CorrelationId] = correlationId,
            [TenantId] = tenantId.ToString(),
            [UserId] = userId,
        };
}

public static class JsonLogging
{
    public static void AddErpJsonLogging(this ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.JsonWriterOptions = new JsonWriterOptions { Indented = false };
        });
    }
}
