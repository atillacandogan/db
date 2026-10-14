import { apiDelete, apiGet, apiPost, apiPut } from "@/lib/api/client";
import { endpoints } from "@/lib/api/endpoints";
import type { NseScriptDto } from "@/schemas/nse-script.schema";
import type { ScanCheckDto, ScanExecutorCapabilityDto } from "@/schemas/scan-check.schema";

export type UpsertScanCheckRequest = {
  id?: string;
  name: string;
  displayName: string;
  description: string;
  executorType: string;
  supportedEngines: ScanCheckDto["supportedEngines"];
  categories?: string;
  defaultEnabled: boolean;
  requiresCredentials: boolean;
  isIntrusive: boolean;
  configurationSchema?: string;
  parserType: ScanCheckDto["parserType"];
  matcherType: ScanCheckDto["matcherType"];
  matcherExpression: string;
  severity: ScanCheckDto["severity"];
  weight: number;
  remediationTemplate?: string;
  documentationUrl?: string;
  approvalStatus: ScanCheckDto["approvalStatus"];
};

export async function getScanChecks(): Promise<NseScriptDto[]> {
  const checks = await apiGet<ScanCheckDto[]>(endpoints.scanChecks);

  return checks.map(toNseScriptDto);
}

export function getScanExecutorCapabilities(): Promise<ScanExecutorCapabilityDto[]> {
  return apiGet<ScanExecutorCapabilityDto[]>(`${endpoints.scanChecks}/executors`);
}

export async function createScanCheck(
  request: Required<Pick<UpsertScanCheckRequest, "id">> & UpsertScanCheckRequest,
): Promise<NseScriptDto> {
  return toNseScriptDto(await apiPost<ScanCheckDto, typeof request>(endpoints.scanChecks, request));
}

export async function updateScanCheck(
  id: string,
  request: UpsertScanCheckRequest,
): Promise<NseScriptDto> {
  return toNseScriptDto(await apiPut<ScanCheckDto, UpsertScanCheckRequest>(`${endpoints.scanChecks}/${id}`, request));
}

export function deleteScanCheck(id: string): Promise<void> {
  return apiDelete(`${endpoints.scanChecks}/${id}`);
}

function toNseScriptDto(check: ScanCheckDto): NseScriptDto {
  return {
    id: check.id,
    executorType: check.executorType,
    nseScriptName: check.name,
    displayName: check.displayName,
    supportedEngines: check.supportedEngines,
    defaultEnabled: check.defaultEnabled,
    requiresCredentials: check.requiresCredentials,
    isIntrusive: check.isIntrusive,
    approvalStatus: check.approvalStatus,
    description: check.description,
    matcherType: check.matcherType,
    parserType: check.parserType,
    matcherExpression: check.matcherExpression,
    severity: check.severity,
    weight: check.weight,
    categories: check.categories,
    configurationSchema: check.configurationSchema,
    remediationTemplate: check.remediationTemplate,
    docUrl: check.documentationUrl,
    source: check.source,
    isInstalled: check.isInstalled,
    canDelete: check.canDelete,
  };
}
