using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

var builder = DistributedApplication.CreateBuilder(args);

const string MsSqlImage = "mcr.microsoft.com/mssql/server";
const string MsSqlImageTag = "2022-CU22-GDR1-ubuntu-22.04";
const string OracleImage = "gvenzl/oracle-free";
const string OracleImageTag = "23.26.1-slim";
const string KeycloakImage = "quay.io/keycloak/keycloak";
const string KeycloakImageTag = "26.7.0";

builder.Services
    .AddHealthChecks()
    .AddCheck<MsSqlReadinessHealthCheck>("demo-mssql-ready")
    .AddCheck<OracleReadinessHealthCheck>("demo-oracle-ready");

var applicationPostgres = builder
    .AddPostgres("application-postgres")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var applicationDatabase = applicationPostgres.AddDatabase("dbscanner");

var rabbitMq = builder
    .AddRabbitMQ("rabbitmq")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithManagementPlugin();

var keycloakPostgresUserName = builder.AddParameter(
    "keycloak-postgres-username",
    "keycloak");
var keycloakPostgres = builder
    .AddPostgres(
        "keycloak-postgres",
        userName: keycloakPostgresUserName)
    .WithImageTag("17.6")
    .WithDataVolume("dbscanner-keycloak-postgres-data")
    .WithLifetime(ContainerLifetime.Persistent);
var keycloakDatabase = keycloakPostgres.AddDatabase(
    "keycloak-database",
    databaseName: "keycloak");

var keycloakAdminPassword = builder.AddParameter("keycloak-admin-password", secret: true);
var keycloak = builder
    .AddContainer("keycloak", KeycloakImage, KeycloakImageTag)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", keycloakAdminPassword)
    .WithEnvironment("KC_DB", "postgres")
    .WithEnvironment(
        "KC_DB_URL",
        ReferenceExpression.Create(
            $"{keycloakPostgres.Resource.JdbcConnectionString}/keycloak"))
    .WithEnvironment("KC_DB_USERNAME", keycloakPostgresUserName)
    .WithEnvironment(
        "KC_DB_PASSWORD",
        ReferenceExpression.Create(
            $"{keycloakPostgres.Resource.PasswordParameter}"))
    .WithArgs("start-dev", "--import-realm")
    .WithBindMount(
        "../../docker/keycloak/import",
        "/opt/keycloak/data/import",
        isReadOnly: true)
    .WithHttpEndpoint(targetPort: 8080, port: 18080, name: "http", isProxied: false)
    .WithHttpHealthCheck("/realms/dbscanner", endpointName: "http")
    .WithReference(keycloakDatabase)
    .WaitFor(keycloakDatabase);

var demoPostgresPassword = builder.AddParameter("demo-postgres-password", "dbscanner", secret: true);
var demoPostgresTarget = builder
    .AddContainer("demo-postgres-target", "postgres", "16")
    .WithEnvironment("POSTGRES_USER", "dbscanner")
    .WithEnvironment("POSTGRES_PASSWORD", demoPostgresPassword)
    .WithEnvironment("POSTGRES_DB", "demo")
    .WithEndpoint(targetPort: 5432, port: 15432, scheme: "tcp", name: "postgres");

var demoMySqlPassword = builder.AddParameter("demo-mysql-password", "dbscanner", secret: true);
var demoMySqlTarget = builder
    .AddContainer("demo-mysql-target", "mysql", "8.4")
    .WithEnvironment("MYSQL_ROOT_PASSWORD", demoMySqlPassword)
    .WithEnvironment("MYSQL_DATABASE", "demo")
    .WithBindMount(
        "../../docker/mysql-vulnerable-init",
        "/docker-entrypoint-initdb.d",
        isReadOnly: true)
    .WithEndpoint(targetPort: 3306, port: 13306, scheme: "tcp", name: "mysql");

var demoMySqlSecurePassword = builder.AddParameter(
    "demo-mysql-secure-password",
    "SecureTest_2026!",
    secret: true);
var demoMySqlSecureTarget = builder
    .AddContainer("demo-mysql-secure-target", "mysql", "8.4")
    .WithEnvironment("MYSQL_ROOT_PASSWORD", demoMySqlSecurePassword)
    .WithEnvironment("MYSQL_DATABASE", "secure_demo")
    .WithEndpoint(targetPort: 3306, port: 13307, scheme: "tcp", name: "mysql");

