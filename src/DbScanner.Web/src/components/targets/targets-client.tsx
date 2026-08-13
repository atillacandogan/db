"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import { ScanStatusBadge } from "@/components/feedback/scan-status-badge";
import { ScopeBoundaryNote } from "@/components/security/scope-boundary-note";
import { DiscoveryPanel } from "@/components/targets/discovery-panel";
import { createScan } from "@/lib/api/scans";
import { getNseScripts } from "@/lib/api/nse-scripts";
import {
  createTarget,
  deleteTarget,
  getTargets,
} from "@/lib/api/targets";
import { resolveRiskLevel } from "@/lib/utils/risk";
import type { NseScriptDto } from "@/schemas/nse-script.schema";
import type { DatabaseEngine, TargetConnectionMode, TargetDto } from "@/schemas/target.schema";

type FormState = {
  connectionMode: TargetConnectionMode;
  name: string;
  host: string;
  port: string;
  engine: DatabaseEngine;
  environmentTag: string;
  connectionString: string;
};

const initialFormState: FormState = {
  connectionMode: "defaultValues",
  name: "",
  host: "",
  port: "5432",
  engine: "postgresql",
  environmentTag: "demo",
  connectionString: "",
};

const nseScriptPageSize = 25;

function splitCategories(categories?: string) {
  return (categories ?? "")
    .split(",")
    .map((category) => category.trim())
    .filter(Boolean);
}

function isScriptDisabled(script: NseScriptDto) {
  return script.approvalStatus === "disabled";
}

function isScriptCompatible(script: NseScriptDto, engine: DatabaseEngine) {
  return script.supportedEngines.includes("generic") || script.supportedEngines.includes(engine);
}

function getDefaultPort(engine: DatabaseEngine) {
  if (engine === "mysql") {
    return "3306";
  }

  if (engine === "mssql") {
    return "1433";
  }

  if (engine === "oracle") {
    return "1521";
  }

  return "5432";
}

function formatEngine(engine: DatabaseEngine) {
  if (engine === "postgresql") {
    return "PostgreSQL";
  }

  if (engine === "mysql") {
    return "MySQL";
  }

  if (engine === "oracle") {
    return "Oracle Database";
  }

  return "Microsoft SQL Server";
}

function getInitiallySelectedScriptIds(scripts: NseScriptDto[], engine: DatabaseEngine) {
  return scripts
    .filter((script) =>
      !isScriptDisabled(script)
      && isScriptCompatible(script, engine)
      && script.defaultEnabled)
    .map((script) => script.id)
    .filter((scriptId, index, scriptIds) => scriptIds.indexOf(scriptId) === index);
}

function getEnabledSelectedScriptIds(
  scriptIds: string[],
  scripts: NseScriptDto[],
  engine: DatabaseEngine,
) {
  const enabledScriptIds = new Set(
    scripts
      .filter((script) => !isScriptDisabled(script) && isScriptCompatible(script, engine))
      .map((script) => script.id),
  );

  return scriptIds.filter(
    (scriptId, index) => enabledScriptIds.has(scriptId) && scriptIds.indexOf(scriptId) === index,
  );
}

function formatCreateTargetError(error: Error) {
  const safeDetails: Record<string, string> = {
    "Target name is required.": "Target Name boş olamaz.",
    "Target environment is required.": "Environment boş olamaz.",
    "Target host is required.": "Host boş olamaz.",
    "Target port must be between 1 and 65535.": "Port 1 ile 65535 arasında olmalıdır.",
    "Connection string is required.": "Connection String boş olamaz.",
    "Connection string is too long.": "Connection String 4096 karakterden uzun olamaz.",
    "Connection string must include a host.": "Connection String bir Host değeri içermelidir.",
    "Connection string must include a server.": "Connection String bir Server değeri içermelidir.",
    "Connection string could not be parsed for the selected engine.":
      "Connection String seçilen engine için ayrıştırılamadı.",
    "Target is outside the approved scan scope.": "Target izinli scan scope dışında.",
    "A target with the same host, port and engine already exists.":
      "Aynı host, port ve engine ile kayıtlı bir target zaten var.",
    "Unsupported database engine.": "Desteklenmeyen database engine.",
    "Unsupported target connection mode.": "Desteklenmeyen connection mode.",
  };
  const detail = safeDetails[error.message];

  return detail ? `Target eklenemedi.\n${detail}` : "Target eklenemedi.";
}

