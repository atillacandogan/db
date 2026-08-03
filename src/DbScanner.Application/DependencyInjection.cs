using DbScanner.Application.Ports;
using DbScanner.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DbScanner.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddDbScannerApplication(this IServiceCollection services)
    {
        services.AddSingleton<IScoringService, ScoringService>();
        services.AddSingleton<IRuleEngine, RuleEngine>();
        services.AddScoped<IScanService, ScanService>();
        services.AddScoped<IScanProcessingService, ScanProcessingService>();
        services.AddScoped<ITargetService, TargetService>();
        services.AddScoped<IDiscoveryService, DiscoveryService>();
        services.AddScoped<IRuleCatalogService, RuleCatalogService>();
        services.AddScoped<INseScriptCatalogService, NseScriptCatalogService>();
        services.AddScoped<IFindingCatalogService, FindingCatalogService>();

        return services;
    }
}
