using DbScanner.Application.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using System.Reflection;

namespace DbScanner.Infrastructure.Observability;

public static class DbScannerTelemetryExtensions
{
    public static IServiceCollection AddDbScannerTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        var serviceVersion = configuration["Telemetry:DeploymentVersion"]
            ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
            ?? "unknown";
        var environmentName = configuration["Telemetry:Environment"]
            ?? configuration["DOTNET_ENVIRONMENT"]
            ?? configuration["ASPNETCORE_ENVIRONMENT"]
            ?? "unknown";
        var operationsEndpoint = configuration["Telemetry:CompatibilityOtlpEndpoint"];

        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName,
                serviceVersion: serviceVersion,
                serviceInstanceId: $"{Environment.MachineName}-{Environment.ProcessId}")
                .AddAttributes([
                    new KeyValuePair<string, object>(
                        "deployment.environment.name",
                        environmentName)
                ]))
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(ScanCompatibilityTelemetry.MeterName)
                    .AddOtlpExporter();

                if (Uri.TryCreate(operationsEndpoint, UriKind.Absolute, out var endpoint))
                {
                    metrics.AddOtlpExporter(
                        "scan-compatibility-operations",
                        options =>
                        {
                            options.Endpoint = endpoint;
                            options.Protocol = OtlpExportProtocol.Grpc;
                        });
                }
            });
        services.AddHostedService<ScanCompatibilityReadinessMonitor>();

        return services;
    }
}
