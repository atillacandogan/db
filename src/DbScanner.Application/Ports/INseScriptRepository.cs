using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;

namespace DbScanner.Application.Ports;

public interface INseScriptRepository
{
    Task<IReadOnlyList<NseScript>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NseScript>> ListInstalledForSyncAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NseScript>> ListApprovedByEngineAsync(DatabaseEngine engine, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NseScript>> ListRunnableByIdsAsync(DatabaseEngine engine, IReadOnlyCollection<string> scriptIds, CancellationToken cancellationToken = default);
    Task<NseScript?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task AddAsync(NseScript script, CancellationToken cancellationToken = default);
    void Delete(NseScript script);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
