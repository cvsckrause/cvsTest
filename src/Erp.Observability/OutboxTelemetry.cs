using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;

namespace Erp.Observability;

public static class ErpActivities
{
    public const string WorkerSourceName = "Erp.Worker";

    public static readonly ActivitySource Worker = new(WorkerSourceName);
}

public sealed record OutboxSnapshot(int Backlog, double OldestPendingAgeSeconds, int DeadLetterCount)
{
    public bool RaisesDeadLetterAlarm => DeadLetterCount > OutboxMetricsDocument.DeadLetterThreshold;
}

public static class OutboxMetricsDocument
{
    public const string Namespace = "Erp/Outbox";
    public const string Backlog = "Backlog";
    public const string OldestPendingAgeSeconds = "OldestPendingAgeSeconds";
    public const string DeadLetterCount = "DeadLetterCount";
    public const string ServiceDimension = "Service";
    public const string WorkerService = "worker";
    public const double DeadLetterThreshold = 0;
    public const double OldestPendingAgeThresholdSeconds = 900;

    public static string ToJson(OutboxSnapshot snapshot, DateTimeOffset timestamp) =>
        JsonSerializer.Serialize(new
        {
            _aws = new
            {
                Timestamp = timestamp.ToUnixTimeMilliseconds(),
                CloudWatchMetrics = new[]
                {
                    new
                    {
                        Namespace,
                        Dimensions = new[] { new[] { ServiceDimension } },
                        Metrics = new object[]
                        {
                            new { Name = Backlog, Unit = "Count" },
                            new { Name = OldestPendingAgeSeconds, Unit = "Seconds" },
                            new { Name = DeadLetterCount, Unit = "Count" },
                        },
                    },
                },
            },
            Service = WorkerService,
            Backlog = snapshot.Backlog,
            OldestPendingAgeSeconds = snapshot.OldestPendingAgeSeconds,
            DeadLetterCount = snapshot.DeadLetterCount,
        });
}

public static class OutboxTelemetry
{
    public const string MeterName = "Erp.Outbox";

    private static readonly Meter Meter = new(MeterName);
    private static OutboxSnapshot _current = new(0, 0, 0);

    static OutboxTelemetry()
    {
        Meter.CreateObservableGauge(OutboxMetricsDocument.Backlog, () => _current.Backlog, unit: "{message}");
        Meter.CreateObservableGauge(
            OutboxMetricsDocument.OldestPendingAgeSeconds,
            () => _current.OldestPendingAgeSeconds,
            unit: "s");
        Meter.CreateObservableGauge(
            OutboxMetricsDocument.DeadLetterCount,
            () => _current.DeadLetterCount,
            unit: "{message}");
    }

    public static void Publish(OutboxSnapshot snapshot)
    {
        _current = snapshot;
        Console.Out.WriteLine(OutboxMetricsDocument.ToJson(snapshot, DateTimeOffset.UtcNow));
    }
}
