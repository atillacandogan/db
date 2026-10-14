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
        services.AddSingleton<IScanExecutorStrategyResolver, ScanExecutorStrategyResolver>();
        services.AddSingleton<IScanExecutionOrchestrator, ScanExecutionOrchestrator>();
        services.AddScoped<IScanService, ScanService>();
        services.AddScoped<IScanProcessingService, ScanProcessingService>();
        services.AddScoped<ITargetService, TargetService>();
        services.AddScoped<IDiscoveryService, DiscoveryService>();
        services.AddScoped<IDiscoveryAllowlistService, DiscoveryAllowlistService>();
        services.AddScoped<IRuleCatalogService, RuleCatalogService>();
        services.AddScoped<INseScriptCatalogService, NseScriptCatalogService>();
        services.AddScoped<IScanCheckCatalogService, ScanCheckCatalogService>();
        services.AddScoped<IFindingCatalogService, FindingCatalogService>();
        services.AddScoped<IAdminUserService, AdminUserService>();

        return services;
    }
}
