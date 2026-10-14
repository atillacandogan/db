using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Contracts.Scans;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Messaging;
using DbScanner.Infrastructure.Persistence;
using DbScanner.Infrastructure.Repositories;
using DbScanner.Infrastructure.Scanning;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using RabbitMQ.Client;
using Xunit;

namespace DbScanner.Infrastructure.Tests;

[Collection(DatabaseContainerTestCollection.Name)]
public sealed class ScanJobReliabilityIntegrationTests(ScanReliabilityFixture fixture)
    : IClassFixture<ScanReliabilityFixture>
{
    private static readonly CancellationToken None = CancellationToken.None;

    [DockerFact]
    public async Task Scan_and_encrypted_outbox_commit_together_without_a_broker_connection()
    {
        await using var environment = await fixture.CreateEnvironmentAsync();
        var authentication = new DatabaseAuthentication("demo", "scanner", "must-not-be-stored-in-plaintext");
        var request = await environment.StageAsync(authentication);
        using var scope = environment.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<DbScannerDbContext>();
        Assert.Equal(ScanStatus.Pending, (await database.Scans.SingleAsync()).Status);
        var outbox = await database.ScanOutboxMessages.SingleAsync();
        Assert.Null(outbox.PublishedAt);
        Assert.Equal(0, outbox.DeliveryAttempts);
        Assert.DoesNotContain(authentication.Password, outbox.Payload);
        Assert.Equal(["banner"], request.SelectedCheckIds);
        Assert.Null(request.SelectedNseScriptIds);
        Assert.Equal(authentication, scope.ServiceProvider.GetRequiredService<IScanCredentialProtector>()
            .Unprotect(request.ProtectedCredentials!));
    }

    [DockerFact]
    public async Task Outbox_constraint_failure_rolls_back_the_scan_as_well()
    {
        await using var environment = await fixture.CreateEnvironmentAsync();
        using var scope = environment.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<DbScannerDbContext>();
        var target = await environment.AddTargetAsync(database);
        var scan = new Scan { TargetId = target.Id };
        var repository = scope.ServiceProvider.GetRequiredService<IScanRepository>();
        await repository.AddAsync(scan);
        await scope.ServiceProvider.GetRequiredService<IScanJobPublisher>()
            .PublishScanRequestedAsync(scan, target, ["banner"], None);
        database.ScanOutboxMessages.Local.Single().Payload = null!;
        await Assert.ThrowsAsync<DbUpdateException>(() => repository.SaveChangesAsync());
        database.ChangeTracker.Clear();
        Assert.False(await database.Scans.AnyAsync(entry => entry.Id == scan.Id));
        Assert.False(await database.ScanOutboxMessages.AnyAsync(entry => entry.ScanId == scan.Id));
    }

    [DockerFact]
    public async Task Broker_failure_preserves_request_and_a_later_dispatch_retries_it()
    {
        await using var environment = await fixture.CreateEnvironmentAsync();
        await environment.StageAsync();
        var transport = new SwitchableTransport { Fail = true };
        var dispatcher = environment.Dispatcher(transport);
        Assert.True(await dispatcher.DispatchOnceAsync(None));
        using (var scope = environment.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<DbScannerDbContext>();
            var row = await database.ScanOutboxMessages.SingleAsync();
            Assert.Null(row.PublishedAt);
            Assert.Null(row.DispatchToken);
            Assert.Equal("broker_publish_failed", row.LastErrorCode);
            Assert.Equal(ScanStatus.Pending, (await database.Scans.SingleAsync()).Status);
        }
        transport.Fail = false;
        Assert.True(await dispatcher.DispatchOnceAsync(None));
        Assert.False(await dispatcher.DispatchOnceAsync(None));
        using var finalScope = environment.Services.CreateScope();
        var final = await finalScope.ServiceProvider.GetRequiredService<DbScannerDbContext>()
            .ScanOutboxMessages.SingleAsync();
        Assert.NotNull(final.PublishedAt);
        Assert.Equal(2, final.DeliveryAttempts);
        Assert.Null(final.LastErrorCode);
    }

    [DockerFact]
    public async Task Initial_dispatch_and_broker_retry_use_database_time_when_host_clock_is_ahead()
    {
        // Offset only this environment's clock reads, not the machine/container clock.
        await using var environment = await fixture.CreateEnvironmentAsync(new BehindHostDatabaseClock());
        var request = await environment.StageAsync();
        var transport = new SwitchableTransport { Fail = true };
        var dispatcher = environment.Dispatcher(transport);

        Assert.True(await dispatcher.DispatchOnceAsync(None));
        using (var scope = environment.Services.CreateScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<DbScannerDbContext>()
                .ScanOutboxMessages.SingleAsync();
            Assert.Equal(request.RequestedAt, row.CreatedAt);
            Assert.Null(row.PublishedAt);
            Assert.Equal("broker_publish_failed", row.LastErrorCode);
        }
        transport.Fail = false;
        Assert.True(await dispatcher.DispatchOnceAsync(None));
        Assert.False(await dispatcher.DispatchOnceAsync(None));
        using var finalScope = environment.Services.CreateScope();
        var final = await finalScope.ServiceProvider.GetRequiredService<DbScannerDbContext>()
            .ScanOutboxMessages.SingleAsync();
        Assert.NotNull(final.PublishedAt);
        Assert.Equal(2, final.DeliveryAttempts);
    }

    [DockerFact]
    public async Task Scheduled_outbox_is_not_published_before_its_database_due_time()
    {
        await using var environment = await fixture.CreateEnvironmentAsync();
        var request = await environment.StageAsync();
        using (var scope = environment.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<DbScannerDbContext>().Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE scan_outbox_messages SET "AvailableAt" = clock_timestamp() + interval '1 hour'
                WHERE "ScanId" = {request.ScanId}
                """);

        Assert.False(await environment.Dispatcher(new SwitchableTransport()).DispatchOnceAsync(None));
        using var finalScope = environment.Services.CreateScope();
        var row = await finalScope.ServiceProvider.GetRequiredService<DbScannerDbContext>()
            .ScanOutboxMessages.SingleAsync();
        Assert.Null(row.PublishedAt);
        Assert.Null(row.DispatchToken);
        Assert.Equal(0, row.DeliveryAttempts);
    }

    [DockerFact]
    public async Task Only_one_concurrent_delivery_can_own_the_execution_lease()
    {
        await using var environment = await fixture.CreateEnvironmentAsync();
        var request = await environment.StageAsync();
        var leases = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var scope = environment.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IScanJobLeaseStore>().TryAcquireAsync(request, None);
        }));
        var lease = Assert.Single(leases, value => value is not null)!;
        Assert.Equal(1, lease.Attempt);
        Assert.True(await environment.WithStoreAsync(store => store.RenewAsync(lease, None)));
        Assert.Equal(0, await environment.WithStoreAsync(store => store.RecoverExpiredAsync(None)));
    }

    [DockerFact]
    public async Task Expired_lease_replays_original_selection_and_fences_stale_results()
    {
        await using var environment = await fixture.CreateEnvironmentAsync();
        var request = await environment.StageAsync(new DatabaseAuthentication("demo", "scanner", "test-password"));
        var oldLease = (await environment.WithStoreAsync(store => store.TryAcquireAsync(request, None)))!;
        await environment.ExpireAsync(request.ScanId);
        Assert.False(await environment.WithStoreAsync(store => store.RenewAsync(oldLease, None)));
        var counts = await Task.WhenAll(Enumerable.Range(0, 2)
            .Select(_ => environment.WithStoreAsync(store => store.RecoverExpiredAsync(None))));
        Assert.Equal(1, counts.Sum());
        var newLease = (await environment.WithStoreAsync(store => store.TryAcquireAsync(
            request with { SelectedCheckIds = ["must-not-replace-the-plan"] }, None)))!;
        Assert.NotEqual(oldLease.Token, newLease.Token);
        Assert.Equal(2, newLease.Attempt);
        Assert.Equal(request.SelectedCheckIds, newLease.Request.SelectedCheckIds);
        Assert.Equal(request.ProtectedCredentials, newLease.Request.ProtectedCredentials);
        Assert.False(await environment.WithStoreAsync(store => store.RetryAsync(oldLease, None)));
        await Assert.ThrowsAsync<ScanLeaseLostException>(() => environment.CompleteAsync(oldLease));
        await environment.CompleteAsync(newLease);
        Assert.Null(await environment.WithStoreAsync(store => store.TryAcquireAsync(request, None)));
        using var scope = environment.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<DbScannerDbContext>();
        Assert.Equal(ScanStatus.Scanned, (await database.Scans.SingleAsync()).Status);
        Assert.Single(await database.ScanScriptResults.ToListAsync());
        Assert.False(await environment.Dispatcher(new SwitchableTransport()).DispatchOnceAsync(None));
    }

    [DockerFact]
    public async Task Failed_evidence_commit_is_atomic_and_keeps_the_lease_for_recovery()
    {
        await using var environment = await fixture.CreateEnvironmentAsync();
        var request = await environment.StageAsync();
        var lease = (await environment.WithStoreAsync(store => store.TryAcquireAsync(request, None)))!;
        await Assert.ThrowsAsync<DbUpdateException>(() => environment.CompleteAsync(lease, "missing-check-foreign-key"));
        using (var scope = environment.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<DbScannerDbContext>();
            var scan = await database.Scans.SingleAsync();
            Assert.Equal(ScanStatus.Scanning, scan.Status);
            Assert.Equal(lease.Token, scan.LeaseToken);
            Assert.Empty(await database.ScanScriptResults.ToListAsync());
        }
        await environment.CompleteAsync(lease);
    }

    [DockerFact]
    public async Task User_cancellation_revokes_heartbeat_and_prevents_completion_or_recovery()
    {
        await using var environment = await fixture.CreateEnvironmentAsync();
        var request = await environment.StageAsync();
        var lease = (await environment.WithStoreAsync(store => store.TryAcquireAsync(request, None)))!;
        using (var scope = environment.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IScanRepository>().UpdateStatusAsync(request.ScanId, ScanStatus.Cancelled);
        Assert.False(await environment.WithStoreAsync(store => store.RenewAsync(lease, None)));
        await Assert.ThrowsAsync<ScanLeaseLostException>(() => environment.CompleteAsync(lease));
        Assert.Equal(0, await environment.WithStoreAsync(store => store.RecoverExpiredAsync(None)));
        Assert.False(await environment.Dispatcher(new SwitchableTransport()).DispatchOnceAsync(None));
    }

    [DockerFact]
    public async Task Heartbeat_renews_a_live_worker_and_stops_it_when_ownership_is_revoked()
    {
        await using var environment = await fixture.CreateEnvironmentAsync();
        var request = await environment.StageAsync();
        var lease = (await environment.WithStoreAsync(store => store.TryAcquireAsync(request, None)))!;
        using var execution = new CancellationTokenSource();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var heartbeat = environment.Services.GetRequiredService<ScanJobHeartbeat>();
        var loop = heartbeat.RunAsync(lease, execution, stop.Token);
        await Task.Delay(1200);
        using (var scope = environment.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<DbScannerDbContext>();
            var scan = await database.Scans.SingleAsync();
            Assert.True(scan.LastHeartbeatAt > scan.StartedAt);
            await scope.ServiceProvider.GetRequiredService<IScanRepository>().UpdateStatusAsync(request.ScanId, ScanStatus.Cancelled);
        }
        await loop.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.True(execution.IsCancellationRequested);
    }

    [DockerFact]
    public async Task Repeated_worker_failures_are_bounded_and_publish_a_durable_dead_letter()
    {
        await using var environment = await fixture.CreateEnvironmentAsync();
        var request = await environment.StageAsync();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var lease = (await environment.WithStoreAsync(store => store.TryAcquireAsync(request, None)))!;
            Assert.Equal(attempt, lease.Attempt);
            await environment.ExpireAsync(request.ScanId);
            Assert.Equal(1, await environment.WithStoreAsync(store => store.RecoverExpiredAsync(None)));
        }
        using (var scope = environment.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<DbScannerDbContext>();
            var scan = await database.Scans.SingleAsync();
            Assert.Equal(ScanStatus.Failed, scan.Status);
            Assert.Equal("execution_attempts_exhausted", scan.ErrorMessage);
            Assert.True((await database.ScanOutboxMessages.SingleAsync()).DeadLetter);
        }
        Assert.True(await environment.Dispatcher().DispatchOnceAsync(None));
        await using var connection = await environment.Services.GetRequiredService<RabbitMqConnectionFactory>()
            .Create().CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var received = await channel.BasicGetAsync(environment.QueueOptions.DeadLetterQueue, true);
        Assert.NotNull(received);
        Assert.Equal(request.ScanId.ToString(), received.BasicProperties.MessageId);
    }

    [DockerFact]
    public async Task Publish_before_dispatcher_crash_and_duplicate_delivery_execute_only_once()
    {
        await using var environment = await fixture.CreateEnvironmentAsync();
        var request = await environment.StageAsync();
        using (var scope = environment.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ScanJobReliabilityStore>();
            var entry = (await store.ClaimOutboxAsync(None))!;
            await environment.Services.GetRequiredService<IScanOutboxTransport>().PublishAsync(entry, None);
            // Simulate a process dying after broker confirmation, before MarkPublished.
            await scope.ServiceProvider.GetRequiredService<DbScannerDbContext>().Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE scan_outbox_messages SET "DispatchExpiresAt" = clock_timestamp() - interval '1 second'
                WHERE "ScanId" = {request.ScanId}
                """);
        }
        Assert.True(await environment.Dispatcher().DispatchOnceAsync(None));
        using var stop = new CancellationTokenSource();
        var consuming = environment.Services.GetRequiredService<IScanJobConsumer>().StartAsync(stop.Token);
        try
        {
            await environment.WaitForCompletedAsync(request.ScanId);
            await environment.WaitForEmptyQueueAsync();
        }
        finally
        {
            await stop.CancelAsync();
            await consuming;
        }
        Assert.Equal(1, environment.Recorder.Started);
        Assert.Equal(1, environment.Recorder.Completed);
    }

    [DockerFact]
    public async Task Worker_shutdown_before_ack_is_recovered_without_losing_the_plan()
    {
        await using var environment = await fixture.CreateEnvironmentAsync();
        var request = await environment.StageAsync();
        environment.Recorder.Block = true;
        Assert.True(await environment.Dispatcher().DispatchOnceAsync(None));
        using var stop = new CancellationTokenSource();
        var consuming = environment.Services.GetRequiredService<IScanJobConsumer>().StartAsync(stop.Token);
        try
        {
            await environment.Recorder.StartSignal.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await stop.CancelAsync();
            await consuming;
        }
        Assert.Equal(0, environment.Recorder.Completed);
        await environment.ExpireAsync(request.ScanId);
        Assert.Equal(1, await environment.WithStoreAsync(store => store.RecoverExpiredAsync(None)));
        Assert.True(await environment.Dispatcher().DispatchOnceAsync(None));
        environment.Recorder.Block = false;
        using var secondStop = new CancellationTokenSource();
        var resumed = environment.Services.GetRequiredService<IScanJobConsumer>().StartAsync(secondStop.Token);
        try
        {
            await environment.WaitForCompletedAsync(request.ScanId);
            await environment.WaitForEmptyQueueAsync();
        }
        finally
        {
            await secondStop.CancelAsync();
            await resumed;
        }
        Assert.Equal(2, environment.Recorder.Started);
        Assert.Equal(1, environment.Recorder.Completed);
        Assert.Equal(["banner"], environment.Recorder.LastSelection);
    }

    [DockerFact]
    public async Task Pre_outbox_queued_request_is_captured_before_execution_and_can_be_recovered()
    {
        await using var environment = await fixture.CreateEnvironmentAsync();
        ScanRequestedMessage request;
        using (var scope = environment.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<DbScannerDbContext>();
            var target = await environment.AddTargetAsync(database);
            var scan = new Scan { TargetId = target.Id, Status = ScanStatus.Scanning };
            database.Scans.Add(scan);
            await database.SaveChangesAsync();
            request = new ScanRequestedMessage(scan.Id, target.Id, "postgresql", target.Host,
                target.Port, DateTimeOffset.UtcNow, SelectedNseScriptIds: ["banner"]);
        }
        var lease = (await environment.WithStoreAsync(store => store.TryAcquireAsync(request, None)))!;
        Assert.Equal(["banner"], ScanRequestedMessageCompatibility.ResolveSelection(lease.Request));
        await environment.ExpireAsync(request.ScanId);
        Assert.Equal(1, await environment.WithStoreAsync(store => store.RecoverExpiredAsync(None)));
        var recovered = (await environment.WithStoreAsync(store => store.TryAcquireAsync(request, None)))!;
        Assert.Equal(["banner"], ScanRequestedMessageCompatibility.ResolveSelection(recovered.Request));
    }

    private sealed class SwitchableTransport : IScanOutboxTransport
    {
        public bool Fail { get; set; }
        public Task PublishAsync(ClaimedScanOutbox entry, CancellationToken cancellationToken) =>
            Fail ? Task.FromException(new IOException("simulated broker outage")) : Task.CompletedTask;
    }

    private sealed class BehindHostDatabaseClock : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            command.CommandText = command.CommandText.Replace(
                "SELECT clock_timestamp() AS \"Value\"",
                "SELECT clock_timestamp() - interval '5 minutes' AS \"Value\"", StringComparison.Ordinal);
            return ValueTask.FromResult(result);
        }
    }
}

