using DbScanner.Application.Models;
using DbScanner.Contracts.NseScripts;

namespace DbScanner.Application.Ports;

public interface INseScriptCatalogService
{
    Task<IReadOnlyList<NseScriptDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<NseScriptDto> CreateAsync(CreateNseScriptCommand command, CancellationToken cancellationToken = default);
    Task<NseScriptDto> UpdateAsync(string id, UpdateNseScriptCommand command, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
