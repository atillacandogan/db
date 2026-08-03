"use client";

import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { EmptyState } from "@/components/feedback/empty-state";
import { Button } from "@/components/ui/button";
import { createNseScript, deleteNseScript, getNseScripts, syncNseScripts, updateNseScript, type UpsertNseScriptRequest } from "@/lib/api/nse-scripts";
import { createRule, getRules, getRuleSupportMatrix, updateRule, type UpsertRuleRequest } from "@/lib/api/rules";
import { getSeverityLabel } from "@/lib/utils/severity";
import type { MatcherType, NseScriptDto, NseScriptEngine, NseScriptStatus, ParserType, ScriptSeverity } from "@/schemas/nse-script.schema";
import type { RuleDto, RuleSupportMatrixDto, RuleSeverity } from "@/schemas/rule.schema";
import type { DatabaseEngine } from "@/schemas/target.schema";

type RulesTab = "rules" | "scripts";
type RuleEngineOption = "generic" | DatabaseEngine;
const scriptPageSize = 25;

type RuleFormState = {
  id: string;
  title: string;
  description: string;
  engine: RuleEngineOption;
  severity: RuleSeverity;
  weight: string;
  enabled: boolean;
  parserType: ParserType;
  matcherType: MatcherType;
  matcherExpression: string;
  remediation: string;
  supportedEngines: DatabaseEngine[];
  nseScripts: string[];
};

type ScriptFormState = {
  id: string;
  nseScriptName: string;
  displayName: string;
  description: string;
  supportedEngines: NseScriptEngine[];
  defaultEnabled: boolean;
  approvalStatus: NseScriptStatus;
  matcherType: MatcherType;
  parserType: ParserType;
  matcherExpression: string;
  severity: ScriptSeverity;
  weight: string;
  categories: string;
  remediationTemplate: string;
  docUrl: string;
};

const initialRuleFormState: RuleFormState = {
  id: "",
  title: "",
  description: "",
  engine: "generic",
  severity: "info",
  weight: "0",
  enabled: true,
  parserType: "generic_output",
  matcherType: "contains",
  matcherExpression: "",
  remediation: "",
  supportedEngines: ["postgresql"],
  nseScripts: [],
};

const initialScriptFormState: ScriptFormState = {
  id: "",
  nseScriptName: "",
  displayName: "",
  description: "",
  supportedEngines: ["postgresql"],
  defaultEnabled: false,
  approvalStatus: "draft",
  matcherType: "contains",
  parserType: "generic_output",
  matcherExpression: "",
  severity: "info",
  weight: "0",
  categories: "",
  remediationTemplate: "",
  docUrl: "",
};

function splitCategories(categories?: string) {
  return (categories ?? "")
    .split(",")
    .map((category) => category.trim().toLowerCase())
    .filter(Boolean);
}

function formatEngine(engine: NseScriptEngine | RuleEngineOption) {
  if (engine === "postgresql") {
    return "PostgreSQL";
  }

  if (engine === "mysql") {
    return "MySQL";
  }

  if (engine === "mssql") {
    return "Microsoft SQL Server";
  }

  if (engine === "oracle") {
    return "Oracle Database";
  }

  return "Generic";
}

function normalizeParserType(value: string): ParserType {
  return value.trim().toLowerCase() === "genericoutput"
    ? "generic_output"
    : value.trim().toLowerCase() === "structuredxml"
      ? "structured_xml"
      : value.trim().toLowerCase() as ParserType;
}

function normalizeMatcherType(value: string): MatcherType {
  return value.trim().toLowerCase() === "keyvalue"
    ? "key_value"
    : value.trim().toLowerCase() as MatcherType;
}

function createScriptUpdateRequest(
  script: NseScriptDto,
  approvalStatus: NseScriptStatus = script.approvalStatus,
  defaultEnabled: boolean = script.defaultEnabled,
): UpsertNseScriptRequest {
  return {
    displayName: script.displayName,
    description: script.description,
    supportedEngines: script.supportedEngines,
    defaultEnabled: approvalStatus === "disabled" ? false : defaultEnabled,
    approvalStatus,
    matcherType: normalizeMatcherType(script.matcherType),
    parserType: normalizeParserType(script.parserType),
    matcherExpression: script.matcherExpression,
    severity: script.severity,
    weight: script.weight,
    categories: script.categories,
    remediationTemplate: script.remediationTemplate,
    docUrl: script.docUrl,
  };
}

type InlineSwitchProps = {
  checked: boolean;
  disabled?: boolean;
  label: string;
  onChange: (checked: boolean) => void;
};

function InlineSwitch({ checked, disabled = false, label, onChange }: InlineSwitchProps) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      className={`inline-switch${checked ? " inline-switch--checked" : ""}`}
      disabled={disabled}
      onClick={() => onChange(!checked)}
    >
      <span className="inline-switch__track" aria-hidden="true">
        <span className="inline-switch__thumb" />
      </span>
      <span className="inline-switch__label">{checked ? "ON" : "OFF"}</span>
    </button>
  );
}

