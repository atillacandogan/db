import type { RuleSeverity } from "./rule.schema";
import type { ScanStatus } from "./scan.schema";

export type FindingDto = {
  id: string;
  scanId: string;
  targetId: string;
  ruleId: string;
  title: string;
  severity: RuleSeverity;
  evidence: string;
  remediation: string;
  sourceScript: string;
  detectedAt: string;
};

export type FindingListItemDto = FindingDto & {
  targetName: string;
  targetHost: string;
  targetEngine: string;
  scoreContribution: number;
  scanStatus: ScanStatus;
  riskScore?: number;
  overallSeverity?: RuleSeverity;
  description?: string;
  status?: "open" | "resolved";
};
