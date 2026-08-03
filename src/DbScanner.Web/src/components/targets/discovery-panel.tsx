"use client";

import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import {
  addDiscoveryTargets,
  createDiscovery,
  getDiscoveries,
} from "@/lib/api/discoveries";
import type {
  DiscoveryJobDto,
  DiscoveryResultDto,
  SupportedDiscoveryEngine,
} from "@/schemas/discovery.schema";

const engineOptions: { value: SupportedDiscoveryEngine; label: string }[] = [
  { value: "mysql", label: "MySQL" },
  { value: "postgresql", label: "PostgreSQL" },
  { value: "mssql", label: "Microsoft SQL Server" },
  { value: "oracle", label: "Oracle Database" },
];

type DiscoveryForm = {
  name: string;
  scope: string;
  environment: string;
  engines: SupportedDiscoveryEngine[];
};

const initialForm: DiscoveryForm = {
  name: "Demo Database Discovery",
  scope: "demo-postgres-target\ndemo-mysql-target\ndemo-mssql-target\ndemo-oracle-target",
  environment: "demo",
  engines: ["mysql", "postgresql", "mssql", "oracle"],
};

export function DiscoveryPanel() {
  const queryClient = useQueryClient();
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [form, setForm] = useState<DiscoveryForm>(initialForm);
  const [selectedJobId, setSelectedJobId] = useState<string | null>(null);
  const [selectedResultIds, setSelectedResultIds] = useState<string[]>([]);
  const [feedback, setFeedback] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const discoveriesQuery = useQuery({
    queryKey: ["discoveries"],
    queryFn: getDiscoveries,
    refetchInterval: (query) => {
      const jobs = query.state.data ?? [];
      return jobs.some((job) => job.status === "pending" || job.status === "running") ? 2000 : false;
    },
  });

  const jobs = useMemo(() => discoveriesQuery.data ?? [], [discoveriesQuery.data]);
  const selectedJob = useMemo(
    () => jobs.find((job) => job.id === selectedJobId) ?? jobs[0] ?? null,
    [jobs, selectedJobId],
  );

  const createMutation = useMutation({
    mutationFn: createDiscovery,
    onSuccess: async (job) => {
      setSelectedJobId(job.id);
      setSelectedResultIds([]);
      setIsModalOpen(false);
      setFeedback("Discovery job kuyruğa alındı.");
      setError(null);
      await queryClient.invalidateQueries({ queryKey: ["discoveries"] });
    },
    onError: (mutationError: Error) => {
      setError(mutationError.message);
      setFeedback(null);
    },
  });

  const addTargetsMutation = useMutation({
    mutationFn: ({ jobId, resultIds }: { jobId: string; resultIds: string[] }) =>
      addDiscoveryTargets(jobId, resultIds),
    onSuccess: async (results) => {
      setSelectedResultIds([]);
      setFeedback(`${results.length} discovery sonucu target envanterine bağlandı.`);
      setError(null);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["discoveries"] }),
        queryClient.invalidateQueries({ queryKey: ["targets"] }),
      ]);
    },
    onError: (mutationError: Error) => {
      setError(mutationError.message);
      setFeedback(null);
    },
  });

  function submitDiscovery(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!form.name.trim() || !form.scope.trim() || !form.environment.trim()) {
      setError("Discovery Name, Scope ve Environment zorunludur.");
      return;
    }

    if (form.engines.length === 0) {
      setError("En az bir supported engine seçilmelidir.");
      return;
    }

    createMutation.mutate({
      name: form.name.trim(),
      scope: form.scope.trim(),
      environment: form.environment.trim(),
      supportedEngineFilter: form.engines,
    });
  }

  function toggleEngine(engine: SupportedDiscoveryEngine) {
    setForm((current) => ({
      ...current,
      engines: current.engines.includes(engine)
        ? current.engines.filter((item) => item !== engine)
        : [...current.engines, engine],
    }));
  }

  function addResults(resultIds: string[]) {
    if (!selectedJob || resultIds.length === 0) {
      return;
    }

    addTargetsMutation.mutate({ jobId: selectedJob.id, resultIds });
  }

  const addableResults = selectedJob?.supportedDatabases.filter((item) => item.canAddAsTarget) ?? [];
  const validSelectedResultIds = selectedResultIds.filter((id) =>
    addableResults.some((result) => result.id === id));
  const allAddableSelected = addableResults.length > 0
    && addableResults.every((item) => validSelectedResultIds.includes(item.id));

  return (
    <section className="discovery-panel placeholder">
      <div className="discovery-panel__heading">
        <div>
          <p className="detail-label">Database Discovery Agent</p>
          <h2>Discovery Results</h2>
          <p className="muted">
            Yalnızca allowlist içindeki host ve portlarda sınırlı TCP service/fingerprint discovery çalışır.
          </p>
        </div>
        <Button type="button" onClick={() => { setIsModalOpen(true); setError(null); }}>
          Discovery Başlat
        </Button>
      </div>

      {feedback ? <p className="feedback feedback--success">{feedback}</p> : null}
      {error && !isModalOpen ? <p className="feedback feedback--error">{error}</p> : null}

      {jobs.length > 0 ? (
        <label className="field-group discovery-job-picker">
          <span>Discovery Job</span>
          <select
            className="input"
            value={selectedJob?.id ?? ""}
            onChange={(event) => { setSelectedJobId(event.target.value); setSelectedResultIds([]); }}
          >
            {jobs.map((job) => (
              <option key={job.id} value={job.id}>
                {job.name} · {formatJobStatus(job)}
              </option>
            ))}
          </select>
        </label>
      ) : null}

      {discoveriesQuery.isLoading ? <p className="muted">Discovery jobları yükleniyor…</p> : null}
      {discoveriesQuery.isError ? <p className="feedback feedback--error">Discovery sonuçları yüklenemedi.</p> : null}
      {selectedJob ? (
        <DiscoveryResults
          job={selectedJob}
          selectedResultIds={validSelectedResultIds}
          allAddableSelected={allAddableSelected}
          isAdding={addTargetsMutation.isPending}
          onToggleResult={(resultId) => setSelectedResultIds((current) =>
            current.includes(resultId) ? current.filter((id) => id !== resultId) : [...current, resultId])}
          onToggleAll={() => setSelectedResultIds(allAddableSelected ? [] : addableResults.map((item) => item.id))}
          onAdd={addResults}
        />
      ) : (
        <p className="discovery-empty">Henüz discovery job başlatılmadı.</p>
      )}

      {isModalOpen ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setIsModalOpen(false)}>
          <section
            className="modal-panel modal-panel--discovery"
            role="dialog"
            aria-modal="true"
            aria-labelledby="discovery-title"
            onMouseDown={(event) => event.stopPropagation()}
          >
            <div className="modal-panel__header">
              <div>
                <p className="eyebrow">Authorized discovery</p>
                <h2 id="discovery-title">Database Discovery Başlat</h2>
              </div>
              <Button type="button" onClick={() => setIsModalOpen(false)}>Kapat</Button>
            </div>
            <form className="form-grid discovery-form" onSubmit={submitDiscovery}>
              <label className="field-group">
                <span>Discovery Name</span>
                <input className="input" value={form.name} onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))} />
              </label>
              <label className="field-group">
                <span>Environment</span>
                <input className="input" value={form.environment} onChange={(event) => setForm((current) => ({ ...current, environment: event.target.value }))} />
              </label>
              <label className="field-group discovery-form__scope">
                <span>Scope veya host listesi</span>
                <textarea
                  className="input discovery-scope-input"
                  value={form.scope}
                  onChange={(event) => setForm((current) => ({ ...current, scope: event.target.value }))}
                  placeholder={"192.168.1.0/24\ndb-server-01\n10.0.0.15"}
                />
              </label>
              <fieldset className="discovery-engine-filter">
                <legend>Supported engine filter</legend>
                {engineOptions.map((option) => (
                  <label key={option.value}>
                    <input type="checkbox" checked={form.engines.includes(option.value)} onChange={() => toggleEngine(option.value)} />
                    <span>{option.label}</span>
                  </label>
                ))}
              </fieldset>
              <p className="discovery-security-note">
                Kimlik bilgisi istenmez; parola denemesi, brute force, exploit, veri okuma/değiştirme ve DoS yapılmaz.
                Geniş public CIDR’lar reddedilir; concurrency ve probe hızı sınırlandırılmıştır.
              </p>
              {error ? <p className="feedback feedback--error discovery-form__feedback">{error}</p> : null}
              <div className="form-actions discovery-form__actions">
                <Button type="submit" disabled={createMutation.isPending}>
                  {createMutation.isPending ? "Kuyruğa alınıyor…" : "Discovery Başlat"}
                </Button>
              </div>
            </form>
          </section>
        </div>
      ) : null}
    </section>
  );
}

