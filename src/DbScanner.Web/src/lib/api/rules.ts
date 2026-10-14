import { apiGet, apiPost, apiPut } from "@/lib/api/client";
import { endpoints } from "@/lib/api/endpoints";
import type { RuleDto, RuleSupportMatrixDto } from "@/schemas/rule.schema";
import type { DatabaseEngine } from "@/schemas/target.schema";

export type UpsertRuleRequest = {
  id?: string;
  title: string;
  description: string;
  engine: "generic" | DatabaseEngine;
  severity: RuleDto["severity"];
  weight: number;
  enabled: boolean;
  parserType: RuleDto["parserType"];
  matcherType: RuleDto["matcherType"];
  matcherExpression: string;
  remediation: string;
  supportedEngines: DatabaseEngine[];
  scanChecks: string[];
};

export function getRules(): Promise<RuleDto[]> {
  return apiGet<RuleDto[]>(endpoints.rules);
}

export function createRule(request: Required<Pick<UpsertRuleRequest, "id">> & UpsertRuleRequest): Promise<RuleDto> {
  return apiPost<RuleDto, typeof request>(endpoints.rules, request);
}

export function updateRule(id: string, request: UpsertRuleRequest): Promise<RuleDto> {
  return apiPut<RuleDto, UpsertRuleRequest>(`${endpoints.rules}/${id}`, request);
}

export function getRuleSupportMatrix(): Promise<RuleSupportMatrixDto[]> {
  return apiGet<RuleSupportMatrixDto[]>(`${endpoints.rules}/support-matrix`);
}