var demoMySqlRootEmptyTarget = builder
    .AddContainer("demo-mysql-root-empty-target", "mysql", "8.4")
    .WithEnvironment("MYSQL_ALLOW_EMPTY_PASSWORD", "yes")
    .WithEnvironment("MYSQL_ROOT_HOST", "%")
    .WithEnvironment("MYSQL_DATABASE", "root_empty_demo")
    .WithEndpoint(targetPort: 3306, port: 13308, scheme: "tcp", name: "mysql");

var demoRedisUnsupportedTarget = builder
    .AddContainer("demo-redis-unsupported-target", "redis", "7.4-alpine")
    .WithEndpoint(targetPort: 6379, port: 16379, scheme: "tcp", name: "redis");

var demoMsSqlPassword = builder.AddParameter("demo-mssql-password", secret: true);
var demoMsSqlAppPassword = builder.AddParameter("demo-mssql-app-password", secret: true);
var demoMsSqlScannerPassword = builder.AddParameter("demo-mssql-scanner-password", secret: true);
var demoMsSqlRiskySettings = builder.AddParameter(
    "demo-mssql-risky-settings",
    "false");
var scannerCredentialProtectionKey = builder.AddParameter(
    "scanner-credential-protection-key",
    secret: true);
var demoMsSqlTarget = builder
    .AddContainer("demo-mssql-target", MsSqlImage, MsSqlImageTag)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithEnvironment("ACCEPT_EULA", "Y")
    .WithEnvironment("MSSQL_PID", "Developer")
    .WithEnvironment("MSSQL_SA_PASSWORD", demoMsSqlPassword)
    .WithVolume("dbscanner-mssql-data", "/var/opt/mssql")
    .WithEndpoint(targetPort: 1433, port: 11433, scheme: "tcp", name: "mssql")
    .WithHealthCheck("demo-mssql-ready");

var demoMsSqlInit = builder
    .AddContainer("demo-mssql-init", MsSqlImage, MsSqlImageTag)
    .WithEntrypoint("/bin/bash")
    .WithArgs(
        "-c",
        """
        set -euo pipefail
        export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"
        export DemoAppPasswordBase64="$(printf '%s' "$MSSQL_APP_PASSWORD" | base64 -w0)"
        export DemoScannerPasswordBase64="$(printf '%s' "$MSSQL_SCANNER_PASSWORD" | base64 -w0)"
        for attempt in $(seq 1 60); do
          if /opt/mssql-tools18/bin/sqlcmd -S demo-mssql-target -U sa -C -b -Q "SELECT 1" >/dev/null 2>&1; then
            /opt/mssql-tools18/bin/sqlcmd -S demo-mssql-target -U sa -C -b \
              -i /dbscanner-init/001-initialize-dbscanner-demo.sql
            exit 0
          fi
          sleep 2
        done
        echo "SQL Server did not become ready before initialization timeout." >&2
        exit 1
        """)
    .WithEnvironment("MSSQL_SA_PASSWORD", demoMsSqlPassword)
    .WithEnvironment("MSSQL_APP_PASSWORD", demoMsSqlAppPassword)
    .WithEnvironment("MSSQL_SCANNER_PASSWORD", demoMsSqlScannerPassword)
    .WithEnvironment("EnableRiskySettings", demoMsSqlRiskySettings)
    .WithBindMount("../../docker/mssql-init", "/dbscanner-init", isReadOnly: true)
    .WaitFor(demoMsSqlTarget);

var demoOraclePassword = builder.AddParameter("demo-oracle-password", secret: true);
var demoOracleScannerPassword = builder.AddParameter(
    "demo-oracle-scanner-password",
    secret: true);