export function RulesClient() {
  const queryClient = useQueryClient();
  const [activeTab, setActiveTab] = useState<RulesTab>("rules");
  const [ruleFormState, setRuleFormState] = useState<RuleFormState>(initialRuleFormState);
  const [scriptFormState, setScriptFormState] = useState<ScriptFormState>(initialScriptFormState);
  const [selectedRuleId, setSelectedRuleId] = useState<string | null>(null);
  const [selectedScriptId, setSelectedScriptId] = useState<string | null>(null);
  const [isRuleModalOpen, setIsRuleModalOpen] = useState(false);
  const [isScriptModalOpen, setIsScriptModalOpen] = useState(false);
  const [scriptPendingDetails, setScriptPendingDetails] = useState<NseScriptDto | null>(null);
  const [scriptPendingDelete, setScriptPendingDelete] = useState<NseScriptDto | null>(null);
  const [scriptSearch, setScriptSearch] = useState("");
  const [scriptCategory, setScriptCategory] = useState("all");
  const [scriptPage, setScriptPage] = useState(1);
  const [feedback, setFeedback] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const rulesQuery = useQuery({ queryKey: ["rules"], queryFn: getRules });
  const matrixQuery = useQuery({ queryKey: ["rules", "support-matrix"], queryFn: getRuleSupportMatrix });
  const scriptsQuery = useQuery({ queryKey: ["nse-scripts"], queryFn: getNseScripts });

  useEffect(() => {
    if (!feedback && !error) {
      return;
    }

    const timeoutId = window.setTimeout(() => {
      setFeedback(null);
      setError(null);
    }, 5000);

    return () => window.clearTimeout(timeoutId);
  }, [error, feedback]);

  const supportByRule = useMemo(() => {
    const map = new Map<string, RuleSupportMatrixDto[]>();
    for (const item of matrixQuery.data ?? []) {
      const entries = map.get(item.ruleId) ?? [];
      entries.push(item);
      map.set(item.ruleId, entries);
    }

    return map;
  }, [matrixQuery.data]);

  const sortedRules = useMemo(
    () => [...(rulesQuery.data ?? [])].sort((left, right) => left.title.localeCompare(right.title)),
    [rulesQuery.data],
  );

  const sortedScripts = useMemo(
    () => [...(scriptsQuery.data ?? [])].sort((left, right) => left.displayName.localeCompare(right.displayName)),
    [scriptsQuery.data],
  );

  const scriptCategoryOptions = useMemo(() => {
    const categories = new Set<string>();
    for (const script of sortedScripts) {
      for (const category of splitCategories(script.categories)) {
        categories.add(category);
      }
    }

    return [...categories].sort((left, right) => left.localeCompare(right));
  }, [sortedScripts]);

  const filteredScripts = useMemo(() => {
    const search = scriptSearch.trim().toLowerCase();

    return sortedScripts.filter((script) => {
      const categories = splitCategories(script.categories);
      const matchesCategory = scriptCategory === "all" || categories.includes(scriptCategory);
      const haystack = [
        script.id,
        script.nseScriptName,
        script.displayName,
        script.description,
        script.approvalStatus,
        script.severity,
        script.parserType,
        script.matcherType,
        script.supportedEngines.join(" "),
        categories.join(" "),
      ]
        .join(" ")
        .toLowerCase();

      return matchesCategory && (!search || haystack.includes(search));
    });
  }, [scriptCategory, scriptSearch, sortedScripts]);

  const scriptPageCount = Math.max(1, Math.ceil(filteredScripts.length / scriptPageSize));
  const currentScriptPage = Math.min(scriptPage, scriptPageCount);
  const pagedScripts = filteredScripts.slice((currentScriptPage - 1) * scriptPageSize, currentScriptPage * scriptPageSize);

  const ruleMutation = useMutation({
    mutationFn: async (request: UpsertRuleRequest) => {
      if (selectedRuleId) {
        return updateRule(selectedRuleId, request);
      }

      return createRule({
        ...request,
        id: ruleFormState.id,
      });
    },
    onSuccess: async (rule) => {
      setSelectedRuleId(rule.id);
      loadRuleIntoForm(rule);
      setFeedback(`Rule kaydedildi: ${rule.id}`);
      setError(null);
      await queryClient.invalidateQueries({ queryKey: ["rules"] });
      await queryClient.invalidateQueries({ queryKey: ["rules", "support-matrix"] });
      setIsRuleModalOpen(false);
    },
    onError: (mutationError: Error) => {
      setError(mutationError.message);
      setFeedback(null);
    },
  });

  const scriptMutation = useMutation({
    mutationFn: async (request: UpsertNseScriptRequest) => {
      if (selectedScriptId) {
        return updateNseScript(selectedScriptId, request);
      }

      return createNseScript({
        ...request,
        id: scriptFormState.id.trim(),
        nseScriptName: scriptFormState.nseScriptName.trim(),
      });
    },
    onSuccess: async (script) => {
      setSelectedScriptId(script.id);
      loadScriptIntoForm(script);
      setFeedback(`Script kaydedildi: ${script.id}`);
      setError(null);
      await queryClient.invalidateQueries({ queryKey: ["nse-scripts"] });
      setIsScriptModalOpen(false);
    },
    onError: (mutationError: Error) => {
      setError(mutationError.message);
      setFeedback(null);
    },
  });

  const syncScriptsMutation = useMutation({
    mutationFn: syncNseScripts,
    onSuccess: async (result) => {
      setFeedback(`Sync başarılı: ${result.addedCount} eklendi, ${result.updatedCount} güncellendi, ${result.unchangedCount} değişmedi.`);
      setError(null);
      await queryClient.invalidateQueries({ queryKey: ["nse-scripts"] });
    },
    onError: () => {
      setError("NSE script sync failed.");
      setFeedback(null);
    },
  });

  const scriptEnabledMutation = useMutation({
    mutationFn: ({ script, enabled }: { script: NseScriptDto; enabled: boolean }) =>
      updateNseScript(
        script.id,
        createScriptUpdateRequest(
          script,
          enabled && script.approvalStatus === "disabled" ? "draft" : enabled ? script.approvalStatus : "disabled",
          enabled ? script.defaultEnabled : false,
        ),
      ),
    onMutate: async ({ script, enabled }) => {
      await queryClient.cancelQueries({ queryKey: ["nse-scripts"] });
      const previousScripts = queryClient.getQueryData<NseScriptDto[]>(["nse-scripts"]);
      const approvalStatus = enabled
        ? script.approvalStatus === "disabled" ? "draft" : script.approvalStatus
        : "disabled";
      const updatedScript = {
        ...script,
        approvalStatus,
        defaultEnabled: enabled ? script.defaultEnabled : false,
      } satisfies NseScriptDto;

      queryClient.setQueryData<NseScriptDto[]>(["nse-scripts"], (current = []) =>
        current.map((item) => item.id === script.id ? updatedScript : item),
      );

      return { previousScripts };
    },
    onSuccess: async (script) => {
      setFeedback(`Enabled updated: ${script.displayName}`);
      setError(null);
      setScriptPendingDetails((current) => current?.id === script.id ? script : current);
      await queryClient.invalidateQueries({ queryKey: ["nse-scripts"] });
    },
    onError: (mutationError: Error, _variables, context) => {
      if (context?.previousScripts) {
        queryClient.setQueryData(["nse-scripts"], context.previousScripts);
      }

      setError(mutationError.message);
      setFeedback(null);
    },
  });

  const scriptDefaultMutation = useMutation({
    mutationFn: ({ script, defaultEnabled }: { script: NseScriptDto; defaultEnabled: boolean }) =>
      updateNseScript(
        script.id,
        createScriptUpdateRequest(script, script.approvalStatus, defaultEnabled),
      ),
    onMutate: async ({ script, defaultEnabled }) => {
      await queryClient.cancelQueries({ queryKey: ["nse-scripts"] });
      const previousScripts = queryClient.getQueryData<NseScriptDto[]>(["nse-scripts"]);
      const updatedScript = {
        ...script,
        defaultEnabled: script.approvalStatus === "disabled" ? false : defaultEnabled,
      } satisfies NseScriptDto;

      queryClient.setQueryData<NseScriptDto[]>(["nse-scripts"], (current = []) =>
        current.map((item) => item.id === script.id ? updatedScript : item),
      );

      return { previousScripts };
    },
    onSuccess: async (script) => {
      queryClient.setQueryData<NseScriptDto[]>(["nse-scripts"], (current = []) =>
        current.map((item) => item.id === script.id ? script : item),
      );
      setFeedback(`Default updated: ${script.displayName}`);
      setError(null);
      setScriptPendingDetails((current) => current?.id === script.id ? script : current);
      await queryClient.invalidateQueries({ queryKey: ["nse-scripts"] });
    },
    onError: (mutationError: Error, _variables, context) => {
      if (context?.previousScripts) {
        queryClient.setQueryData(["nse-scripts"], context.previousScripts);
      }

      setError(mutationError.message);
      setFeedback(null);
    },
  });

  const deleteScriptMutation = useMutation({
    mutationFn: deleteNseScript,
    onSuccess: async (_result, scriptId) => {
      if (selectedScriptId === scriptId) {
        setSelectedScriptId(null);
        setScriptFormState(initialScriptFormState);
      }

      setFeedback(`Silme başarılı: ${scriptId}`);
      setError(null);
      setScriptPendingDelete(null);
      await queryClient.invalidateQueries({ queryKey: ["nse-scripts"] });
    },
    onError: (mutationError: Error) => {
      setError(mutationError.message);
      setFeedback(null);
    },
  });

  function loadRuleIntoForm(rule: RuleDto) {
    setRuleFormState({
      id: rule.id,
      title: rule.title,
      description: rule.description,
      engine: rule.engine,
      severity: rule.severity,
      weight: String(rule.weight),
      enabled: rule.enabled,
      parserType: rule.parserType,
      matcherType: rule.matcherType,
      matcherExpression: rule.matcherExpression,
      remediation: rule.remediation,
      supportedEngines: rule.supportedEngines,
      nseScripts: rule.nseScripts,
    });
  }

  function loadScriptIntoForm(script: NseScriptDto) {
    setScriptFormState({
      id: script.id,
      nseScriptName: script.nseScriptName,
      displayName: script.displayName,
      description: script.description,
      supportedEngines: script.supportedEngines,
      defaultEnabled: script.defaultEnabled,
      approvalStatus: script.approvalStatus,
      matcherType: normalizeMatcherType(script.matcherType),
      parserType: normalizeParserType(script.parserType),
      matcherExpression: script.matcherExpression,
      severity: script.severity,
      weight: String(script.weight),
      categories: script.categories ?? "",
      remediationTemplate: script.remediationTemplate ?? "",
      docUrl: script.docUrl ?? "",
    });
  }

  function resetRuleForm() {
    setActiveTab("rules");
    setSelectedRuleId(null);
    setRuleFormState(initialRuleFormState);
    setFeedback(null);
    setError(null);
    setIsRuleModalOpen(true);
  }

  function resetScriptForm() {
    setActiveTab("scripts");
    setSelectedScriptId(null);
    setScriptFormState(initialScriptFormState);
    setFeedback(null);
    setError(null);
    setIsScriptModalOpen(true);
  }

  function loadRule(ruleId: string) {
    const rule = sortedRules.find((item) => item.id === ruleId);
    if (!rule) {
      return;
    }

    setActiveTab("rules");
    setSelectedRuleId(rule.id);
    loadRuleIntoForm(rule);
    setFeedback(null);
    setError(null);
    setIsRuleModalOpen(true);
  }

  function loadScript(scriptId: string) {
    const script = sortedScripts.find((item) => item.id === scriptId);
    if (!script) {
      return;
    }

    setActiveTab("scripts");
    setSelectedScriptId(script.id);
    loadScriptIntoForm(script);
    setFeedback(null);
    setError(null);
    setIsScriptModalOpen(true);
  }

  function closeScriptModal() {
    if (scriptMutation.isPending) {
      return;
    }

    setIsScriptModalOpen(false);
    setError(null);
  }

  function closeRuleModal() {
    if (ruleMutation.isPending) {
      return;
    }

    setIsRuleModalOpen(false);
    setError(null);
  }

  function closeDeleteConfirmation() {
    if (deleteScriptMutation.isPending) {
      return;
    }

    setScriptPendingDelete(null);
    setError(null);
  }

  function closeScriptDetails() {
    setScriptPendingDetails(null);
  }

  function toggleRuleEngine(engine: DatabaseEngine) {
    setRuleFormState((current) => ({
      ...current,
      supportedEngines: current.supportedEngines.includes(engine)
        ? current.supportedEngines.filter((item) => item !== engine)
        : [...current.supportedEngines, engine],
    }));
  }

  function toggleRuleScript(scriptId: string) {
    setRuleFormState((current) => ({
      ...current,
      nseScripts: current.nseScripts.includes(scriptId)
        ? current.nseScripts.filter((item) => item !== scriptId)
        : [...current.nseScripts, scriptId],
    }));
  }

  function toggleScriptEngine(engine: NseScriptEngine) {
    setScriptFormState((current) => ({
      ...current,
      supportedEngines: current.supportedEngines.includes(engine)
        ? current.supportedEngines.filter((item) => item !== engine)
        : [...current.supportedEngines, engine],
    }));
  }

  function handleRuleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setFeedback(null);
    setError(null);

    ruleMutation.mutate({
      title: ruleFormState.title,
      description: ruleFormState.description,
      engine: ruleFormState.engine,
      severity: ruleFormState.severity,
      weight: Number(ruleFormState.weight),
      enabled: ruleFormState.enabled,
      parserType: ruleFormState.parserType,
      matcherType: ruleFormState.matcherType,
      matcherExpression: ruleFormState.matcherExpression,
      remediation: ruleFormState.remediation,
      supportedEngines: ruleFormState.supportedEngines,
      nseScripts: ruleFormState.nseScripts,
    });
  }

  function handleScriptSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setFeedback(null);
    setError(null);

    const validationMessage = validateScriptForm();
    if (validationMessage) {
      setError(validationMessage);
      return;
    }

    scriptMutation.mutate({
      displayName: scriptFormState.displayName.trim(),
      description: scriptFormState.description.trim(),
      supportedEngines: scriptFormState.supportedEngines,
      defaultEnabled: scriptFormState.approvalStatus === "disabled" ? false : scriptFormState.defaultEnabled,
      approvalStatus: scriptFormState.approvalStatus,
      matcherType: scriptFormState.matcherType,
      parserType: scriptFormState.parserType,
      matcherExpression: scriptFormState.matcherExpression.trim(),
      severity: scriptFormState.severity,
      weight: Number(scriptFormState.weight),
      categories: scriptFormState.categories.trim() || undefined,
      remediationTemplate: scriptFormState.remediationTemplate.trim() || undefined,
      docUrl: scriptFormState.docUrl.trim() || undefined,
    });
  }

  function validateScriptForm() {
    const id = scriptFormState.id.trim();
    const nseScriptName = scriptFormState.nseScriptName.trim();
    const displayName = scriptFormState.displayName.trim();
    const description = scriptFormState.description.trim();
    const matcherExpression = scriptFormState.matcherExpression.trim();
    const weight = Number(scriptFormState.weight);

    if (!id) {
      return "Script id bos birakilamaz.";
    }

    if (!nseScriptName) {
      return "NSE script name bos birakilamaz.";
    }

    if (!displayName) {
      return "Display name bos birakilamaz.";
    }

    if (!description) {
      return "Description bos birakilamaz.";
    }

    if (!matcherExpression) {
      return "Matcher expression bos birakilamaz.";
    }

    if (scriptFormState.supportedEngines.length === 0) {
      return "En az bir supported engine secilmelidir.";
    }

    if (!Number.isFinite(weight) || weight < 0) {
      return "Weight 0 veya daha buyuk bir sayi olmalidir.";
    }

    const selectedId = selectedScriptId?.toLowerCase() ?? null;
    const duplicateScript = sortedScripts.find((script) => {
      if (selectedId && script.id.toLowerCase() === selectedId) {
        return false;
      }

      return script.id.toLowerCase() === id.toLowerCase() || script.nseScriptName.toLowerCase() === nseScriptName.toLowerCase();
    });

    if (duplicateScript) {
      return "Bu id veya NSE script name ile kayit zaten mevcut.";
    }

    return null;
  }

  return (
    <section className="page-stack page-stack--wide">
      <div className="rules-hero">
        <div>
          <p className="eyebrow">Rules</p>
          <h1>Rules</h1>
        </div>
        <p className="muted rules-hero__summary">
          Rule catalog tarama sonucunu bulguya cevirir. Script catalog ise hangi NSE script&apos;lerinin calistirilacagini ve nasil parse
          edilecegini yonetir.
        </p>
      </div>

      <section className="placeholder">
        <div className="tab-strip" role="tablist" aria-label="Rules navigation">
          <button
            type="button"
            className={`tab-strip__button${activeTab === "rules" ? " tab-strip__button--active" : ""}`}
            onClick={() => setActiveTab("rules")}
          >
            Rule Catalog
          </button>
          <button
            type="button"
            className={`tab-strip__button${activeTab === "scripts" ? " tab-strip__button--active" : ""}`}
            onClick={() => setActiveTab("scripts")}
          >
            NSE Scripts
          </button>
        </div>
        {activeTab === "rules" ? (
          <section className="rules-section">
            <div className="section-heading">
              <div>
                <p className="detail-label">Rule Catalog</p>
                <p className="muted">Kural tanimlari, engine kapsami ve script mapping yonetimi.</p>
              </div>
              <Button type="button" onClick={resetRuleForm}>
                Yeni Rule
              </Button>
            </div>

            <div className="rule-catalog-summary">
              <span className="muted">{sortedRules.length} kayit</span>
            </div>

            {rulesQuery.isLoading ? (
              <div className="rule-catalog-grid" role="status" aria-label="Rule listesi yukleniyor">
                {Array.from({ length: 6 }, (_, index) => (
                  <article key={index} className="rule-card rule-card--skeleton" aria-hidden="true">
                    <span className="rule-card-skeleton__line rule-card-skeleton__line--title" />
                    <span className="rule-card-skeleton__line rule-card-skeleton__line--id" />
                    <span className="rule-card-skeleton__line rule-card-skeleton__line--description" />
                    <span className="rule-card-skeleton__line rule-card-skeleton__line--description-short" />
                    <span className="rule-card-skeleton__line rule-card-skeleton__line--button" />
                  </article>
                ))}
              </div>
            ) : null}

            {rulesQuery.isError ? (
              <div className="rule-catalog-state">
                <p className="feedback-error">Rule listesi alinamadi.</p>
              </div>
            ) : null}

            {rulesQuery.data && sortedRules.length === 0 ? (
              <div className="rule-catalog-state">
                <EmptyState
                  title="Kayitli rule bulunamadi."
                  description="Ilk rule kaydini olusturmak icin Yeni Rule butonunu kullanin."
                />
              </div>
            ) : null}

            {rulesQuery.data && sortedRules.length > 0 ? (
              <div className="rule-catalog-grid">
                {sortedRules.map((rule) => {
                  const supportEntries = supportByRule.get(rule.id) ?? [];

                  return (
                    <article key={rule.id} className="rule-card">
                      <div className="rule-card__header">
                        <div className="rule-card__identity">
                          <h2>{rule.title}</h2>
                          <p className="rule-card__id">{rule.id}</p>
                        </div>
                        <div className="rule-card__badges">
                          <span className={`severity-badge severity-badge--${rule.severity}`}>
                            {getSeverityLabel(rule.severity)}
                          </span>
                          <span className="catalog-pill catalog-pill--muted">{formatEngine(rule.engine)}</span>
                        </div>
                      </div>

                      <p className="rule-card__description muted">{rule.description}</p>

                      <dl className="rule-card__technical">
                        <div>
                          <dt>Parser</dt>
                          <dd>{rule.parserType}</dd>
                        </div>
                        <div>
                          <dt>Matcher</dt>
                          <dd>{rule.matcherType}</dd>
                        </div>
                        <div>
                          <dt>Weight</dt>
                          <dd>{rule.weight}</dd>
                        </div>
                      </dl>

                      <div className="rule-card__mapping">
                        <span className="rule-card__mapping-label">Scripts</span>
                        <div className="catalog-pill-list">
                          {rule.nseScripts.length > 0 ? (
                            rule.nseScripts.slice(0, 3).map((scriptId) => (
                              <span key={`${rule.id}-${scriptId}`} className="catalog-pill">
                                {scriptId}
                              </span>
                            ))
                          ) : (
                            <span className="muted">Script mapping yok</span>
                          )}
                          {rule.nseScripts.length > 3 ? (
                            <span className="catalog-pill catalog-pill--muted">+{rule.nseScripts.length - 3}</span>
                          ) : null}
                        </div>
                      </div>

                      <div className="rule-card__footer">
                        <span className={`rule-card__status${rule.enabled ? " rule-card__status--enabled" : ""}`}>
                          {rule.enabled ? "Enabled" : "Disabled"}
                        </span>
                        <span className="rule-card__support muted">
                          {supportEntries.length > 0 ? `${supportEntries.length} engine desteği` : "Engine desteği yok"}
                        </span>
                        <Button type="button" onClick={() => loadRule(rule.id)}>
                          Düzenle
                        </Button>
                      </div>
                    </article>
                  );
                })}
              </div>
            ) : null}

            {isRuleModalOpen ? (
              <div
                className="modal-backdrop"
                role="presentation"
                onMouseDown={(event) => {
                  if (event.target === event.currentTarget) {
                    closeRuleModal();
                  }
                }}
              >
                <section
                  className="modal-panel modal-panel--rule-editor"
                  role="dialog"
                  aria-modal="true"
                  aria-labelledby="rule-modal-title"
                >
                  <div className="modal-panel__header">
                    <div>
                      <p className="detail-label" id="rule-modal-title">
                        {selectedRuleId ? "Rule Editor" : "Yeni Rule"}
                      </p>
                      <p className="muted">
                        {selectedRuleId ? "Secili kural kaydini guncelle." : "Yeni bir rule ve script mapping tanimi olustur."}
                      </p>
                    </div>
                  </div>

                  <form className="form-grid" onSubmit={handleRuleSubmit}>
                    <label className="field-group">
                      <span>Rule Id</span>
                      <input className="input" value={ruleFormState.id} disabled={Boolean(selectedRuleId)} onChange={(event) => setRuleFormState((current) => ({ ...current, id: event.target.value }))} required />
                    </label>

                    <label className="field-group">
                      <span>Title</span>
                      <input className="input" value={ruleFormState.title} onChange={(event) => setRuleFormState((current) => ({ ...current, title: event.target.value }))} required />
                    </label>

                    <label className="field-group">
                      <span>Description</span>
                      <input className="input" value={ruleFormState.description} onChange={(event) => setRuleFormState((current) => ({ ...current, description: event.target.value }))} required />
                    </label>

                    <label className="field-group">
                      <span>Remediation</span>
                      <input className="input" value={ruleFormState.remediation} onChange={(event) => setRuleFormState((current) => ({ ...current, remediation: event.target.value }))} required />
                    </label>

                    <label className="field-group">
                      <span>Engine</span>
                      <select className="input" value={ruleFormState.engine} onChange={(event) => setRuleFormState((current) => ({ ...current, engine: event.target.value as RuleEngineOption }))}>
                        <option value="generic">Generic</option>
                        <option value="postgresql">PostgreSQL</option>
                        <option value="mysql">MySQL</option>
                        <option value="mssql">Microsoft SQL Server</option>
                        <option value="oracle">Oracle Database</option>
                      </select>
                    </label>

                    <label className="field-group">
                      <span>Severity</span>
                      <select className="input" value={ruleFormState.severity} onChange={(event) => setRuleFormState((current) => ({ ...current, severity: event.target.value as RuleSeverity }))}>
                        <option value="info">Info</option>
                        <option value="low">Low</option>
                        <option value="medium">Medium</option>
                        <option value="high">High</option>
                        <option value="critical">Critical</option>
                      </select>
                    </label>

                    <label className="field-group">
                      <span>Parser Type</span>
                      <select className="input" value={ruleFormState.parserType} onChange={(event) => setRuleFormState((current) => ({ ...current, parserType: event.target.value as ParserType }))}>
                        <option value="generic_output">Generic Output</option>
                        <option value="structured_xml">Structured XML</option>
                        <option value="custom">Custom</option>
                      </select>
                    </label>

                    <label className="field-group">
                      <span>Matcher Type</span>
                      <select className="input" value={ruleFormState.matcherType} onChange={(event) => setRuleFormState((current) => ({ ...current, matcherType: event.target.value as MatcherType }))}>
                        <option value="contains">Contains</option>
                        <option value="regex">Regex</option>
                        <option value="key_value">Key Value</option>
                        <option value="xpath">XPath</option>
                        <option value="custom">Custom</option>
                      </select>
                    </label>

                    <label className="field-group">
                      <span>Matcher Expression</span>
                      <input className="input" value={ruleFormState.matcherExpression} onChange={(event) => setRuleFormState((current) => ({ ...current, matcherExpression: event.target.value }))} required />
                    </label>

                    <label className="field-group">
                      <span>Weight</span>
                      <input className="input" type="number" min={0} value={ruleFormState.weight} onChange={(event) => setRuleFormState((current) => ({ ...current, weight: event.target.value }))} required />
                    </label>

                    <div className="field-group rule-options-field">
                      <span>Supported Engines</span>
                      <div className="checkbox-row">
                        {(["postgresql", "mysql", "mssql", "oracle"] as DatabaseEngine[]).map((engine) => (
                          <label key={engine} className="checkbox-item">
                            <input type="checkbox" checked={ruleFormState.supportedEngines.includes(engine)} onChange={() => toggleRuleEngine(engine)} />
                            <span>{formatEngine(engine)}</span>
                          </label>
                        ))}
                      </div>
                    </div>

                    <div className="field-group rule-scripts-field">
                      <span>Mapped Scripts</span>
                      <div className="checkbox-grid">
                        {sortedScripts.map((script) => (
                          <label key={script.id} className="checkbox-item">
                            <input type="checkbox" checked={ruleFormState.nseScripts.includes(script.id)} onChange={() => toggleRuleScript(script.id)} />
                            <span>{script.displayName}</span>
                          </label>
                        ))}
                      </div>
                    </div>

                    <label className="checkbox-item checkbox-item--inline rule-enabled-field">
                      <input type="checkbox" checked={ruleFormState.enabled} onChange={(event) => setRuleFormState((current) => ({ ...current, enabled: event.target.checked }))} />
                      <span>Enabled</span>
                    </label>

                    <div className="form-actions form-actions--split rule-form-actions">
                      <Button className="button button--secondary" type="button" onClick={closeRuleModal} disabled={ruleMutation.isPending}>
                        İptal
                      </Button>
                      <Button type="submit" disabled={ruleMutation.isPending}>
                        {ruleMutation.isPending ? "Kaydediliyor" : selectedRuleId ? "Rule Güncelle" : "Rule Ekle"}
                      </Button>
                    </div>
                  </form>
                </section>
              </div>
            ) : null}
          </section>
        ) : (
          <section className="rules-section">
            <div className="section-heading">
              <div>
                <p className="detail-label">NSE Scripts</p>
                <p className="muted">Script envanteri ve parser tanimlari.</p>
              </div>
              <div className="section-heading-actions">
                <Button
                  className="button button--secondary"
                  type="button"
                  disabled={syncScriptsMutation.isPending}
                  onClick={() => {
                    setFeedback(null);
                    setError(null);
                    syncScriptsMutation.mutate();
                  }}
                >
                  {syncScriptsMutation.isPending ? (
                    <>
                      <span className="button-spinner" aria-hidden="true" />
                      Sync yapılıyor...
                    </>
                  ) : "Sync"}
                </Button>
                <Button type="button" onClick={resetScriptForm}>
                  Yeni NSE Script
                </Button>
              </div>
            </div>

            {scriptsQuery.isLoading ? <p className="muted">Script listesi yukleniyor.</p> : null}
            {scriptsQuery.isError ? <p className="feedback-error">Script listesi alinamadi.</p> : null}

            {scriptsQuery.data ? (
              <div className="nse-script-grid">
                <div className="script-grid-toolbar">
                  <div>
                    <p className="detail-label">Script Catalog</p>
                    <p className="muted">
                      {sortedScripts.length} scripts toplam, {filteredScripts.length} sonuc gosteriliyor.
                    </p>
                  </div>

                  <div className="script-grid-controls">
                    <input
                      className="input"
                      placeholder="Script ara..."
                      value={scriptSearch}
                      onChange={(event) => {
                        setScriptSearch(event.target.value);
                        setScriptPage(1);
                      }}
                    />
                    <select
                      className="input"
                      value={scriptCategory}
                      onChange={(event) => {
                        setScriptCategory(event.target.value);
                        setScriptPage(1);
                      }}
                    >
                      <option value="all">Tum kategoriler</option>
                      {scriptCategoryOptions.map((category) => (
                        <option key={category} value={category}>
                          {category}
                        </option>
                      ))}
                    </select>
                  </div>
                </div>

                <div className="catalog-table nse-script-table">
                  <div className="catalog-table__header">
                    <span>Script Name</span>
                    <span>File Name</span>
                    <span>Categories</span>
                    <span>Enabled</span>
                    <span>Default</span>
                    <span>Actions</span>
                  </div>

                  {pagedScripts.map((script) => {
                    const isAvailable = script.source === "manual" || script.isInstalled !== false;
                    const isEnabled = isAvailable && script.approvalStatus !== "disabled";
                    const isEnabledSaving = scriptEnabledMutation.isPending
                      && scriptEnabledMutation.variables?.script.id === script.id;
                    const isDefaultSaving = scriptDefaultMutation.isPending
                      && scriptDefaultMutation.variables?.script.id === script.id;
                    const isSwitchSaving = isEnabledSaving || isDefaultSaving;

                    return (
                      <article key={script.id} className={`catalog-table__row${selectedScriptId === script.id ? " catalog-table__row--selected" : ""}`}>
                      <div className="catalog-table__cell catalog-table__cell--primary nse-script-table__script">
                        <strong>{script.displayName}</strong>
                        <span className="muted">{script.id}</span>
                        <div className="catalog-pill-list">
                          {script.supportedEngines.map((engine) => (
                            <span key={`${script.id}-${engine}`} className="catalog-pill">
                              {formatEngine(engine)}
                            </span>
                          ))}
                          <span className={`catalog-pill${script.source === "installed" ? " catalog-pill--muted" : ""}`}>
                            {script.source === "installed"
                              ? script.isInstalled === false ? "Not installed" : "Installed"
                              : "Manual"}
                          </span>
                        </div>
                      </div>

                      <div className="catalog-table__cell nse-script-table__filename">
                        <span>{script.nseScriptName}.nse</span>
                      </div>

                      <div className="catalog-table__cell nse-script-table__categories">
                        {splitCategories(script.categories).length > 0 ? (
                          splitCategories(script.categories).map((category) => (
                            <span key={`${script.id}-${category}`} className="catalog-pill">
                              {category}
                            </span>
                          ))
                        ) : (
                          <span className="muted">-</span>
                        )}
                      </div>

                      <div className="catalog-table__cell nse-script-table__status">
                        <InlineSwitch
                          checked={isEnabled}
                          disabled={!isAvailable || isSwitchSaving}
                          label={`${script.displayName} enabled`}
                          onChange={(enabled) => {
                            setFeedback(null);
                            setError(null);
                            scriptEnabledMutation.mutate({ script, enabled });
                          }}
                        />
                        {isEnabledSaving ? <span className="switch-saving">Saving...</span> : null}
                      </div>

                      <div className="catalog-table__cell nse-script-table__default">
                        <InlineSwitch
                          checked={isEnabled && script.defaultEnabled}
                          disabled={!isEnabled || isSwitchSaving}
                          label={`${script.displayName} default`}
                          onChange={(defaultEnabled) => {
                            setFeedback(null);
                            setError(null);
                            scriptDefaultMutation.mutate({ script, defaultEnabled });
                          }}
                        />
                        {isDefaultSaving ? <span className="switch-saving">Saving...</span> : null}
                      </div>

                      <div className="catalog-table__cell nse-script-table__actions">
                        <Button
                          className="button button--secondary"
                          type="button"
                          onClick={() => setScriptPendingDetails(script)}
                        >
                          Detaylar
                        </Button>
                        <Button type="button" onClick={() => loadScript(script.id)}>
                          Duzenle
                        </Button>
                        {script.source === "manual" && script.canDelete ? (
                          <Button
                            className="button button--danger"
                            type="button"
                            disabled={deleteScriptMutation.isPending}
                            onClick={() => {
                              setFeedback(null);
                              setError(null);
                              setScriptPendingDelete(script);
                            }}
                          >
                            Sil
                          </Button>
                        ) : null}
                      </div>
                      </article>
                    );
                  })}
                </div>

                {filteredScripts.length === 0 ? <p className="muted">Filtreye uyan NSE script bulunamadi.</p> : null}

                <div className="pagination-bar">
                  <span className="muted">
                    Sayfa {currentScriptPage} / {scriptPageCount}
                  </span>
                  <div className="pagination-actions">
                    <Button type="button" disabled={currentScriptPage === 1} onClick={() => setScriptPage(Math.max(1, currentScriptPage - 1))}>
                      Onceki
                    </Button>
                    <Button type="button" disabled={currentScriptPage === scriptPageCount} onClick={() => setScriptPage(Math.min(scriptPageCount, currentScriptPage + 1))}>
                      Sonraki
                    </Button>
                  </div>
                </div>
              </div>
            ) : null}

            {scriptsQuery.data && sortedScripts.length === 0 ? <p className="muted">Kayitli NSE script yok.</p> : null}

            {isScriptModalOpen ? (
              <div
                className="modal-backdrop"
                role="presentation"
                onMouseDown={(event) => {
                  if (event.target === event.currentTarget) {
                    closeScriptModal();
                  }
                }}
              >
                <section className="modal-panel" role="dialog" aria-modal="true" aria-labelledby="nse-script-modal-title">
                  <div className="modal-panel__header">
                    <div>
                      <p className="detail-label" id="nse-script-modal-title">
                        {selectedScriptId ? "Script Editor" : "Yeni NSE Script"}
                      </p>
                      <p className="muted">{selectedScriptId ? "Secili script kaydini guncelle." : "Yeni bir NSE script tanimi olustur."}</p>
                    </div>
                  </div>

                <form className="form-grid" onSubmit={handleScriptSubmit}>
                  <label className="field-group">
                    <span>Script Id</span>
                    <input className="input" value={scriptFormState.id} disabled={Boolean(selectedScriptId)} onChange={(event) => setScriptFormState((current) => ({ ...current, id: event.target.value }))} required />
                  </label>

                  <label className="field-group">
                    <span>NSE Script Name</span>
                    <input className="input" value={scriptFormState.nseScriptName} disabled={Boolean(selectedScriptId)} onChange={(event) => setScriptFormState((current) => ({ ...current, nseScriptName: event.target.value }))} required />
                  </label>

                  <label className="field-group">
                    <span>Display Name</span>
                    <input className="input" value={scriptFormState.displayName} onChange={(event) => setScriptFormState((current) => ({ ...current, displayName: event.target.value }))} required />
                  </label>

                  <label className="field-group">
                    <span>Description</span>
                    <input className="input" value={scriptFormState.description} onChange={(event) => setScriptFormState((current) => ({ ...current, description: event.target.value }))} required />
                  </label>

                  <label className="field-group">
                    <span>Approval</span>
                    <select
                      className="input"
                      value={scriptFormState.approvalStatus}
                      onChange={(event) => {
                        const approvalStatus = event.target.value as NseScriptStatus;
                        setScriptFormState((current) => ({
                          ...current,
                          approvalStatus,
                          defaultEnabled: approvalStatus === "disabled" ? false : current.defaultEnabled,
                        }));
                      }}
                    >
                      <option value="draft">Draft</option>
                      <option value="approved">Approved</option>
                      <option value="disabled">Disabled</option>
                    </select>
                  </label>

                  <label className="field-group">
                    <span>Severity</span>
                    <select className="input" value={scriptFormState.severity} onChange={(event) => setScriptFormState((current) => ({ ...current, severity: event.target.value as ScriptSeverity }))}>
                      <option value="info">Info</option>
                      <option value="low">Low</option>
                      <option value="medium">Medium</option>
                      <option value="high">High</option>
                      <option value="critical">Critical</option>
                    </select>
                  </label>

                  <label className="field-group">
                    <span>Matcher Type</span>
                    <select className="input" value={scriptFormState.matcherType} onChange={(event) => setScriptFormState((current) => ({ ...current, matcherType: event.target.value as MatcherType }))}>
                      <option value="contains">Contains</option>
                      <option value="regex">Regex</option>
                      <option value="key_value">Key Value</option>
                      <option value="xpath">XPath</option>
                      <option value="custom">Custom</option>
                    </select>
                  </label>

                  <label className="field-group">
                    <span>Parser Type</span>
                    <select className="input" value={scriptFormState.parserType} onChange={(event) => setScriptFormState((current) => ({ ...current, parserType: event.target.value as ParserType }))}>
                      <option value="generic_output">Generic Output</option>
                      <option value="structured_xml">Structured XML</option>
                      <option value="custom">Custom</option>
                    </select>
                  </label>

                  <label className="field-group">
                    <span>Matcher Expression</span>
                    <input className="input" value={scriptFormState.matcherExpression} onChange={(event) => setScriptFormState((current) => ({ ...current, matcherExpression: event.target.value }))} required />
                  </label>

                  <label className="field-group">
                    <span>Weight</span>
                    <input className="input" type="number" min={0} value={scriptFormState.weight} onChange={(event) => setScriptFormState((current) => ({ ...current, weight: event.target.value }))} required />
                  </label>

                  <label className="field-group">
                    <span>Categories</span>
                    <input className="input" value={scriptFormState.categories} onChange={(event) => setScriptFormState((current) => ({ ...current, categories: event.target.value }))} />
                  </label>

                  <label className="field-group">
                    <span>Remediation</span>
                    <input className="input" value={scriptFormState.remediationTemplate} onChange={(event) => setScriptFormState((current) => ({ ...current, remediationTemplate: event.target.value }))} />
                  </label>

                  <label className="field-group">
                    <span>Doc Url</span>
                    <input className="input" value={scriptFormState.docUrl} onChange={(event) => setScriptFormState((current) => ({ ...current, docUrl: event.target.value }))} />
                  </label>

                  <div className="field-group">
                    <span>Supported Engines</span>
                    <div className="checkbox-row">
                      {(["generic", "postgresql", "mysql", "mssql", "oracle"] as NseScriptEngine[]).map((engine) => (
                        <label key={engine} className="checkbox-item">
                          <input type="checkbox" checked={scriptFormState.supportedEngines.includes(engine)} onChange={() => toggleScriptEngine(engine)} />
                          <span>{formatEngine(engine)}</span>
                        </label>
                      ))}
                    </div>
                  </div>

                  <label className="checkbox-item checkbox-item--inline">
                    <input
                      type="checkbox"
                      checked={scriptFormState.defaultEnabled}
                      disabled={scriptFormState.approvalStatus === "disabled"}
                      onChange={(event) => setScriptFormState((current) => ({ ...current, defaultEnabled: event.target.checked }))}
                    />
                    <span>Default enabled</span>
                  </label>

                    <div className="form-actions form-actions--split">
                      <Button type="button" onClick={closeScriptModal} disabled={scriptMutation.isPending}>
                        Iptal
                      </Button>
                    <Button type="submit" disabled={scriptMutation.isPending}>
                      {scriptMutation.isPending ? "Kaydediliyor" : selectedScriptId ? "Script Guncelle" : "Script Ekle"}
                    </Button>
                  </div>
                </form>
              </section>
              </div>
            ) : null}

            {scriptPendingDetails ? (
              <div
                className="modal-backdrop"
                role="presentation"
                onMouseDown={(event) => {
                  if (event.target === event.currentTarget) {
                    closeScriptDetails();
                  }
                }}
              >
                <section className="modal-panel modal-panel--details" role="dialog" aria-modal="true" aria-labelledby="nse-script-details-title">
                  <div className="modal-panel__header">
                    <div>
                      <p className="detail-label" id="nse-script-details-title">NSE Script Details</p>
                      <p className="muted">Read-only script catalog information.</p>
                    </div>
                  </div>

                  <dl className="script-details-grid">
                    <div className="script-details-item">
                      <dt>Script Name</dt>
                      <dd>{scriptPendingDetails.displayName}</dd>
                      <dd className="muted">{scriptPendingDetails.id}</dd>
                    </div>
                    <div className="script-details-item">
                      <dt>File Name</dt>
                      <dd>{scriptPendingDetails.nseScriptName}.nse</dd>
                    </div>
                    <div className="script-details-item">
                      <dt>Source</dt>
                      <dd>{scriptPendingDetails.source === "installed" ? "Installed" : "Manual"}</dd>
                    </div>
                    {scriptPendingDetails.source === "installed" ? (
                      <div className="script-details-item">
                        <dt>Installed Status</dt>
                        <dd>{scriptPendingDetails.isInstalled === false ? "Not installed" : "Installed"}</dd>
                      </div>
                    ) : null}
                    <div className="script-details-item">
                      <dt>Enabled</dt>
                      <dd>{scriptPendingDetails.approvalStatus === "disabled" || scriptPendingDetails.isInstalled === false ? "No" : "Yes"}</dd>
                    </div>
                    <div className="script-details-item">
                      <dt>Approval Status</dt>
                      <dd>{scriptPendingDetails.approvalStatus}</dd>
                    </div>
                    <div className="script-details-item">
                      <dt>Default</dt>
                      <dd>{scriptPendingDetails.defaultEnabled ? "Yes" : "No"}</dd>
                    </div>
                    <div className="script-details-item script-details-item--wide">
                      <dt>Supported Engines</dt>
                      <dd className="catalog-pill-list">
                        {scriptPendingDetails.supportedEngines.map((engine) => (
                          <span key={`${scriptPendingDetails.id}-detail-${engine}`} className="catalog-pill">{formatEngine(engine)}</span>
                        ))}
                      </dd>
                    </div>
                    <div className="script-details-item script-details-item--wide">
                      <dt>Categories</dt>
                      <dd className="catalog-pill-list">
                        {splitCategories(scriptPendingDetails.categories).length > 0
                          ? splitCategories(scriptPendingDetails.categories).map((category) => (
                              <span key={`${scriptPendingDetails.id}-detail-${category}`} className="catalog-pill">{category}</span>
                            ))
                          : <span className="muted">Uncategorized</span>}
                      </dd>
                    </div>
                    <div className="script-details-item script-details-item--wide">
                      <dt>Description</dt>
                      <dd className="script-details-description">{scriptPendingDetails.description}</dd>
                    </div>
                    <div className="script-details-item">
                      <dt>Severity</dt>
                      <dd>{scriptPendingDetails.severity}</dd>
                    </div>
                    <div className="script-details-item">
                      <dt>Parser Type</dt>
                      <dd>{normalizeParserType(scriptPendingDetails.parserType)}</dd>
                    </div>
                    <div className="script-details-item">
                      <dt>Matcher Type</dt>
                      <dd>{normalizeMatcherType(scriptPendingDetails.matcherType)}</dd>
                    </div>
                    <div className="script-details-item">
                      <dt>Weight</dt>
                      <dd>{scriptPendingDetails.weight}</dd>
                    </div>
                    <div className="script-details-item">
                      <dt>Requires Credentials</dt>
                      <dd>{scriptPendingDetails.requiresCredentials ? "Yes" : "No"}</dd>
                    </div>
                    <div className="script-details-item">
                      <dt>Intrusive</dt>
                      <dd>{scriptPendingDetails.isIntrusive ? "Yes" : "No"}</dd>
                    </div>
                    <div className="script-details-item script-details-item--wide">
                      <dt>Matcher Expression</dt>
                      <dd className="script-details-description">{scriptPendingDetails.matcherExpression || "-"}</dd>
                    </div>
                    {scriptPendingDetails.remediationTemplate ? (
                      <div className="script-details-item script-details-item--wide">
                        <dt>Remediation</dt>
                        <dd className="script-details-description">{scriptPendingDetails.remediationTemplate}</dd>
                      </div>
                    ) : null}
                    {scriptPendingDetails.docUrl ? (
                      <div className="script-details-item script-details-item--wide">
                        <dt>Documentation</dt>
                        <dd className="script-details-description">{scriptPendingDetails.docUrl}</dd>
                      </div>
                    ) : null}
                  </dl>

                  <div className="modal-actions script-details-actions">
                    <Button type="button" onClick={closeScriptDetails}>Kapat</Button>
                  </div>
                </section>
              </div>
            ) : null}

            {scriptPendingDelete ? (
              <div
                className="modal-backdrop"
                role="presentation"
                onMouseDown={(event) => {
                  if (event.target === event.currentTarget) {
                    closeDeleteConfirmation();
                  }
                }}
              >
                <section className="modal-panel modal-panel--confirm" role="dialog" aria-modal="true" aria-labelledby="delete-nse-script-title">
                  <div className="modal-panel__header">
                    <div>
                      <p className="detail-label" id="delete-nse-script-title">
                        NSE Script Sil
                      </p>
                      <p className="muted">Bu NSE scriptini silmek istediğinize emin misiniz?</p>
                    </div>
                  </div>

                  <div className="confirm-summary">
                    <strong>{scriptPendingDelete.displayName}</strong>
                    <span className="muted">{scriptPendingDelete.nseScriptName}.nse</span>
                  </div>

                  <div className="modal-actions">
                    <Button className="button button--secondary" type="button" disabled={deleteScriptMutation.isPending} onClick={closeDeleteConfirmation}>
                      İptal
                    </Button>
                    <Button
                      className="button button--danger"
                      type="button"
                      disabled={deleteScriptMutation.isPending}
                      onClick={() => {
                        setFeedback(null);
                        setError(null);
                        deleteScriptMutation.mutate(scriptPendingDelete.id);
                      }}
                    >
                      {deleteScriptMutation.isPending ? (
                        <>
                          <span className="button-spinner" aria-hidden="true" />
                          Siliniyor...
                        </>
                      ) : "Sil"}
                    </Button>
                  </div>
                </section>
              </div>
            ) : null}
          </section>
        )}

      </section>

      {feedback || error ? (
        <div
          className={`operation-toast ${error ? "operation-toast--error" : "operation-toast--success"}`}
          role={error ? "alert" : "status"}
          aria-live={error ? "assertive" : "polite"}
        >
          <span>{error ?? feedback}</span>
          <button
            className="operation-toast__close"
            type="button"
            aria-label="Bildirimi kapat"
            onClick={() => {
              setFeedback(null);
              setError(null);
            }}
          >
            ×
          </button>
        </div>
      ) : null}
    </section>
  );
}
