import { apiGet, apiPost } from "@/lib/api/client";
import { endpoints } from "@/lib/api/endpoints";
import type {
  DiscoveryJobDto,
  DiscoveryResultDto,
  SupportedDiscoveryEngine,
} from "@/schemas/discovery.schema";

export type CreateDiscoveryRequest = {
  name: string;
  scope: string;
  environment: string;
  supportedEngineFilter: SupportedDiscoveryEngine[];
};

export function getDiscoveries(): Promise<DiscoveryJobDto[]> {
  return apiGet<DiscoveryJobDto[]>(endpoints.discoveries);
}

export function getDiscovery(discoveryId: string): Promise<DiscoveryJobDto> {
  return apiGet<DiscoveryJobDto>(`${endpoints.discoveries}/${discoveryId}`);
}

export function createDiscovery(request: CreateDiscoveryRequest): Promise<DiscoveryJobDto> {
  return apiPost<DiscoveryJobDto, CreateDiscoveryRequest>(endpoints.discoveries, request);
}

export function addDiscoveryTargets(
  discoveryId: string,
  resultIds: string[],
): Promise<DiscoveryResultDto[]> {
  return apiPost<DiscoveryResultDto[], { resultIds: string[] }>(
    `${endpoints.discoveries}/${discoveryId}/targets`,
    { resultIds },
  );
}
