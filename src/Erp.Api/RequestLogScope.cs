using System.Collections;
using Erp.Observability;

namespace Erp.Api;

internal sealed class RequestLogScope(HttpContext context) : IReadOnlyList<KeyValuePair<string, object>>
{
    public int Count => 3;

    public KeyValuePair<string, object> this[int index] => index switch
    {
        0 => new(RequestLog.CorrelationId, context.TraceIdentifier),
        1 => new(RequestLog.TenantId, context.Items[RequestLog.TenantId]?.ToString() ?? string.Empty),
        2 => new(RequestLog.UserId, context.Items[RequestLog.UserId]?.ToString() ?? string.Empty),
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
        {
            yield return this[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
