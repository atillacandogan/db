export type RiskLevel = "info" | "low" | "medium" | "high" | "critical";

export function getRiskLevel(score: number): RiskLevel {
  if (score >= 76) {
    return "critical";
  }

  if (score >= 51) {
    return "high";
  }

  if (score >= 26) {
    return "medium";
  }

  if (score >= 1) {
    return "low";
  }

  return "info";
}

export function resolveRiskLevel(
  score: number | undefined,
  overallSeverity: RiskLevel | undefined,
): RiskLevel | null {
  if (overallSeverity) {
    return overallSeverity;
  }

  return typeof score === "number" ? getRiskLevel(score) : null;
}
