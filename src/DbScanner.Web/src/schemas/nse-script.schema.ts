import type { DatabaseEngine } from "./target.schema";

export type NseScriptStatus = "draft" | "approved" | "disabled";
export type MatcherType = "contains" | "regex" | "key_value" | "xpath" | "custom";
export type ParserType = "generic_output" | "structured_xml" | "custom";
export type ScriptSeverity = "critical" | "high" | "medium" | "low" | "info";
export type NseScriptEngine = "generic" | DatabaseEngine;
export type NseScriptSource = "installed" | "manual";

export type NseScriptDto = {
  id: string;
  nseScriptName: string;
  displayName: string;
  supportedEngines: NseScriptEngine[];
  defaultEnabled: boolean;
  requiresCredentials: boolean;
  isIntrusive: boolean;
  approvalStatus: NseScriptStatus;
  description: string;
  matcherType: MatcherType;
  parserType: ParserType;
  matcherExpression: string;
  severity: ScriptSeverity;
  weight: number;
  categories?: string;
  remediationTemplate?: string;
  docUrl?: string;
  source: NseScriptSource;
  isInstalled?: boolean | null;
  canDelete: boolean;
};