export function TargetsClient() {
  const queryClient = useQueryClient();
  const [formState, setFormState] = useState<FormState>(initialFormState);
  const [feedback, setFeedback] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [scriptSelections, setScriptSelections] = useState<Record<string, string[]>>({});
  const [scriptPickerTargetId, setScriptPickerTargetId] = useState<string | null>(null);
  const [targetEngineFilter, setTargetEngineFilter] = useState<"all" | DatabaseEngine>("all");

  const targetsQuery = useQuery({
    queryKey: ["targets"],
    queryFn: getTargets,
  });

  const nseScriptsQuery = useQuery({
    queryKey: ["nse-scripts"],
    queryFn: getNseScripts,
  });

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

  useEffect(() => {
    if (!targetsQuery.isSuccess || !window.location.hash.startsWith("#target-")) {
      return;
    }

    window.requestAnimationFrame(() => {
      document.getElementById(window.location.hash.slice(1))?.scrollIntoView({
        behavior: "smooth",
        block: "center",
      });
    });
  }, [targetsQuery.data, targetsQuery.isSuccess]);

  const createTargetMutation = useMutation({
    mutationFn: createTarget,
    onSuccess: async () => {
      setFeedback("Target başarıyla eklendi.");
      setError(null);
      setFormState((current) => ({
        ...initialFormState,
        connectionMode: current.connectionMode,
        engine: current.engine,
        port: getDefaultPort(current.engine),
      }));
      await queryClient.invalidateQueries({ queryKey: ["targets"] });
    },
    onError: (mutationError: Error) => {
      setError(formatCreateTargetError(mutationError));
      setFeedback(null);
    },
  });

  const deleteTargetMutation = useMutation({
    mutationFn: deleteTarget,
    onSuccess: async () => {
      setFeedback("Target başarıyla silindi.");
      setError(null);
      await queryClient.invalidateQueries({ queryKey: ["targets"] });
    },
    onError: (mutationError: Error) => {
      setError(mutationError.message);
      setFeedback(null);
    },
  });

  const createScanMutation = useMutation({
    mutationFn: createScan,
    onSuccess: async (scan) => {
      setFeedback(`Scan başarıyla başlatıldı.\nDetay: ${scan.id}`);
      setError(null);
      await queryClient.invalidateQueries({ queryKey: ["targets"] });
    },
    onError: (mutationError: Error) => {
      setError(mutationError.message);
      setFeedback(null);
    },
  });

  const sortedTargets = useMemo(
    () => [...(targetsQuery.data ?? [])].sort((left, right) => left.name.localeCompare(right.name)),
    [targetsQuery.data],
  );
  const filteredTargets = useMemo(
    () => targetEngineFilter === "all"
      ? sortedTargets
      : sortedTargets.filter((target) => target.engine === targetEngineFilter),
    [sortedTargets, targetEngineFilter],
  );

  const nseScripts = nseScriptsQuery.data ?? [];
  const scriptPickerTarget = sortedTargets.find((target) => target.id === scriptPickerTargetId) ?? null;

  function handleEngineChange(engine: DatabaseEngine) {
    setFormState((current) => ({
      ...current,
      engine,
      port: getDefaultPort(engine),
    }));
  }

  function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setFeedback(null);
    setError(null);

    const name = formState.name.trim();
    const environmentTag = formState.environmentTag.trim();

    if (!name) {
      setError("Target eklenemedi.\nTarget Name boş olamaz.");
      return;
    }

    if (!environmentTag) {
      setError("Target eklenemedi.\nEnvironment boş olamaz.");
      return;
    }

    if (formState.connectionMode === "manualConnectionString") {
      if (!formState.connectionString.trim()) {
        setError("Target eklenemedi.\nConnection String boş olamaz.");
        return;
      }

      createTargetMutation.mutate({
        name,
        engine: formState.engine,
        environmentTag,
        enabled: true,
        connectionMode: formState.connectionMode,
        connectionString: formState.connectionString,
      });
      return;
    }

    const port = Number(formState.port);

    if (!formState.host.trim()) {
      setError("Target eklenemedi.\nHost boş olamaz.");
      return;
    }

    if (!Number.isInteger(port) || port < 1 || port > 65535) {
      setError("Target eklenemedi.\nPort 1 ile 65535 arasında olmalıdır.");
      return;
    }

    createTargetMutation.mutate({
      name,
      host: formState.host.trim(),
      port,
      engine: formState.engine,
      environmentTag,
      enabled: true,
      connectionMode: formState.connectionMode,
    });
  }

  function getSelectedScriptIds(target: TargetDto) {
    const selectedScriptIds =
      scriptSelections[target.id] ?? getInitiallySelectedScriptIds(nseScripts, target.engine);
    return getEnabledSelectedScriptIds(selectedScriptIds, nseScripts, target.engine);
  }

  return (
    <section className="page-stack page-stack--wide targets-page">
      <div>
        <p className="eyebrow">Targets</p>
        <h1>Targets</h1>
      </div>

      <ScopeBoundaryNote />

      <DiscoveryPanel />

      <section className="placeholder">
        <p className="detail-label">Yeni Target</p>
        <form className="form-grid target-form-grid" onSubmit={handleSubmit}>
          <fieldset className="target-connection-mode">
            <legend>Connection Mode</legend>
            <div className="target-connection-mode__options">
              <label
                className={
                  formState.connectionMode === "defaultValues"
                    ? "target-connection-mode__option target-connection-mode__option--active"
                    : "target-connection-mode__option"
                }
              >
                <input
                  type="radio"
                  name="connectionMode"
                  value="defaultValues"
                  checked={formState.connectionMode === "defaultValues"}
                  onChange={() =>
                    setFormState((current) => ({ ...current, connectionMode: "defaultValues" }))
                  }
                />
                <span>
                  <strong>Default Values</strong>
                  <small>Host ve port alanlarını kullan</small>
                </span>
              </label>
              <label
                className={
                  formState.connectionMode === "manualConnectionString"
                    ? "target-connection-mode__option target-connection-mode__option--active"
                    : "target-connection-mode__option"
                }
              >
                <input
                  type="radio"
                  name="connectionMode"
                  value="manualConnectionString"
                  checked={formState.connectionMode === "manualConnectionString"}
                  onChange={() =>
                    setFormState((current) => ({ ...current, connectionMode: "manualConnectionString" }))
                  }
                />
                <span>
                  <strong>Manual Connection String</strong>
                  <small>Provider connection string&apos;ini ayrıştır</small>
                </span>
              </label>
            </div>
          </fieldset>

          <label className="field-group">
            <span>{formState.connectionMode === "manualConnectionString" ? "Target Name" : "Name"}</span>
            <input
              className="input"
              value={formState.name}
              onChange={(event) => setFormState((current) => ({ ...current, name: event.target.value }))}
              placeholder={formState.connectionMode === "manualConnectionString" ? "Local MySQL" : "Demo PostgreSQL"}
              required
            />
          </label>

          {formState.connectionMode === "defaultValues" ? (
            <label className="field-group">
              <span>Host</span>
              <input
                className="input"
                value={formState.host}
                onChange={(event) => setFormState((current) => ({ ...current, host: event.target.value }))}
                placeholder={
                  formState.engine === "mysql"
                    ? "demo-mysql-target"
                    : formState.engine === "mssql"
                      ? "demo-mssql-target"
                      : formState.engine === "oracle"
                        ? "demo-oracle-target"
                        : "demo-postgres-target"
                }
                required
              />
            </label>
          ) : (
            <label className="field-group">
              <span>Engine</span>
              <select
                className="input"
                value={formState.engine}
                onChange={(event) => handleEngineChange(event.target.value as DatabaseEngine)}
              >
                <option value="postgresql">PostgreSQL</option>
                <option value="mysql">MySQL</option>
                <option value="mssql">Microsoft SQL Server</option>
                <option value="oracle">Oracle Database</option>
              </select>
            </label>
          )}

          {formState.connectionMode === "defaultValues" ? (
            <>
              <label className="field-group">
                <span>Port</span>
                <input
                  className="input"
                  type="number"
                  min={1}
                  max={65535}
                  value={formState.port}
                  onChange={(event) => setFormState((current) => ({ ...current, port: event.target.value }))}
                  required
                />
              </label>

              <label className="field-group">
                <span>Engine</span>
                <select
                  className="input"
                  value={formState.engine}
                  onChange={(event) => handleEngineChange(event.target.value as DatabaseEngine)}
                >
                  <option value="postgresql">PostgreSQL</option>
                  <option value="mysql">MySQL</option>
                  <option value="mssql">Microsoft SQL Server</option>
                  <option value="oracle">Oracle Database</option>
                </select>
              </label>
            </>
          ) : (
            <label className="field-group target-connection-string-field">
              <span>Connection String</span>
              <textarea
                className="input target-connection-string-input"
                value={formState.connectionString}
                onChange={(event) =>
                  setFormState((current) => ({ ...current, connectionString: event.target.value }))
                }
                placeholder={
                  formState.engine === "mysql"
                    ? "Server=localhost;Port=3306;Database=demo;User=root;Password=1234;"
                    : formState.engine === "mssql"
                      ? "Server=demo-mssql-target,1433;Database=DbScannerDemo;User Id=dbscanner_scanner;Password=<password>;"
                      : formState.engine === "oracle"
                        ? "User Id=dbscanner_scanner;Password=<password>;Data Source=demo-oracle-target:1521/FREEPDB1;"
                        : "Host=localhost;Port=5432;Database=demo;Username=postgres;Password=1234;"
                }
                autoComplete="off"
                spellCheck={false}
                aria-describedby="connection-string-security-note"
                required
              />
              <small id="connection-string-security-note" className="target-connection-string-note">
                Connection string yalnızca host ve port bilgilerini ayıklamak için kullanılır; parola kaydedilmez.
              </small>
            </label>
          )}

          <label className="field-group">
            <span>Environment</span>
            <input
              className="input"
              value={formState.environmentTag}
              onChange={(event) => setFormState((current) => ({ ...current, environmentTag: event.target.value }))}
              placeholder="demo"
              required
            />
          </label>

          <div className="form-actions target-form-actions">
            <Button type="submit" disabled={createTargetMutation.isPending}>
              {createTargetMutation.isPending ? "Kaydediliyor" : "Target Ekle"}
            </Button>
          </div>
        </form>

      </section>

      <section id="registered-targets" className="placeholder">
        <div className="targets-section-heading">
          <div>
            <p className="detail-label">Kayitli Targetlar</p>
            <p className="muted">Tarama hedefleri, script seçimi ve son scan durumları.</p>
          </div>
          <div className="targets-filter-actions">
            <label className="field-group targets-engine-filter">
              <span>Engine filter</span>
              <select
                className="input"
                value={targetEngineFilter}
                onChange={(event) => setTargetEngineFilter(event.target.value as "all" | DatabaseEngine)}
              >
                <option value="all">All engines</option>
                <option value="postgresql">PostgreSQL</option>
                <option value="mysql">MySQL</option>
                <option value="mssql">Microsoft SQL Server</option>
                <option value="oracle">Oracle Database</option>
              </select>
            </label>
            <span className="targets-count">{filteredTargets.length} / {sortedTargets.length} target</span>
          </div>
        </div>
        {targetsQuery.isLoading ? <p className="muted">Target listesi yukleniyor.</p> : null}
        {targetsQuery.isError ? <p className="feedback-error">Target listesi alinamadi.</p> : null}

        {!targetsQuery.isLoading && !targetsQuery.isError ? (
          <div className="targets-grid">
            {filteredTargets.length > 0 ? (
              <div className="targets-grid__header" aria-hidden="true">
                <span>Target Info</span>
                <span>Engine / Environment</span>
                <span>NSE Scripts</span>
                <span>Status</span>
                <span>Actions</span>
              </div>
            ) : null}

            {filteredTargets.map((target) => {
              const selectedScriptIds = getSelectedScriptIds(target);
              const isScanStarting = createScanMutation.isPending
                && createScanMutation.variables?.targetId === target.id;

              return (
                <article id={`target-${target.id}`} key={target.id} className="target-card">
                  <div className="target-card__main">
                    <div className="target-card__info target-card__cell">
                      <span className="target-card__cell-label">Target Info</span>
                      <strong className="target-card__name">{target.name}</strong>
                      <div className="target-card__detail">
                        <span>Host</span>
                        <strong>{target.host}</strong>
                      </div>
                      <div className="target-card__detail">
                        <span>Port</span>
                        <strong>{target.port}</strong>
                      </div>
                    </div>

                    <div className="target-card__context target-card__cell">
                      <span className="target-card__cell-label">Engine / Environment</span>
                      <span className="target-card__engine">{formatEngine(target.engine)}</span>
                      <div className="target-card__detail">
                        <span>Environment</span>
                        <strong>{target.environmentTag ?? "-"}</strong>
                      </div>
                    </div>

                    <div className="target-card__scripts target-card__cell">
                      <span className="target-card__cell-label">NSE Scripts</span>
                      <strong>{selectedScriptIds.length}</strong>
                      <span className="muted">scripts selected</span>
                      {nseScriptsQuery.isLoading ? <span className="muted">Yukleniyor...</span> : null}
                      {nseScriptsQuery.isError ? <span className="feedback-error">Alinamadi</span> : null}
                    </div>

                    <div className="target-card__status target-card__cell">
                      <span className="target-card__cell-label">Status</span>
                      <span className={`target-status-pill${target.enabled ? " target-status-pill--enabled" : " target-status-pill--disabled"}`}>
                        {target.enabled ? "Enabled" : "Disabled"}
                      </span>
                      {target.lastScan ? <ScanStatusBadge status={target.lastScan.status} /> : <span className="muted">No scan yet</span>}
                    </div>

                    <div className="target-card__actions target-card__cell">
                      <span className="target-card__cell-label">Actions</span>
                      <div className="target-card__action-buttons">
                        <Button
                          className="button button--secondary"
                          type="button"
                          disabled={nseScriptsQuery.isLoading || nseScriptsQuery.isError}
                          onClick={() => setScriptPickerTargetId(target.id)}
                        >
                          Select NSE Scripts
                        </Button>
                        <Button
                          type="button"
                          disabled={createScanMutation.isPending || !nseScriptsQuery.isSuccess || selectedScriptIds.length === 0}
                          onClick={() => {
                            setFeedback(null);
                            setError(null);
                            createScanMutation.mutate({
                              targetId: target.id,
                              selectedNseScriptIds: selectedScriptIds,
                            });
                          }}
                        >
                          {isScanStarting ? (
                            <>
                              <span className="button-spinner" aria-hidden="true" />
                              Başlatılıyor...
                            </>
                          ) : "Scan Başlat"}
                        </Button>
                        <Button
                          className="button button--danger"
                          type="button"
                          disabled={deleteTargetMutation.isPending}
                          onClick={() => {
                            setFeedback(null);
                            setError(null);
                            deleteTargetMutation.mutate(target.id);
                          }}
                        >
                          Sil
                        </Button>
                      </div>
                    </div>
                  </div>

                  <div className="target-card__scan-summary">
                    {target.lastScan ? (
                      <>
                        <div className="target-card__scan-metric">
                          <span>Son Scan</span>
                          <ScanStatusBadge status={target.lastScan.status} />
                        </div>
                        <div className="target-card__scan-metric">
                          <span>Risk</span>
                          <strong>
                            {typeof target.lastScan.riskScore === "number"
                              ? `${target.lastScan.riskScore} / ${resolveRiskLevel(
                                  target.lastScan.riskScore,
                                  target.lastScan.overallSeverity,
                                )}`
                              : "N/A"}
                          </strong>
                        </div>
                        <Link className="link-inline target-card__scan-link" href={`/scans/${target.lastScan.scanId}`}>
                          Scan detayina git
                        </Link>
                      </>
                    ) : (
                      <span className="muted">Bu target icin henuz scan yok.</span>
                    )}
                  </div>
                </article>
              );
            })}

            {sortedTargets.length === 0 ? <p className="muted">Kayitli target bulunmuyor.</p> : null}
            {sortedTargets.length > 0 && filteredTargets.length === 0 ? (
              <p className="muted">Secilen engine icin kayitli target bulunmuyor.</p>
            ) : null}
          </div>
        ) : null}
      </section>

      {scriptPickerTarget && nseScriptsQuery.isSuccess ? (
        <NseScriptSelectionModal
          key={scriptPickerTarget.id}
          target={scriptPickerTarget}
          scripts={nseScripts}
          initialSelectedScriptIds={getSelectedScriptIds(scriptPickerTarget)}
          onCancel={() => setScriptPickerTargetId(null)}
          onApply={(scriptIds) => {
            setScriptSelections((current) => ({ ...current, [scriptPickerTarget.id]: scriptIds }));
            setFeedback("NSE script seçimi kaydedildi.");
            setError(null);
            setScriptPickerTargetId(null);
          }}
        />
      ) : null}

      {feedback || error ? (
        <div
          className={`operation-toast ${error ? "operation-toast--error" : "operation-toast--success"}`}
          role={error ? "alert" : "status"}
          aria-live={error ? "assertive" : "polite"}
        >
          <span className="operation-toast__message">{error ?? feedback}</span>
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

type NseScriptSelectionModalProps = {
  target: TargetDto;
  scripts: NseScriptDto[];
  initialSelectedScriptIds: string[];
  onCancel: () => void;
  onApply: (scriptIds: string[]) => void;
};

function NseScriptSelectionModal({
  target,
  scripts,
  initialSelectedScriptIds,
  onCancel,
  onApply,
}: NseScriptSelectionModalProps) {
  const [selectedScriptIds, setSelectedScriptIds] = useState(() =>
    getEnabledSelectedScriptIds(initialSelectedScriptIds, scripts, target.engine),
  );
  const [search, setSearch] = useState("");
  const [category, setCategory] = useState("all");
  const [selectedOnly, setSelectedOnly] = useState(false);
  const [page, setPage] = useState(1);
  const selectedScriptSet = useMemo(() => new Set(selectedScriptIds), [selectedScriptIds]);
  const enabledScriptIdSet = useMemo(
    () => new Set(
      scripts
        .filter((script) => !isScriptDisabled(script) && isScriptCompatible(script, target.engine))
        .map((script) => script.id),
    ),
    [scripts, target.engine],
  );
  const categories = useMemo(
    () =>
      Array.from(new Set(scripts.flatMap((script) => splitCategories(script.categories))))
        .sort((left, right) => left.localeCompare(right)),
    [scripts],
  );
  const filteredScripts = useMemo(() => {
    const normalizedSearch = search.trim().toLowerCase();

    return scripts.filter((script) => {
      if (selectedOnly && !selectedScriptSet.has(script.id)) {
        return false;
      }

      if (category !== "all" && !splitCategories(script.categories).includes(category)) {
        return false;
      }

      if (!normalizedSearch) {
        return true;
      }

      return [script.id, script.nseScriptName, script.displayName, script.description, script.categories ?? ""]
        .join(" ")
        .toLowerCase()
        .includes(normalizedSearch);
    });
  }, [category, scripts, search, selectedOnly, selectedScriptSet]);
  const pageCount = Math.max(1, Math.ceil(filteredScripts.length / nseScriptPageSize));
  const currentPage = Math.min(page, pageCount);
  const pageStart = (currentPage - 1) * nseScriptPageSize;
  const pagedScripts = filteredScripts.slice(pageStart, pageStart + nseScriptPageSize);

  function toggleScript(scriptId: string, checked: boolean) {
    if (!enabledScriptIdSet.has(scriptId)) {
      return;
    }

    setSelectedScriptIds((current) => {
      if (checked) {
        return [...current, scriptId].filter((value, index, values) => values.indexOf(value) === index);
      }

      return current.filter((selectedScriptId) => selectedScriptId !== scriptId);
    });
  }

  return (
    <div
      className="modal-backdrop"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          onCancel();
        }
      }}
    >
      <section className="modal-panel modal-panel--script-picker" role="dialog" aria-modal="true" aria-labelledby="nse-script-picker-title">
        <div className="modal-panel__header">
          <div>
            <p className="detail-label" id="nse-script-picker-title">Select NSE Scripts</p>
            <p className="muted">{target.name} / {selectedScriptIds.length} scripts selected</p>
          </div>
        </div>

        <div className="nse-selection-toolbar">
          <input
            className="input"
            value={search}
            onChange={(event) => {
              setSearch(event.target.value);
              setPage(1);
            }}
            placeholder="Search scripts..."
            aria-label="Search scripts"
          />
          <select
            className="input"
            value={category}
            onChange={(event) => {
              setCategory(event.target.value);
              setPage(1);
            }}
            aria-label="Script category"
          >
            <option value="all">All Categories</option>
            {categories.map((categoryName) => (
              <option key={categoryName} value={categoryName}>{categoryName}</option>
            ))}
          </select>
          <label className="nse-selection-toggle">
            <input
              type="checkbox"
              checked={selectedOnly}
              onChange={(event) => {
                setSelectedOnly(event.target.checked);
                setPage(1);
              }}
            />
            <span>Selected Only</span>
          </label>
        </div>

        <div className="nse-selection-table">
          <div className="nse-selection-table__header" aria-hidden="true">
            <span />
            <span>Script Name</span>
            <span>File Name</span>
            <span>Categories</span>
            <span>Description</span>
          </div>

          <div className="nse-selection-table__body">
            {pagedScripts.map((script) => {
              const scriptDisabled = isScriptDisabled(script);
              const scriptCompatible = isScriptCompatible(script, target.engine);
              const scriptUnavailable = scriptDisabled || !scriptCompatible;
              const unavailableReason = scriptDisabled
                ? "Disabled in Rules"
                : !scriptCompatible
                  ? `Not compatible with ${formatEngine(target.engine)}`
                  : null;
              const scriptCategories = splitCategories(script.categories);

              return (
                <label
                  key={script.id}
                  className={`nse-selection-table__row${scriptUnavailable ? " nse-selection-table__row--disabled" : ""}`}
                >
                  <span className="nse-selection-table__checkbox">
                    <input
                      type="checkbox"
                      checked={selectedScriptSet.has(script.id)}
                      disabled={scriptUnavailable}
                      onChange={(event) => toggleScript(script.id, event.target.checked)}
                    />
                  </span>
                  <span className="nse-selection-table__script">
                    <strong>{script.displayName}</strong>
                    <span className="muted">{script.id}</span>
                    {unavailableReason ? <span className="nse-selection-table__availability">{unavailableReason}</span> : null}
                  </span>
                  <span className="nse-selection-table__filename">{script.nseScriptName}.nse</span>
                  <span className="nse-selection-table__categories">
                    {scriptCategories.length > 0
                      ? scriptCategories.map((scriptCategory) => (
                          <span key={`${script.id}-${scriptCategory}`} className="catalog-pill">{scriptCategory}</span>
                        ))
                      : <span className="muted">Uncategorized</span>}
                  </span>
                  <span className="nse-selection-table__description" title={script.description}>{script.description}</span>
                </label>
              );
            })}

            {pagedScripts.length === 0 ? <p className="nse-selection-table__empty">No scripts match the current filters.</p> : null}
          </div>
        </div>

        <div className="nse-selection-footer">
          <div className="nse-selection-pagination">
            <span className="muted">
              {filteredScripts.length === 0 ? "0 scripts" : `${pageStart + 1}-${Math.min(pageStart + nseScriptPageSize, filteredScripts.length)} of ${filteredScripts.length}`}
            </span>
            <Button
              className="button button--secondary"
              type="button"
              disabled={currentPage === 1}
              onClick={() => setPage(Math.max(1, currentPage - 1))}
            >
              Previous
            </Button>
            <span className="muted">Page {currentPage} of {pageCount}</span>
            <Button
              className="button button--secondary"
              type="button"
              disabled={currentPage === pageCount}
              onClick={() => setPage(Math.min(pageCount, currentPage + 1))}
            >
              Next
            </Button>
          </div>
          <div className="modal-actions">
            <Button className="button button--secondary" type="button" onClick={onCancel}>Cancel</Button>
            <Button
              type="button"
              onClick={() => onApply(getEnabledSelectedScriptIds(selectedScriptIds, scripts, target.engine))}
            >
              Apply Selection
            </Button>
          </div>
        </div>
      </section>
    </div>
  );
}
