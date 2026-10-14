using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Infrastructure.Persistence;

namespace DbScanner.Infrastructure.Repositories;

public sealed class UserManagementAuditRepository(DbScannerDbContext dbContext)
    : IUserManagementAuditRepository
{
    public async Task AddAsync(
        UserManagementAudit audit,
        CancellationToken cancellationToken = default) =>
        await dbContext.UserManagementAudits.AddAsync(audit, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        await dbContext.SaveChangesAsync(cancellationToken);
}
