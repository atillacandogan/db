export type SupportedDiscoveryEngine = "mysql" | "postgresql" | "mssql" | "oracle";
export type DiscoveryJobStatus = "pending" | "running" | "completed" | "failed";
export type DiscoveryResultStatus = "supported" | "alreadyDiscovered" | "unsupported" | "addedAsTarget";
export type DiscoveryFingerprintLevel = "Unknown" | "PortHint" | "PortOnly" | "Probable" | "Confirmed";
export type DiscoveryEnvironment = "demo" | "test" | "staging" | "prod";
export type DiscoveryTargetValidationStatus =
  | "Authorized"
  | "InvalidFormat"
  | "NotInAllowlist"
  | "InactiveAllowlist"
  | "EnvironmentMismatch"
  | "UnsupportedAddressType"
  | "CidrTooLarge"
  | "DnsResolutionFailed"
  | "ResolvedAddressNotAuthorized";

export type DiscoveryResultDto = {
  id: string;
  host: string;
  port: number;
  detectedService: string;
  detectedEngine?: SupportedDiscoveryEngine;
  confidence: number;
  fingerprintLevel: DiscoveryFingerprintLevel;
  environment: string;
  status: DiscoveryResultStatus;
  reason: string;
  canAddAsTarget: boolean;
  targetId?: string;
  isAlreadyTarget: boolean;
  existingTargetId?: string;
  targetState: "Available" | "AlreadyDiscovered" | "AlreadyAdded" | "Unsupported";
};

export type DiscoveryTargetValidationDto = {
  target: string;
  status: DiscoveryTargetValidationStatus;
  message: string;
  resolvedAddresses: string[];
};

export type DiscoveryScopeValidationDto = {
  environment: DiscoveryEnvironment;
  isValid: boolean;
  targets: DiscoveryTargetValidationDto[];
};

export type LocalNetworkDto = {
  interfaceName: string;
  address: string;
  cidr: string;
  hostCount: number;
  canScan: boolean;
  reason?: string;
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