var demoOracleTarget = builder
    .AddContainer("demo-oracle-target", OracleImage, OracleImageTag)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithEnvironment("ORACLE_PASSWORD", demoOraclePassword)
    .WithEnvironment("APP_USER", "dbscanner_scanner")
    .WithEnvironment("APP_USER_PASSWORD", demoOracleScannerPassword)
    .WithVolume("dbscanner-oracle-data", "/opt/oracle/oradata")
    .WithBindMount(
        "../../docker/oracle-init",
        "/container-entrypoint-initdb.d",
        isReadOnly: true)
    .WithEndpoint(targetPort: 1521, port: 11521, scheme: "tcp", name: "oracle")
    .WithHealthCheck("demo-oracle-ready");

var api = builder
    .AddExecutable(
        "api",
        "dotnet",
        "../DbScanner.Api",
        "run",
        "--project",
        "DbScanner.Api.csproj",
        "--no-build",
        "--launch-profile",
        "http")
    .WithHttpEndpoint(targetPort: 5166, port: 5166, name: "http", isProxied: false)
    .WithReference(applicationDatabase)
    .WithReference(rabbitMq)
    .WithEnvironment("Authentication__Authority", "http://localhost:18080/realms/dbscanner")
    .WithEnvironment("Authentication__Audience", "dbscanner-api")
    .WithEnvironment("Authentication__RequireHttpsMetadata", "false")
    .WithEnvironment("Discovery__MaxCidrHosts", "512")
    .WithEnvironment("CredentialProtection__Key", scannerCredentialProtectionKey)
    .WaitFor(applicationDatabase)
    .WaitFor(rabbitMq)
    .WaitFor(keycloak)
    .WaitForCompletion(demoMsSqlInit);

builder
    .AddDockerfile("scanner-worker", "../..", "src/DbScanner.ScannerWorker/Dockerfile")
    .WithReference(applicationDatabase)
    .WithReference(rabbitMq)
    .WithEnvironment("Scanner__ExecutablePath", "/usr/bin/nmap")
    .WithEnvironment("Scanner__UseSampleOutputWhenUnavailable", "false")
    .WithEnvironment("Discovery__ConnectTimeoutMilliseconds", "1000")
    .WithEnvironment("Discovery__BannerReadTimeoutMilliseconds", "1000")
    .WithEnvironment("Discovery__DnsTimeoutMilliseconds", "2000")
    .WithEnvironment("Discovery__JobTimeoutSeconds", "600")
    .WithEnvironment("Discovery__MaxConcurrency", "8")
    .WithEnvironment("Discovery__MaxProbesPerSecond", "20")
    .WithEnvironment("Discovery__ProbeDelayMilliseconds", "50")
    .WithEnvironment("Discovery__MaxCidrHosts", "512")
    .WithEnvironment("Discovery__MaxPortsPerTarget", "16")
    .WithEnvironment("Discovery__MaxBannerBytes", "512")
    .WithEnvironment("CredentialProtection__Key", scannerCredentialProtectionKey)
    .WaitFor(applicationDatabase)
    .WaitFor(rabbitMq)
    .WaitFor(demoPostgresTarget)
    .WaitFor(demoMySqlTarget)
    .WaitFor(demoMySqlSecureTarget)
    .WaitFor(demoMySqlRootEmptyTarget)
    .WaitFor(demoRedisUnsupportedTarget)
    .WaitForCompletion(demoMsSqlInit)
    .WaitFor(demoOracleTarget);

builder
    .AddExecutable("web", "npm", "../DbScanner.Web", "run", "dev", "--", "--hostname", "0.0.0.0")
    .WithHttpEndpoint(targetPort: 3000, port: 3000, name: "http", isProxied: false)
    .WithEnvironment("NEXT_PUBLIC_API_BASE_URL", api.GetEndpoint("http"))
    .WithEnvironment("NEXT_PUBLIC_KEYCLOAK_URL", "http://localhost:18080")
    .WithEnvironment("NEXT_PUBLIC_KEYCLOAK_REALM", "dbscanner")
    .WithEnvironment("NEXT_PUBLIC_KEYCLOAK_CLIENT_ID", "dbscanner-web")
    .WaitFor(keycloak)
    .WaitFor(demoPostgresTarget)
    .WaitFor(demoMySqlTarget)
    .WaitFor(demoMySqlSecureTarget)
    .WaitFor(demoMySqlRootEmptyTarget)
    .WaitForCompletion(demoMsSqlInit)
    .WaitFor(demoOracleTarget);

builder.Build().Run();
