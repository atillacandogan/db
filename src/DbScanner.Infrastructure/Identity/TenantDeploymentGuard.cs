using DbScanner.Infrastructure.Persistence;
using DbScanner.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DbScanner.Infrastructure.Identity;

public static class TenantDeploymentGuard
{
    public static async Task VerifyDbScannerTenantAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITenantDeploymentVerifier>().VerifyAsync(ct);
    }

    public static async Task VerifyDbScannerTenantBeforeMigrationAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITenantDeploymentVerifier>().VerifyBeforeMigrationAsync(ct);
    }
}

public interface ITenantDeploymentVerifier
{
    Task VerifyAsync(CancellationToken ct = default);
    Task VerifyBeforeMigrationAsync(CancellationToken ct = default);
}

public sealed class TenantDeploymentVerifier(IServiceProvider services) : ITenantDeploymentVerifier
{
    public async Task VerifyAsync(CancellationToken ct = default)
    {
        await VerifyBeforeMigrationAsync(ct);
        var tenant = services.GetRequiredService<IOptions<TenantIsolationOptions>>().Value;
        var db = services.GetRequiredService<DbScannerDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO deployment_tenant (\"Id\", \"TenantId\") VALUES (1, {tenant.TenantId}) ON CONFLICT (\"Id\") DO NOTHING", ct);
        var boundTenant = await db.Set<DbScanner.Domain.Entities.DeploymentTenant>().AsNoTracking().SingleAsync(ct);
        if (boundTenant.TenantId != tenant.TenantId)
            throw new InvalidOperationException("Database is bound to a different tenant. Use a separately provisioned database; do not rename the binding.");
        if (tenant.DedicatedTenant && await db.ScanScopeAllowlist.AnyAsync(s => s.Enabled
                && s.ScopeType == DbScanner.Domain.Enums.ScanScopeType.DemoTarget, ct))
            throw new InvalidOperationException("Dedicated tenants must disable demo scan scopes and provision only their own approved network scope before startup.");
    }

    public async Task VerifyBeforeMigrationAsync(CancellationToken ct = default)
    {
        var tenant = services.GetRequiredService<IOptions<TenantIsolationOptions>>().Value;
        var db = services.GetRequiredService<DbScannerDbContext>();
        if (tenant.DedicatedTenant)
        {
            var privileged = await db.Database.SqlQuery<bool>($"SELECT rolsuper OR rolbypassrls AS \"Value\" FROM pg_roles WHERE rolname = current_user").SingleAsync(ct);
            if (privileged) throw new InvalidOperationException("Dedicated tenants must use a non-superuser, non-BYPASSRLS database login with access only to their own database.");
            // Validate the broker vhost/login even before the first publication.
            _ = services.GetRequiredService<RabbitMqConnectionFactory>().Create();
        }
        var table = await db.Database.SqlQuery<string?>($"SELECT to_regclass('public.deployment_tenant')::text AS \"Value\"").SingleAsync(ct);
        if (table is not null)
        {
            var binding = await db.Set<DbScanner.Domain.Entities.DeploymentTenant>().AsNoTracking().SingleOrDefaultAsync(ct);
            if (binding is not null && binding.TenantId != tenant.TenantId)
                throw new InvalidOperationException("Database tenant mismatch; migration/startup refused.");
        }
    }
}
