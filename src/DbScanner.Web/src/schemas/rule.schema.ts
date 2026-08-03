export type RuleSeverity = "critical" | "high" | "medium" | "low" | "info";

export type RuleDto = {
  id: string;
  title: string;
  description: string;
  severity: RuleSeverity;
  weight: number;
  enabled: boolean;
  engine: "generic" | "postgresql" | "mysql" | "mssql" | "oracle";
  parserType: "generic_output" | "structured_xml" | "custom";
  matcherType: "contains" | "regex" | "key_value" | "xpath" | "custom";
  matcherExpression: string;
  remediation: string;
  supportedEngines: Array<"postgresql" | "mysql" | "mssql" | "oracle">;
  nseScripts: string[];
};

export type RuleSupportMatrixDto = {
  ruleId: string;
  engine: "postgresql" | "mysql" | "mssql" | "oracle" | "generic";
  unsupportedStatus: "passed" | "failed" | "not_applicable" | "not_supported" | "error";
};
