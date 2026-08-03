using DbScanner.Application;
using DbScanner.Infrastructure;
using DbScanner.ScannerWorker;
using DbScanner.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddDbScannerApplication()
    .AddDbScannerInfrastructure(builder.Configuration);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.Services.ApplyDbScannerMigrationsAsync();
host.Run();
