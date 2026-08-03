import type { RuleSeverity } from "@/schemas/rule.schema";

export function getSeverityLabel(severity: RuleSeverity): string {
  const labels: Record<RuleSeverity, string> = {
    critical: "Critical",
    high: "High",
    medium: "Medium",
    low: "Low",
    info: "Info",
  };

  return labels[severity];
}
