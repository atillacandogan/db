using DbScanner.Application.Ports;
using DbScanner.Infrastructure.Messaging;
using DbScanner.Infrastructure.Persistence;
using DbScanner.Infrastructure.Repositories;
using DbScanner.Infrastructure.Scanning;
using DbScanner.Infrastructure.Targets;
using DbScanner.Infrastructure.Discovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DbScanner.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddDbScannerInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("dbscanner")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=dbscanner;Username=postgres;Password=postgres";

        services.AddDbContext<DbScannerDbContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptions =>
                npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));

        var rabbitMqSection = configuration.GetSection(RabbitMqOptions.SectionName);
        services.Configure<RabbitMqOptions>(options =>
        {
            options.RequestedQueue = rabbitMqSection[nameof(RabbitMqOptions.RequestedQueue)] ?? options.RequestedQueue;
            options.RetryQueue = rabbitMqSection[nameof(RabbitMqOptions.RetryQueue)] ?? options.RetryQueue;
            options.DeadLetterQueue = rabbitMqSection[nameof(RabbitMqOptions.DeadLetterQueue)] ?? options.DeadLetterQueue;
            options.NseScriptSyncQueue = rabbitMqSection[nameof(RabbitMqOptions.NseScriptSyncQueue)] ?? options.NseScriptSyncQueue;
            options.DiscoveryRequestedQueue = rabbitMqSection[nameof(RabbitMqOptions.DiscoveryRequestedQueue)] ?? options.DiscoveryRequestedQueue;
            options.DiscoveryDeadLetterQueue = rabbitMqSection[nameof(RabbitMqOptions.DiscoveryDeadLetterQueue)] ?? options.DiscoveryDeadLetterQueue;

            if (ushort.TryParse(rabbitMqSection[nameof(RabbitMqOptions.PrefetchCount)], out var prefetchCount))
            {
                options.PrefetchCount = prefetchCount;
            }

            if (int.TryParse(rabbitMqSection[nameof(RabbitMqOptions.RetryDelayMilliseconds)], out var retryDelayMilliseconds))
            {
                options.RetryDelayMilliseconds = retryDelayMilliseconds;
            }

            if (int.TryParse(rabbitMqSection[nameof(RabbitMqOptions.MaxRetryCount)], out var maxRetryCount))
            {
                options.MaxRetryCount = maxRetryCount;
            }

            if (int.TryParse(rabbitMqSection[nameof(RabbitMqOptions.NseScriptSyncTimeoutSeconds)], out var syncTimeoutSeconds))
            {
                options.NseScriptSyncTimeoutSeconds = syncTimeoutSeconds;
            }
        });
        services.Configure<ScannerOptions>(scannerOptions =>
        {
            var scannerSection = configuration.GetSection(ScannerOptions.SectionName);
            scannerOptions.ExecutablePath = scannerSection[nameof(ScannerOptions.ExecutablePath)] ?? scannerOptions.ExecutablePath;

            if (int.TryParse(scannerSection[nameof(ScannerOptions.TimeoutSeconds)], out var timeoutSeconds))
            {
                scannerOptions.TimeoutSeconds = timeoutSeconds;
            }

            if (bool.TryParse(scannerSection[nameof(ScannerOptions.UseSampleOutputWhenUnavailable)], out var useSample))
            {
                scannerOptions.UseSampleOutputWhenUnavailable = useSample;
            }
        });
        services.Configure<NseScriptDiscoveryOptions>(options =>
        {
            var discoverySection = configuration.GetSection(NseScriptDiscoveryOptions.SectionName);
            options.ScriptDirectory = discoverySection[nameof(NseScriptDiscoveryOptions.ScriptDirectory)] ?? options.ScriptDirectory;
        });
        services.Configure<CredentialProtectionOptions>(options =>
        {
            var protectionSection = configuration.GetSection(CredentialProtectionOptions.SectionName);
            options.Key = protectionSection[nameof(CredentialProtectionOptions.Key)];
            options.KeyFile = protectionSection[nameof(CredentialProtectionOptions.KeyFile)];
        });
        services.Configure<DiscoveryOptions>(options =>
        {
            var section = configuration.GetSection(DiscoveryOptions.SectionName);
            if (int.TryParse(section[nameof(DiscoveryOptions.ConnectTimeoutMilliseconds)], out var connectTimeout))
                options.ConnectTimeoutMilliseconds = Math.Clamp(connectTimeout, 100, 5000);
            if (int.TryParse(section[nameof(DiscoveryOptions.BannerReadTimeoutMilliseconds)], out var readTimeout))
                options.BannerReadTimeoutMilliseconds = Math.Clamp(readTimeout, 50, 1000);
            if (int.TryParse(section[nameof(DiscoveryOptions.MaxConcurrency)], out var concurrency))
                options.MaxConcurrency = Math.Clamp(concurrency, 1, 32);
            if (int.TryParse(section[nameof(DiscoveryOptions.MaxProbesPerSecond)], out var rate))
                options.MaxProbesPerSecond = Math.Clamp(rate, 1, 100);
            if (int.TryParse(section[nameof(DiscoveryOptions.MaxHosts)], out var maxHosts))
                options.MaxHosts = Math.Clamp(maxHosts, 1, 256);
        });
        services.AddSingleton<RabbitMqConnectionFactory>();
        services.AddScoped<IScanJobPublisher, RabbitMqScanJobPublisher>();
        services.AddSingleton<IScanJobConsumer, RabbitMqScanJobConsumer>();
        services.AddScoped<INseScriptSyncRequester, RabbitMqNseScriptSyncRequester>();
        services.AddSingleton<INseScriptSyncConsumer, RabbitMqNseScriptSyncConsumer>();
        services.AddScoped<IDiscoveryJobPublisher, RabbitMqDiscoveryJobPublisher>();
        services.AddSingleton<IDiscoveryJobConsumer, RabbitMqDiscoveryJobConsumer>();
        services.AddSingleton<IMySqlEmptyPasswordProbe, MySqlEmptyPasswordProbe>();
        services.AddSingleton<IMsSqlAuthenticatedProbe, MsSqlAuthenticatedProbe>();
        services.AddSingleton<IMsSqlBrowserProbe, MsSqlBrowserProbe>();
        services.AddSingleton<IOracleAuthenticatedProbe, OracleAuthenticatedProbe>();
        services.AddSingleton<IScanCredentialProtector, AesGcmScanCredentialProtector>();
        services.AddSingleton<NmapScannerRuntime>();
        services.AddSingleton<ScriptCompatibilityScannerRuntime>();
        services.AddSingleton<MsSqlScannerRuntime>();
        services.AddSingleton<IScannerRuntime, OracleScannerRuntime>();
        services.AddSingleton<ITargetConnectionStringParser, TargetConnectionStringParser>();
        services.AddScoped<INseScriptSyncService, InstalledNseScriptSyncService>();
        services.AddSingleton<IDiscoveryProbe, TcpDiscoveryProbe>();
        services.AddSingleton<IDatabaseFingerprintDetector, MySqlDiscoveryDetector>();
        services.AddSingleton<IDatabaseFingerprintDetector, PostgreSqlDiscoveryDetector>();
        services.AddSingleton<IDatabaseFingerprintDetector, MsSqlDiscoveryDetector>();
        services.AddSingleton<IDatabaseFingerprintDetector, OracleDiscoveryDetector>();
        services.AddSingleton<IDatabaseFingerprintDetector, MongoDbDiscoveryDetector>();
        services.AddSingleton<IDatabaseFingerprintDetector, RedisDiscoveryDetector>();
        services.AddSingleton<IDatabaseFingerprintDetector, UnknownTcpDiscoveryDetector>();
        services.AddSingleton<IDiscoveryResultClassifier, DiscoveryResultClassifier>();
        services.AddScoped<IDiscoveryScopeValidator, DiscoveryScopeValidator>();
        services.AddScoped<IDiscoveryAgent, DiscoveryAgent>();

        services.AddScoped<ITargetRepository, TargetRepository>();
        services.AddScoped<IScanRepository, ScanRepository>();
        services.AddScoped<IRuleRepository, RuleRepository>();
        services.AddScoped<INseScriptRepository, NseScriptRepository>();
        services.AddScoped<IScanScopeRepository, ScanScopeRepository>();
        services.AddScoped<IFindingRepository, FindingRepository>();
        services.AddScoped<IDiscoveryRepository, DiscoveryRepository>();

        return services;
    }
}
