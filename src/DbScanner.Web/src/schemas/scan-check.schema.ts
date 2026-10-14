import type {
  MatcherType,
  NseScriptEngine,
  NseScriptSource,
  NseScriptStatus,
  ParserType,
  ScriptSeverity,
} from "./nse-script.schema";

export type ScanCheckDto = {
  id: string;
  name: string;
  displayName: string;
  description: string;
  executorType: string;
  supportedEngines: NseScriptEngine[];
  categories?: string;
  defaultEnabled: boolean;
  requiresCredentials: boolean;
  isIntrusive: boolean;
  configurationSchema?: string;
  parserType: ParserType;
  matcherType: MatcherType;
  matcherExpression: string;
  severity: ScriptSeverity;
  weight: number;
  remediationTemplate?: string;
  documentationUrl?: string;
  approvalStatus: NseScriptStatus;
  source: NseScriptSource;
  isInstalled?: boolean | null;
  canDelete: boolean;
};

export type ScanExecutorCapabilityDto = {
  executorType: string;
  displayName: string;
  description: string;
  supportedEngines: NseScriptEngine[];
  allowsManualCheckCreation: boolean;
  allowsCheckNameEditing: boolean;
  supportsConfigurationSchema: boolean;
};
