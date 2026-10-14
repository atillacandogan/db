using DbScanner.Application;
using DbScanner.Infrastructure;
using DbScanner.Infrastructure.Observability;
using DbScanner.ScannerWorker;
using DbScanner.Infrastructure.Persistence;
using DbScanner.Application.Models;
using DbScanner.Infrastructure.Identity;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDbScannerTelemetry(builder.Configuration, "DbScanner.ScannerWorker");
builder.Services
    .AddDbScannerApplication()
    .AddDbScannerInfrastructure(builder.Configuration)
    .AddScanJobReliabilityWorkers();
builder.Services.AddScoped(_ => ResourceAccessContext.ForSystem());
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.Services.VerifyDbScannerTenantBeforeMigrationAsync();
if (builder.Configuration.GetValue("Persistence:ApplyMigrations", true))
    await host.Services.ApplyDbScannerMigrationsAsync();
await host.Services.VerifyDbScannerTenantAsync();
host.Run();
