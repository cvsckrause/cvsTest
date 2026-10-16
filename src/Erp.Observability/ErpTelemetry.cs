using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Erp.Observability;

public static class ErpTelemetry
{
    public static void AddErpTelemetry(
        this IServiceCollection services,
        string serviceName,
        IConfiguration configuration,
        Action<TracerProviderBuilder>? configureTracing = null,
        Action<MeterProviderBuilder>? configureMetrics = null)
    {
        var telemetry = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName));

        telemetry.WithTracing(tracing =>
        {
            tracing.AddSource(ErpActivities.WorkerSourceName);
            configureTracing?.Invoke(tracing);
            if (HasOtlpEndpoint(configuration))
            {
                tracing.AddOtlpExporter();
            }
        });

        telemetry.WithMetrics(metrics =>
        {
            metrics.AddMeter(OutboxTelemetry.MeterName);
            metrics.AddRuntimeInstrumentation();
            configureMetrics?.Invoke(metrics);
            if (HasOtlpEndpoint(configuration))
            {
                metrics.AddOtlpExporter();
            }
        });
    }

    // CloudWatch receives the outbox gauges as EMF on stdout. Traces and the
    // remaining meters leave the process only when a collector endpoint is set.
    private static bool HasOtlpEndpoint(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
}