public sealed class ScanReliabilityFixture : IAsyncLifetime
{
    private readonly string password = Guid.NewGuid().ToString("N");
    private IContainer? postgres;
    private IContainer? rabbit;

    public async Task InitializeAsync()
    {
        if (!DockerFactAttribute.CanRun("docker", "version --format {{.Server.Version}}")) return;
        postgres = new ContainerBuilder("postgres:17.6")
            .WithEnvironment("POSTGRES_PASSWORD", password).WithPortBinding(5432, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5432)).Build();
        rabbit = new ContainerBuilder("rabbitmq:4.2-management")
            .WithEnvironment("RABBITMQ_DEFAULT_USER", "scanner")
            .WithEnvironment("RABBITMQ_DEFAULT_PASS", password).WithPortBinding(5672, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5672)).Build();
        await Task.WhenAll(postgres.StartAsync(), rabbit.StartAsync());
    }

    internal async Task<ScanReliabilityEnvironment> CreateEnvironmentAsync(DbCommandInterceptor? clockInterceptor = null)
    {
        var name = "reliability_" + Guid.NewGuid().ToString("N");
        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = postgres!.Hostname, Port = postgres.GetMappedPublicPort(5432),
            Username = "postgres", Password = password, Database = "postgres"
        };
        await using (var connection = new NpgsqlConnection(connectionString.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\"";
            await command.ExecuteNonQueryAsync();
        }
        connectionString.Database = name;
        return await ScanReliabilityEnvironment.CreateAsync(connectionString.ConnectionString,
            $"amqp://scanner:{password}@{rabbit!.Hostname}:{rabbit.GetMappedPublicPort(5672)}", name,
            clockInterceptor: clockInterceptor);
    }

    public async Task DisposeAsync()
    {
        if (rabbit is not null) await rabbit.DisposeAsync();
        if (postgres is not null) await postgres.DisposeAsync();
    }

    internal async Task<ScanReliabilityEnvironment> CreateDedicatedEnvironmentAsync(string tenantId)
    {
        var login = "tenant_" + Guid.NewGuid().ToString("N");
        var loginPassword = Guid.NewGuid().ToString("N");
        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = postgres!.Hostname, Port = postgres.GetMappedPublicPort(5432),
            Username = "postgres", Password = password, Database = "postgres"
        };
        await using (var connection = new NpgsqlConnection(connectionString.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            // Identifiers/passwords are generated hex-only test values, not external input.
            command.CommandText = $"CREATE ROLE {login} LOGIN PASSWORD '{loginPassword}' NOSUPERUSER NOBYPASSRLS";
            await command.ExecuteNonQueryAsync();
            command.CommandText = $"CREATE DATABASE {login} OWNER {login}";
            await command.ExecuteNonQueryAsync();
        }
        var vhost = "tenant." + tenantId;
        Assert.Equal(0, (await rabbit!.ExecAsync(["rabbitmqctl", "add_vhost", vhost])).ExitCode);
        Assert.Equal(0, (await rabbit.ExecAsync(["rabbitmqctl", "add_user", login, loginPassword])).ExitCode);
        Assert.Equal(0, (await rabbit.ExecAsync(["rabbitmqctl", "set_permissions", "-p", vhost, login, ".*", ".*", ".*"])).ExitCode);
        connectionString.Database = login; connectionString.Username = login; connectionString.Password = loginPassword;
        return await ScanReliabilityEnvironment.CreateAsync(connectionString.ConnectionString,
            $"amqp://{login}:{loginPassword}@{rabbit.Hostname}:{rabbit.GetMappedPublicPort(5672)}/{vhost}", login,
            new DbScanner.Infrastructure.Identity.TenantIsolationOptions { TenantId = tenantId, DedicatedTenant = true });
    }
}

