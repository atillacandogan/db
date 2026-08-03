using DbScanner.Application.Models;
using DbScanner.Contracts.Targets;

namespace DbScanner.Application.Ports;

public interface ITargetService
{
    Task<IReadOnlyList<TargetDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<TargetDto> CreateAsync(CreateTargetCommand command, CancellationToken cancellationToken = default);
    Task<TargetDto> UpdateAsync(
        Guid targetId,
        UpdateTargetCommand command,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid targetId, CancellationToken cancellationToken = default);
}
