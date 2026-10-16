using System.Diagnostics;
using Erp.Observability;
using Microsoft.Extensions.Logging;

namespace Erp.Api.Tests;

public sealed class OutboxTelemetryTests
{
    private static readonly Lock ConsoleGate = new();

    [Fact]
    public void Json_log_includes_correlation_tenant_and_user()
    {
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        string json;
        lock (ConsoleGate)
        {
            using var writer = new StringWriter();
            var previous = Console.Out;
            Console.SetOut(writer);
            try
            {
                using var factory = LoggerFactory.Create(logging => logging.AddErpJsonLogging());
                var logger = factory.CreateLogger("Erp.Request");
                using (logger.BeginScope(RequestLog.Scope("corr-42", tenantId, "user-7")))
                {
                    logger.LogInformation("Handled {Method} {Path}.", "POST", "/api/v1/sales-orders/1/release");
                }
            }
            finally
            {
                Console.SetOut(previous);
            }

            json = writer.ToString();
        }

        Assert.Contains("\"CorrelationId\":\"corr-42\"", json, StringComparison.Ordinal);
        Assert.Contains($"\"TenantId\":\"{tenantId}\"", json, StringComparison.Ordinal);
        Assert.Contains("\"UserId\":\"user-7\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Dead_letter_count_above_zero_is_the_alarm_signal()
    {
        Assert.False(new OutboxSnapshot(4, 30, 0).RaisesDeadLetterAlarm);
        var breached = new OutboxSnapshot(4, 30, 1);
        Assert.True(breached.RaisesDeadLetterAlarm);

        using var document = System.Text.Json.JsonDocument.Parse(
            OutboxMetricsDocument.ToJson(breached, DateTimeOffset.UnixEpoch));
        var metric = document.RootElement.GetProperty("_aws").GetProperty("CloudWatchMetrics")[0];
        Assert.Equal(OutboxMetricsDocument.Namespace, metric.GetProperty("Namespace").GetString());
        Assert.Equal(1, document.RootElement.GetProperty(OutboxMetricsDocument.DeadLetterCount).GetInt32());
        Assert.Equal("worker", document.RootElement.GetProperty(OutboxMetricsDocument.ServiceDimension).GetString());
    }

    [Fact]
    public void Worker_activity_carries_the_request_correlation()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ErpActivities.WorkerSourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = ErpActivities.Worker.StartActivity("outbox.process", ActivityKind.Consumer);
        Assert.NotNull(activity);
        activity.SetTag("correlation.id", "corr-42");
        Assert.Equal("corr-42", activity.GetTagItem("correlation.id"));
    }
}
