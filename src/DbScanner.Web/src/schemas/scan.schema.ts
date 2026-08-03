import type { RuleSeverity } from "./rule.schema";

export type ScanStatus = "pending" | "scanning" | "scanned" | "failed" | "timeout" | "cancelled";

export type ScanScriptResultDto = {
  id: string;
  nseScriptId?: string;
  scriptName: string;
  rawXml?: string;
  normalizedOutput?: string;
  exitCode?: number;
  resultStatus?:
    | "passed"
    | "failed"
    | "skipped"
    | "not_supported"
    | "insufficient_privileges"
    | "connection_failed"
    | "authentication_failed";
  createdAt: string;
};

export type ScanTargetDto = {
  id: string;
  name: string;
  host: string;
  port: number;
  engine: string;
};

export type ScanFindingDto = {
  id: string;
  scanId: string;
  targetId: string;
  ruleId: string;
  title: string;
  severity: RuleSeverity;
  scoreContribution: number;
  evidence: string;
  remediation: string;
  sourceScript: string;
  detectedAt: string;
};

export type ScanDto = {
  id: string;
  targetId: string;
  status: ScanStatus;
  riskScore?: number;
  overallSeverity?: RuleSeverity;
  errorMessage?: string;
  createdAt: string;
  startedAt?: string;
  finishedAt?: string;
  target?: ScanTargetDto;
  scriptResults: ScanScriptResultDto[];
  findings: ScanFindingDto[];
};

export type ScanListItemDto = {
  id: string;
  targetId: string;
  status: ScanStatus;
  riskScore?: number;
  overallSeverity?: RuleSeverity;
  errorMessage?: string;
  createdAt: string;
  startedAt?: string;
  finishedAt?: string;
  target?: ScanTargetDto;
};
