import type { RuleSeverity } from "./rule.schema";

export type DatabaseEngine = "postgresql" | "mysql" | "mssql" | "oracle";
export type TargetConnectionMode = "defaultValues" | "manualConnectionString";

export type TargetScanSummaryDto = {
  scanId: string;
  status: "pending" | "scanning" | "scanned" | "failed" | "timeout" | "cancelled";
  riskScore?: number;
  overallSeverity?: RuleSeverity;
  createdAt: string;
  finishedAt?: string;
};

export type TargetDto = {
  id: string;
  name: string;
  host: string;
  port: number;
  engine: DatabaseEngine;
  connectionMode: TargetConnectionMode;
  environmentTag?: string;
  hasCredentials: boolean;
  enabled: boolean;
  lastScan?: TargetScanSummaryDto;
};