internal sealed class ScanReliabilityEnvironment(ServiceProvider services, RabbitMqOptions queueOptions)
    : IAsyncDisposable
{
    internal ServiceProvider Services => services;
    internal RabbitMqOptions QueueOptions => queueOptions;
    internal ExecutionRecorder Recorder => services.GetRequiredService<ExecutionRecorder>();

    internal static async Task<ScanReliabilityEnvironment> CreateAsync(string database, string broker, string suffix,
        DbScanner.Infrastructure.Identity.TenantIsolationOptions? tenancy = null,
        DbCommandInterceptor? clockInterceptor = null)
    {
        var queues = new RabbitMqOptions
        {
            RequestedQueue = suffix + ".requested", RetryQueue = suffix + ".retry",
            DeadLetterQueue = suffix + ".deadletter"
        };
        tenancy ??= new();
        tenancy.NamespaceQueues(queues);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:rabbitmq"] = broker,
            ["TenantIsolation:TenantId"] = tenancy.TenantId,
            ["TenantIsolation:DedicatedTenant"] = tenancy.DedicatedTenant.ToString()
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => ResourceAccessContext.ForSystem());
        services.AddSingleton(Options.Create(tenancy));
        services.AddDbContext<DbScannerDbContext>(options =>
        {
            options.UseNpgsql(database);
            if (clockInterceptor is not null) options.AddInterceptors(clockInterceptor);
        });
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(Options.Create(queues));
        services.AddSingleton(Options.Create(new ScanReliabilityOptions { RetryDelaySeconds = 0, HeartbeatSeconds = 1 }));
        services.AddSingleton(Options.Create(new CredentialProtectionOptions
        {
            Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }));
        services.AddSingleton<IScanCredentialProtector, AesGcmScanCredentialProtector>();
        services.AddScoped<ScanExecutionLeaseContext>();
        services.AddScoped<IScanRepository, ScanRepository>();
        services.AddScoped<IScanJobPublisher, OutboxScanJobPublisher>();
        services.AddScoped<ScanJobReliabilityStore>();
        services.AddScoped<IScanJobLeaseStore>(provider => provider.GetRequiredService<ScanJobReliabilityStore>());
        services.AddSingleton<ExecutionRecorder>();
        services.AddScoped<IScanProcessingService, CompletingProcessor>();
        services.AddSingleton<ScanJobHeartbeat>();
        services.AddSingleton<RabbitMqConnectionFactory>();
        services.AddSingleton<IScanOutboxTransport, RabbitMqScanOutboxTransport>();
        services.AddSingleton<IScanJobConsumer, RabbitMqScanJobConsumer>();
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DbScannerDbContext>().Database.MigrateAsync();
        if (tenancy.DedicatedTenant)
            await scope.ServiceProvider.GetRequiredService<DbScannerDbContext>().ScanScopeAllowlist
                .Where(s => s.ScopeType == ScanScopeType.DemoTarget).ExecuteUpdateAsync(s => s.SetProperty(a => a.Enabled, false));
        return new ScanReliabilityEnvironment(provider, queues);
    }

    internal async Task<Target> AddTargetAsync(DbScannerDbContext database)
    {
        var target = new Target { Name = "Reliability test", Host = Guid.NewGuid() + ".internal",
            NormalizedHost = Guid.NewGuid().ToString(), Port = 5432, Engine = DatabaseEngine.PostgreSql };
        database.Targets.Add(target);
        await database.SaveChangesAsync();
        return target;
    }

    internal async Task<ScanRequestedMessage> StageAsync(DatabaseAuthentication? authentication = null)
    {
        using var scope = services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<DbScannerDbContext>();
        var target = await AddTargetAsync(database);
        var scan = new Scan { TargetId = target.Id };
        var repository = scope.ServiceProvider.GetRequiredService<IScanRepository>();
        await repository.AddAsync(scan);
        await scope.ServiceProvider.GetRequiredService<IScanJobPublisher>()
            .PublishScanRequestedAsync(scan, target, ["banner"], authentication, CancellationToken.None);
        await repository.SaveChangesAsync();
        var payload = (await database.ScanOutboxMessages.SingleAsync()).Payload;
        return JsonSerializer.Deserialize<ScanRequestedMessage>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    internal async Task<T> WithStoreAsync<T>(Func<IScanJobLeaseStore, Task<T>> action)
    {
        using var scope = services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IScanJobLeaseStore>());
    }

    internal async Task ExpireAsync(Guid scanId)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DbScannerDbContext>().Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE scans SET "LeaseExpiresAt" = clock_timestamp() - interval '1 second' WHERE "Id" = {scanId}
            """);
    }

    internal async Task CompleteAsync(ScanJobLease lease, string checkId = "banner")
    {
        using var scope = services.CreateScope();
        var fence = scope.ServiceProvider.GetRequiredService<ScanExecutionLeaseContext>();
        fence.Token = lease.Token;
        fence.ScanId = lease.ScanId;
        await scope.ServiceProvider.GetRequiredService<IScanRepository>().StoreExecutionArtifactsAsync(
            lease.ScanId, [new ScanScriptResult { ScanId = lease.ScanId, NseScriptId = checkId,
                ScanCheckId = checkId, ScriptName = "banner", NormalizedOutput = "test-evidence" }],
            [], 0, Severity.Info, ScanStatus.Scanned);
    }

    internal ScanOutboxDispatcher Dispatcher(IScanOutboxTransport? transport = null) => new(
        services.GetRequiredService<IServiceScopeFactory>(), transport ?? services.GetRequiredService<IScanOutboxTransport>(),
        services.GetRequiredService<IOptions<ScanReliabilityOptions>>(), NullLogger<ScanOutboxDispatcher>.Instance);

    internal async Task WaitForCompletedAsync(Guid scanId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            using var scope = services.CreateScope();
            if (await scope.ServiceProvider.GetRequiredService<DbScannerDbContext>().Scans
                .AnyAsync(scan => scan.Id == scanId && scan.Status == ScanStatus.Scanned, timeout.Token)) return;
            await Task.Delay(50, timeout.Token);
        }
    }

    internal async Task WaitForEmptyQueueAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var connection = await services.GetRequiredService<RabbitMqConnectionFactory>().Create().CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        while (true)
        {
            if ((await channel.QueueDeclarePassiveAsync(queueOptions.RequestedQueue, timeout.Token)).MessageCount == 0) return;
            await Task.Delay(50, timeout.Token);
        }
    }

    public ValueTask DisposeAsync() => services.DisposeAsync();

    internal sealed class ExecutionRecorder
    {
        public int Started;
        public int Completed;
        public volatile bool Block;
        public IReadOnlyCollection<string>? LastSelection;
        public TaskCompletionSource StartSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class CompletingProcessor(IScanRepository repository, ExecutionRecorder recorder) : IScanProcessingService
    {
        public Task ProcessAsync(Guid scanId, CancellationToken cancellationToken = default) =>
            ProcessAsync(scanId, null, null, cancellationToken);
        public Task ProcessAsync(Guid scanId, IReadOnlyCollection<string>? ids, CancellationToken cancellationToken) =>
            ProcessAsync(scanId, ids, null, cancellationToken);
        public async Task ProcessAsync(Guid scanId, IReadOnlyCollection<string>? ids,
            DatabaseAuthentication? authentication, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref recorder.Started);
            recorder.LastSelection = ids;
            recorder.StartSignal.TrySetResult();
            if (recorder.Block) await Task.Delay(Timeout.Infinite, cancellationToken);
            await repository.StoreExecutionArtifactsAsync(scanId, [], [], 0, Severity.Info,
                ScanStatus.Scanned, cancellationToken: cancellationToken);
            Interlocked.Increment(ref recorder.Completed);
        }
    }
}