type DiscoveryResultsProps = {
  job: DiscoveryJobDto;
  selectedResultIds: string[];
  allAddableSelected: boolean;
  isAdding: boolean;
  onToggleResult: (resultId: string) => void;
  onToggleAll: () => void;
  onAdd: (resultIds: string[]) => void;
};

function DiscoveryResults({
  job,
  selectedResultIds,
  allAddableSelected,
  isAdding,
  onToggleResult,
  onToggleAll,
  onAdd,
}: DiscoveryResultsProps) {
  const isActive = job.status === "pending" || job.status === "running";

  return (
    <div className="discovery-results">
      <div className="discovery-job-summary">
        <span className={`status-badge status-badge--${job.status}`}>{formatJobStatus(job)}</span>
        <span><strong>Scope:</strong> {job.scope.replaceAll("\n", ", ")}</span>
        <span><strong>Environment:</strong> {job.environment}</span>
      </div>
      {isActive ? <p className="discovery-progress">Güvenli discovery worker tarafından işleniyor…</p> : null}
      {job.status === "failed" ? <p className="feedback feedback--error">{job.errorMessage ?? "Discovery başarısız oldu."}</p> : null}

      <section className="discovery-result-section">
        <div className="discovery-result-heading">
          <div>
            <h3>Supported Databases</h3>
            <p className="muted">Target olarak eklenebilir database servisleri.</p>
          </div>
          <Button type="button" disabled={selectedResultIds.length === 0 || isAdding} onClick={() => onAdd(selectedResultIds)}>
            Add Selected as Targets ({selectedResultIds.length})
          </Button>
        </div>
        <div className="discovery-table discovery-table--supported">
          <div className="discovery-table__header">
            <span><input type="checkbox" aria-label="Tüm supported sonuçları seç" checked={allAddableSelected} onChange={onToggleAll} /></span>
            <span>Host</span><span>Port</span><span>Detected Engine</span><span>Confidence</span><span>Environment</span><span>Status</span><span>Action</span>
          </div>
          {job.supportedDatabases.map((result) => (
            <div className="discovery-table__row" key={result.id}>
              <span data-label="Select"><input type="checkbox" disabled={!result.canAddAsTarget} checked={selectedResultIds.includes(result.id)} onChange={() => onToggleResult(result.id)} /></span>
              <span data-label="Host">{result.host}</span>
              <span data-label="Port">{result.port}</span>
              <span data-label="Detected Engine"><strong>{result.detectedService}</strong></span>
              <span data-label="Confidence">{result.confidence}%</span>
              <span data-label="Environment">{result.environment}</span>
              <span data-label="Status"><span className={`status-badge status-badge--${result.status}`}>{formatResultStatus(result)}</span></span>
              <span data-label="Action">
                <Button type="button" disabled={!result.canAddAsTarget || isAdding} onClick={() => onAdd([result.id])}>
                  {result.targetId ? "Added" : "Add as Target"}
                </Button>
              </span>
            </div>
          ))}
          {job.supportedDatabases.length === 0 ? <p className="discovery-table__empty">Supported database bulunmadı.</p> : null}
        </div>
      </section>

      <section className="discovery-result-section">
        <div>
          <h3>Unsupported / Not Supported Yet</h3>
          <p className="muted">Bilgi amaçlı gösterilir; target olarak eklenemez.</p>
        </div>
        <div className="discovery-table discovery-table--unsupported">
          <div className="discovery-table__header">
            <span>Host</span><span>Port</span><span>Detected Service</span><span>Reason</span>
          </div>
          {job.unsupportedDatabases.map((result) => (
            <div className="discovery-table__row" key={result.id}>
              <span data-label="Host">{result.host}</span>
              <span data-label="Port">{result.port}</span>
              <span data-label="Detected Service"><strong>{result.detectedService}</strong></span>
              <span data-label="Reason">{result.reason}</span>
            </div>
          ))}
          {job.unsupportedDatabases.length === 0 ? <p className="discovery-table__empty">Unsupported service bulunmadı.</p> : null}
        </div>
      </section>
    </div>
  );
}

function formatJobStatus(job: DiscoveryJobDto) {
  if (job.status === "running") return "Running";
  if (job.status === "completed") return "Completed";
  if (job.status === "failed") return "Failed";
  return "Pending";
}

function formatResultStatus(result: DiscoveryResultDto) {
  return result.status === "addedAsTarget" ? "Added as Target" : "Supported";
}
