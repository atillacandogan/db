export type SupportedDiscoveryEngine = "mysql" | "postgresql" | "mssql" | "oracle";
export type DiscoveryJobStatus = "pending" | "running" | "completed" | "failed";
export type DiscoveryResultStatus = "supported" | "unsupported" | "addedAsTarget";

export type DiscoveryResultDto = {
  id: string;
  host: string;
  port: number;
  detectedService: string;
  detectedEngine?: SupportedDiscoveryEngine;
  confidence: number;
  environment: string;
  status: DiscoveryResultStatus;
  reason: string;
  canAddAsTarget: boolean;
  targetId?: string;
};

export type DiscoveryJobDto = {
  id: string;
  name: string;
  scope: string;
  environment: string;
  supportedEngineFilter: SupportedDiscoveryEngine[];
  status: DiscoveryJobStatus;
  errorMessage?: string;
  createdAt: string;
  startedAt?: string;
  finishedAt?: string;
  supportedDatabases: DiscoveryResultDto[];
  unsupportedDatabases: DiscoveryResultDto[];
};
