using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DbScanner.Infrastructure.Repositories;

public sealed class NseScriptRepository(DbScannerDbContext dbContext) : INseScriptRepository
{
    public async Task<IReadOnlyList<NseScript>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.NseScripts
            .AsNoTracking()
            .Include(script => script.Matchers)
            .OrderBy(script => script.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<NseScript>> ListInstalledForSyncAsync(CancellationToken cancellationToken = default) =>
        await dbContext.NseScripts
            .Include(script => script.Matchers)
            .Where(script => script.Source == NseScriptSource.Installed)
            .OrderBy(script => script.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<NseScript>> ListApprovedByEngineAsync(DatabaseEngine engine, CancellationToken cancellationToken = default)
    {
        var engineToken = engine.ToApiString();

        return await dbContext.NseScripts
            .AsNoTracking()
            .Where(script => script.ApprovalStatus == ScriptApprovalStatus.Approved)
            .Where(script => script.Source == NseScriptSource.Manual || script.IsInstalled == true)
            .Where(script => script.SupportedEngines.Contains("generic") || script.SupportedEngines.Contains(engineToken))
            .OrderBy(script => script.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NseScript>> ListRunnableByIdsAsync(
        DatabaseEngine _,
        IReadOnlyCollection<string> scriptIds,
        CancellationToken cancellationToken = default)
    {
        if (scriptIds.Count == 0)
        {
            return Array.Empty<NseScript>();
        }

        var normalizedScriptIds = scriptIds
            .Where(scriptId => !string.IsNullOrWhiteSpace(scriptId))
            .Select(scriptId => scriptId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedScriptIds.Length == 0)
        {
            return Array.Empty<NseScript>();
        }

        var scripts = await dbContext.NseScripts
            .AsNoTracking()
            .Where(script => script.ApprovalStatus != ScriptApprovalStatus.Disabled)
            .Where(script => script.Source == NseScriptSource.Manual || script.IsInstalled == true)
            .Where(script => normalizedScriptIds.Contains(script.Id) || normalizedScriptIds.Contains(script.NseScriptName))
            .ToListAsync(cancellationToken);

        return normalizedScriptIds
            .Select(scriptId => scripts.FirstOrDefault(script =>
                string.Equals(script.Id, scriptId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(script.NseScriptName, scriptId, StringComparison.OrdinalIgnoreCase)))
            .Where(script => script is not null)
            .Select(script => script!)
            .GroupBy(script => script.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    public Task<NseScript?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
        dbContext.NseScripts
            .Include(script => script.Matchers)
            .FirstOrDefaultAsync(script => script.Id == id, cancellationToken);

    public async Task AddAsync(NseScript script, CancellationToken cancellationToken = default) =>
        await dbContext.NseScripts.AddAsync(script, cancellationToken);

    public void Delete(NseScript script) =>
        dbContext.NseScripts.Remove(script);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);

}
