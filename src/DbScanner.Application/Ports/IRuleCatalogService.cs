using DbScanner.Application.Models;
using DbScanner.Contracts.Rules;

namespace DbScanner.Application.Ports;

public interface IRuleCatalogService
{
    Task<IReadOnlyList<RuleDto>> ListRulesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RuleSupportMatrixDto>> ListSupportMatrixAsync(CancellationToken cancellationToken = default);
    Task<RuleDto> CreateRuleAsync(CreateRuleCommand command, CancellationToken cancellationToken = default);
    Task<RuleDto> UpdateRuleAsync(string id, UpdateRuleCommand command, CancellationToken cancellationToken = default);
}
