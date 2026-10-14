using System.Security.Claims;
using System.Text.RegularExpressions;
using DbScanner.Infrastructure.Messaging;

namespace DbScanner.Infrastructure.Identity;

public sealed class TenantIsolationOptions
{
    public const string SectionName = "TenantIsolation";
    public string TenantId { get; set; } = "local";
    public bool DedicatedTenant { get; set; }
    public string ClaimName { get; set; } = "dbscanner_tenant";

    public bool IsValid() => Regex.IsMatch(TenantId, "^[a-z0-9][a-z0-9-]{0,62}$")
        && !string.IsNullOrWhiteSpace(ClaimName)
        && (!DedicatedTenant || TenantId != "local");

    public bool Allows(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true) return false;
        var claims = principal.FindAll(ClaimName).Select(c => c.Value).ToArray();
        // A claim, when present, must match even in local single-organization mode.
        return claims.Length == 0 ? !DedicatedTenant : claims.Length == 1 && claims[0] == TenantId;
    }

    public bool AcceptsMessage(string? tenantId) => tenantId == TenantId
        || (!DedicatedTenant && tenantId is null); // only local pre-upgrade messages

    public void NamespaceQueues(RabbitMqOptions queues)
    {
        if (!DedicatedTenant) return;
        var prefix = $"tenant.{TenantId}.";
        queues.RequestedQueue = prefix + queues.RequestedQueue;
        queues.RetryQueue = prefix + queues.RetryQueue;
        queues.DeadLetterQueue = prefix + queues.DeadLetterQueue;
        queues.DiscoveryRequestedQueue = prefix + queues.DiscoveryRequestedQueue;
        queues.DiscoveryDeadLetterQueue = prefix + queues.DiscoveryDeadLetterQueue;
        queues.NseScriptSyncQueue = prefix + queues.NseScriptSyncQueue;
    }
}
