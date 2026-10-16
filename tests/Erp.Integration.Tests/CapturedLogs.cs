using Microsoft.Extensions.Logging;

namespace Erp.Integration.Tests;

internal sealed class CapturedLogs : ILoggerProvider, ISupportExternalScope
{
    private readonly Lock _gate = new();
    private readonly List<CapturedLog> _entries = [];
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public IReadOnlyList<CapturedLog> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    public ILogger CreateLogger(string categoryName) => new Sink(this, categoryName);

    public void Dispose()
    {
    }

    private void Add(CapturedLog entry)
    {
        lock (_gate)
        {
            _entries.Add(entry);
        }
    }

    private sealed class Sink(CapturedLogs owner, string category) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
            owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var scopes = new List<KeyValuePair<string, object>>();
            owner._scopes.ForEachScope(static (scope, collected) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object>> pairs)
                {
                    collected.AddRange(pairs);
                }
            }, scopes);
            owner.Add(new CapturedLog(category, logLevel, formatter(state, exception), scopes));
        }
    }
}

internal sealed record CapturedLog(
    string Category,
    LogLevel Level,
    string Message,
    IReadOnlyList<KeyValuePair<string, object>> Scopes);
