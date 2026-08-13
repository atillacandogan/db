import { apiDelete, apiGet, apiPost, apiPut } from "@/lib/api/client";
import { endpoints } from "@/lib/api/endpoints";
import type { MatcherType, NseScriptDto, NseScriptEngine, NseScriptStatus, ParserType, ScriptSeverity } from "@/schemas/nse-script.schema";

export type UpsertNseScriptRequest = {
  id?: string;
  nseScriptName?: string;
  displayName: string;
  description: string;
  supportedEngines: NseScriptEngine[];
  defaultEnabled: boolean;
  approvalStatus: NseScriptStatus;
  matcherType: MatcherType;
  parserType: ParserType;
  matcherExpression: string;
  severity: ScriptSeverity;
  weight: number;
  categories?: string;
  remediationTemplate?: string;
  docUrl?: string;
};

export type NseScriptSyncResult = {
  scriptDirectory: string;
  discoveredCount: number;
  addedCount: number;
  updatedCount: number;
  unchangedCount: number;
  skippedCount: number;
  failedCount: number;
  uninstalledCount: number;
};

export function getNseScripts(): Promise<NseScriptDto[]> {
  return apiGet<NseScriptDto[]>(endpoints.nseScripts);
}

export function syncNseScripts(): Promise<NseScriptSyncResult> {
  return apiPost<NseScriptSyncResult, Record<string, never>>(`${endpoints.nseScripts}/sync`, {});
}

export function createNseScript(request: Required<Pick<UpsertNseScriptRequest, "id" | "nseScriptName">> & UpsertNseScriptRequest): Promise<NseScriptDto> {
  return apiPost<NseScriptDto, typeof request>(endpoints.nseScripts, request);
}

export function updateNseScript(id: string, request: UpsertNseScriptRequest): Promise<NseScriptDto> {
  return apiPut<NseScriptDto, UpsertNseScriptRequest>(`${endpoints.nseScripts}/${id}`, request);
}

export function deleteNseScript(id: string): Promise<void> {
  return apiDelete(`${endpoints.nseScripts}/${id}`);
}
