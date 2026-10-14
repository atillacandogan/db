using DbScanner.Application.Ports;
using DbScanner.Application.Models;
using DbScanner.Infrastructure.Messaging;
using DbScanner.Infrastructure.Persistence;
using DbScanner.Infrastructure.Repositories;
using DbScanner.Infrastructure.Scanning;
using DbScanner.Infrastructure.Targets;
using DbScanner.Infrastructure.Discovery;
using DbScanner.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DbScanner.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddDbScannerInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("dbscanner")
            ?? configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Configure ConnectionStrings:dbscanner (Aspire) or ConnectionStrings:DefaultConnection using environment variables or User Secrets. No default database credential is provided.");

        services.AddScoped<ResourceAccessContext>(); // deny by default; API or worker must establish authority
        services.AddScoped<ITenantDeploymentVerifier, TenantDeploymentVerifier>();
        services.AddOptions<TenantIsolationOptions>()
            .Bind(configuration.GetSection(TenantIsolationOptions.SectionName))
            .Validate(o => o.IsValid(), "TenantIsolation requires a safe, non-local dedicated tenant ID and claim name.")
            .ValidateOnStart();

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
        services.PostConfigure<RabbitMqOptions>(queues =>
        {
            var tenancy = configuration.GetSection(TenantIsolationOptions.SectionName).Get<TenantIsolationOptions>() ?? new();
            tenancy.NamespaceQueues(queues);
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

            if (bool.TryParse(scannerSection["UseStrategyOrchestrator"], out var useStrategies)
                && !useStrategies)
            {
                throw new InvalidOperationException(
                    "Scanner:UseStrategyOrchestrator=false is no longer supported. " +
                    "Stage 15 requires the strategy execution path.");
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
        var keycloakAdminSection = configuration.GetSection(KeycloakAdminOptions.SectionName);
        services.AddOptions<KeycloakAdminOptions>()
            .Bind(keycloakAdminSection)
            .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _),
                "KeycloakAdmin:BaseUrl must be an absolute URL.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Realm),
                "KeycloakAdmin:Realm is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ClientId),
                "KeycloakAdmin:ClientId is required.")
            .Validate(options => options.RequestTimeoutSeconds is >= 1 and <= 120,
                "KeycloakAdmin:RequestTimeoutSeconds must be between 1 and 120.")
            .ValidateOnStart();
        var keycloakAdminBaseUrl = keycloakAdminSection[nameof(KeycloakAdminOptions.BaseUrl)]
            ?? "http://localhost:18080";
        var keycloakAdminTimeoutSeconds = int.TryParse(
            keycloakAdminSection[nameof(KeycloakAdminOptions.RequestTimeoutSeconds)],
            out var configuredKeycloakTimeout)
            ? configuredKeycloakTimeout
            : 15;
        services.AddSingleton<IKeycloakAdminService>(serviceProvider =>
            new KeycloakAdminService(
                new HttpClient
                {
                    BaseAddress = new Uri(keycloakAdminBaseUrl.TrimEnd('/') + "/"),
                    Timeout = TimeSpan.FromSeconds(keycloakAdminTimeoutSeconds)
                },
                serviceProvider.GetRequiredService<IOptions<KeycloakAdminOptions>>(),
                serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<KeycloakAdminService>>()));
        var discoverySection = configuration.GetSection(DiscoveryOptions.SectionName);
        services.AddOptions<DiscoveryOptions>()
            .Configure(options =>
            {
                // ConfigurationBinder appends configured arrays to arrays already present in a
                // dictionary initializer. Clear only this configured collection so JSON/env values
                // replace the safe defaults instead of producing duplicate ports.
                if (discoverySection.GetSection(nameof(DiscoveryOptions.LocalNetworkPorts)).GetChildren().Any())
                {
                    options.LocalNetworkPorts = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
                }

                if (discoverySection.GetSection(nameof(DiscoveryOptions.UnsupportedDatabasePorts)).GetChildren().Any())
                {
                    options.UnsupportedDatabasePorts = [];
                }
            })
            .Bind(discoverySection)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DiscoveryOptions>, DiscoveryOptionsValidator>();
        services.AddSingleton<RabbitMqConnectionFactory>();
        services.AddScoped<IScanJobPublisher, OutboxScanJobPublisher>();
        services.AddScoped<ScanExecutionLeaseContext>();
        services.AddScoped<ScanJobReliabilityStore>();
        services.AddScoped<IScanJobLeaseStore>(provider => provider.GetRequiredService<ScanJobReliabilityStore>());
        services.AddSingleton<ScanJobHeartbeat>();
        services.AddSingleton<IScanOutboxTransport, RabbitMqScanOutboxTransport>();
        services.AddOptions<ScanReliabilityOptions>()
            .Bind(configuration.GetSection(ScanReliabilityOptions.SectionName))
            .Validate(value => value.HeartbeatSeconds > 0 && value.LeaseSeconds >= value.HeartbeatSeconds * 3
                && value.RecoveryIntervalSeconds > 0 && value.MaxExecutionAttempts > 0
                && value.RetryDelaySeconds >= 0 && value.OutboxPollSeconds > 0
                && value.PublishTimeoutSeconds > 0 && value.DispatchLeaseSeconds > value.PublishTimeoutSeconds + 5,
                "ScanReliability intervals/attempts must be positive; leases must exceed heartbeat/publish timeouts.")
            .ValidateOnStart();
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
        services.AddSingleton<INseScriptScanCheckRouter, NseScriptScanCheckRouter>();
        services.AddSingleton<IScanExecutorStrategy, NmapNseScanExecutorStrategy>();
        services.AddSingleton<IScanExecutorStrategy, MySqlNativeScanExecutorStrategy>();
        services.AddSingleton<IScanExecutorStrategy, MsSqlNativeScanExecutorStrategy>();
        services.AddSingleton<IScanExecutorStrategy, OracleNativeScanExecutorStrategy>();
        services.AddSingleton<IScannerRuntime, StrategyScannerRuntime>();
        services.AddSingleton<ITargetConnectionStringParser, TargetConnectionStringParser>();
        services.AddScoped<INseScriptSyncService, InstalledNseScriptSyncService>();
        services.AddSingleton<IDiscoveryProbe, TcpDiscoveryProbe>();
        services.AddSingleton<IDiscoveryDnsResolver, DiscoveryDnsResolver>();
        services.AddSingleton<ILocalNetworkProvider, LocalNetworkProvider>();
        services.AddSingleton<IDiscoveryConfiguration, DiscoveryConfiguration>();
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
        services.AddScoped<IScanCompatibilityReadinessService, ScanCompatibilityReadinessService>();
        services.AddScoped<IScanScopeRepository, ScanScopeRepository>();
        services.AddScoped<IFindingRepository, FindingRepository>();
        services.AddScoped<IDiscoveryRepository, DiscoveryRepository>();
        services.AddScoped<IUserManagementAuditRepository, UserManagementAuditRepository>();

        return services;
    }

    public static IServiceCollection AddScanJobReliabilityWorkers(this IServiceCollection services)
    {
        services.AddHostedService<ScanOutboxDispatcher>();
        services.AddHostedService<ScanLeaseRecoveryService>();
        return services;
    }
}
