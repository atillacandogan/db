namespace DbScanner.Domain.Entities;

// A database belongs to exactly one deployment tenant; this is NOT a row-tenant discriminator.
public sealed class DeploymentTenant
{
    public int Id { get; set; } = 1;
    public required string TenantId { get; set; }
}
