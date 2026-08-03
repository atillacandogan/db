namespace DbScanner.Contracts.Rules;

public sealed record RuleSupportMatrixDto(
    string RuleId,
    string Engine,
    string UnsupportedStatus);
