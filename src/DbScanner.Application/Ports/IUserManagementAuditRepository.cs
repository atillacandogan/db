using DbScanner.Domain.Entities;

namespace DbScanner.Application.Ports;

public interface IUserManagementAuditRepository
{
    Task AddAsync(UserManagementAudit audit, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
