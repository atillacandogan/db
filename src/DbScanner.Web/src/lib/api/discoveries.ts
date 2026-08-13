import { apiDelete, apiGet, apiPost } from "@/lib/api/client";
import { endpoints } from "@/lib/api/endpoints";
import type {
  DiscoveryJobDto,
  DiscoveryEnvironment,
  DiscoveryScopeValidationDto,
  DiscoveryResultDto,
  LocalNetworkDto,
  SupportedDiscoveryEngine,
} from "@/schemas/discovery.schema";

export type CreateDiscoveryRequest = {
  name: string;
  scope?: string;
  environment: string;
  supportedEngineFilter: SupportedDiscoveryEngine[];
  useAuthorizedScope: boolean;
};

export type CreateLocalNetworkDiscoveryRequest = {
  name: string;
  environment: DiscoveryEnvironment;
  supportedEngineFilter: SupportedDiscoveryEngine[];
  networks: string[];
};

export function getDiscoveries(): Promise<DiscoveryJobDto[]> {
  return apiGet<DiscoveryJobDto[]>(endpoints.discoveries);
}

export function getDiscovery(discoveryId: string): Promise<DiscoveryJobDto> {
  return apiGet<DiscoveryJobDto>(`${endpoints.discoveries}/${discoveryId}`);
}

export function deleteDiscovery(discoveryId: string): Promise<void> {
  return apiDelete(`${endpoints.discoveries}/${discoveryId}`);
}

export function createDiscovery(request: CreateDiscoveryRequest): Promise<DiscoveryJobDto> {
  return apiPost<DiscoveryJobDto, CreateDiscoveryRequest>(endpoints.discoveries, request);
}

export function getLocalDiscoveryNetworks(): Promise<LocalNetworkDto[]> {
  return apiGet<LocalNetworkDto[]>(`${endpoints.discoveries}/local-networks`);
}

export function createLocalNetworkDiscovery(request: CreateLocalNetworkDiscoveryRequest): Promise<DiscoveryJobDto> {
  return apiPost<DiscoveryJobDto, CreateLocalNetworkDiscoveryRequest>(
    `${endpoints.discoveries}/local-network`,
    request,
  );
}

export function validateDiscoveryScope(request: {
  scope: string;
  environment: DiscoveryEnvironment;
  supportedEngineFilter: SupportedDiscoveryEngine[];
}): Promise<DiscoveryScopeValidationDto> {
  return apiPost<DiscoveryScopeValidationDto, typeof request>(`${endpoints.discoveries}/validate-scope`, request);
}

export function addDiscoveryTargets(
  discoveryId: string,
  resultIds: string[],
  isBulk: boolean,
): Promise<AddDiscoveryTargetsResponse> {
  return apiPost<AddDiscoveryTargetsResponse, { resultIds: string[]; isBulk: boolean }>(
    `${endpoints.discoveries}/${discoveryId}/targets`,
    { resultIds, isBulk },
  );
}

export type DiscoveryTargetAddItem = {
  resultId: string;
  status: "added" | "alreadyExists" | "failed";
  code?: string;
  message?: string;
  targetId?: string;
};

export type AddDiscoveryTargetsResponse = {
  addedCount: number;
  alreadyExistingCount: number;
  failedCount: number;
  items: DiscoveryTargetAddItem[];
  results: DiscoveryResultDto[];
};
