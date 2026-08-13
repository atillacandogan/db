"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useAuth } from "@/components/providers/auth-provider";
import { OperationToast } from "@/components/feedback/operation-toast";
import { Button } from "@/components/ui/button";
import { ExportMenu } from "@/components/ui/export-menu";
import {
  addDiscoveryTargets,
  createDiscovery,
  createLocalNetworkDiscovery,
  deleteDiscovery,
  getDiscoveries,
  getLocalDiscoveryNetworks,
  validateDiscoveryScope,
} from "@/lib/api/discoveries";
import { getTargets } from "@/lib/api/targets";
import { ApiError } from "@/lib/api/client";
import {
  exportDiscoveryAsCsv,
  exportDiscoveryAsJson,
  exportDiscoveryAsXlsx,
} from "@/lib/utils/report-exports";
import type {
  DiscoveryJobDto,
  DiscoveryEnvironment,
  DiscoveryTargetValidationDto,
  DiscoveryTargetValidationStatus,
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
  scopeMode: DiscoveryScopeMode | null;
  environment: DiscoveryEnvironment;
  engines: SupportedDiscoveryEngine[];
};

type DiscoveryScopeMode = "localNetwork" | "advanced";
type AdvancedTargetType = "ipv4" | "cidr" | "range" | "hostname";

type AdvancedTarget = {
  id: string;
  type: AdvancedTargetType;
  value: string;
  normalizedValue: string;
};

type BulkTargetError = {
  line: number;
  value: string;
  message: string;
};

const advancedTargetOptions: { value: AdvancedTargetType; label: string }[] = [
  { value: "ipv4", label: "Tek IPv4 adresi" },
  { value: "cidr", label: "IPv4 CIDR aralığı" },
  { value: "range", label: "IP aralığı" },
  { value: "hostname", label: "Hostname" },
];

const advancedTargetMeta: Record<AdvancedTargetType, { label: string; placeholder: string; example: string }> = {
  ipv4: { label: "IPv4 Adresi", placeholder: "192.168.1.25", example: "Örnek: 192.168.1.25" },
  cidr: { label: "IPv4 Ağ Aralığı", placeholder: "192.168.10.0/24", example: "Örnek: 192.168.10.0/24" },
  range: { label: "IP Aralığı", placeholder: "192.168.1.10 - 192.168.1.50", example: "Örnek: 192.168.1.10 - 192.168.1.50" },
  hostname: { label: "Hostname", placeholder: "db-server.company.local", example: "Örnek: db-server.company.local" },
};

const initialForm: DiscoveryForm = {
  name: "Database Discovery",
  scopeMode: "localNetwork",
  environment: "demo",
  engines: ["mysql", "postgresql", "mssql", "oracle"],
};

export function DiscoveryPanel() {
  const queryClient = useQueryClient();
  const { hasRole } = useAuth();
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [form, setForm] = useState<DiscoveryForm>(initialForm);
  const [selectedJobId, setSelectedJobId] = useState<string | null>(null);
  const [selectedResultIds, setSelectedResultIds] = useState<string[]>([]);
  const [feedback, setFeedback] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [targetValidations, setTargetValidations] = useState<DiscoveryTargetValidationDto[]>([]);
  const [selectedNetworks, setSelectedNetworks] = useState<string[]>([]);
  const [isValidating, setIsValidating] = useState(false);
  const [advancedTargetType, setAdvancedTargetType] = useState<AdvancedTargetType>("ipv4");
  const [advancedTargetValue, setAdvancedTargetValue] = useState("");
  const [advancedTargets, setAdvancedTargets] = useState<AdvancedTarget[]>([]);
  const [advancedInputError, setAdvancedInputError] = useState<string | null>(null);
  const [editingTargetId, setEditingTargetId] = useState<string | null>(null);
  const [isBulkTargetInputOpen, setIsBulkTargetInputOpen] = useState(false);
  const [bulkTargetValue, setBulkTargetValue] = useState("");
  const [bulkTargetErrors, setBulkTargetErrors] = useState<BulkTargetError[]>([]);
  const [networkSelectionInitialized, setNetworkSelectionInitialized] = useState(false);
  const [jobPendingDelete, setJobPendingDelete] = useState<DiscoveryJobDto | null>(null);
  const [deleteNotification, setDeleteNotification] = useState<{
    message: string;
    variant: "success" | "error";
  } | null>(null);

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
  const canDeleteDiscoveryJobs = hasRole("dbscanner-operator") || hasRole("dbscanner-admin");
  const localNetworksQuery = useQuery({
    queryKey: ["local-discovery-networks"],
    queryFn: getLocalDiscoveryNetworks,
    enabled: isModalOpen && form.scopeMode === "localNetwork",
  });

  useEffect(() => {
    if (form.scopeMode === "localNetwork" && localNetworksQuery.data && !networkSelectionInitialized) {
      setSelectedNetworks(localNetworksQuery.data.filter((network) => network.canScan).map((network) => network.cidr));
      setNetworkSelectionInitialized(true);
    }
  }, [form.scopeMode, localNetworksQuery.data, networkSelectionInitialized]);

  async function handleCreatedJob(job: DiscoveryJobDto, message: string) {
    setSelectedJobId(job.id);
    setSelectedResultIds([]);
    setIsModalOpen(false);
    setFeedback(message);
    setError(null);
    await queryClient.invalidateQueries({ queryKey: ["discoveries"] });
  }

  const createMutation = useMutation({
    mutationFn: createDiscovery,
    onSuccess: (job) => {
      const authorizedCount = targetValidations.filter((item) => item.status === "Authorized").length;
      const rejectedCount = targetValidations.length - authorizedCount;
      const message = rejectedCount > 0
        ? `${authorizedCount} hedef tarama kuyruğuna alındı, ${rejectedCount} geçersiz hedef atlandı.`
        : "Discovery job kuyruğa alındı.";
      return handleCreatedJob(job, message);
    },
    onError: (mutationError: Error) => {
      setError(mutationError.message);
      setFeedback(null);
    },
  });

  const localCreateMutation = useMutation({
    mutationFn: createLocalNetworkDiscovery,
    onSuccess: (job) => handleCreatedJob(job, "Yerel ağ discovery job'u kuyruğa alındı."),
    onError: (mutationError: Error) => {
      setError(mutationError.message);
      setFeedback(null);
    },
  });

  const deleteJobMutation = useMutation({
    mutationFn: (job: DiscoveryJobDto) => deleteDiscovery(job.id),
    onSuccess: async (_, deletedJob) => {
      const currentJobs = queryClient.getQueryData<DiscoveryJobDto[]>(["discoveries"]) ?? [];
      const deletedIndex = currentJobs.findIndex((job) => job.id === deletedJob.id);
      const remainingJobs = currentJobs.filter((job) => job.id !== deletedJob.id);
      const nextJob = remainingJobs[deletedIndex]
        ?? remainingJobs[Math.max(0, deletedIndex - 1)]
        ?? null;

      queryClient.setQueryData(["discoveries"], remainingJobs);
      setSelectedJobId(nextJob?.id ?? null);
      setSelectedResultIds([]);
      setJobPendingDelete(null);
      setError(null);
      setFeedback(null);
      setDeleteNotification({ message: "Discovery job silindi.", variant: "success" });
      await queryClient.invalidateQueries({ queryKey: ["discoveries"] });
    },
    onError: () => {
      setDeleteNotification({
        message: "Discovery job silinemedi. Lütfen tekrar deneyin.",
        variant: "error",
      });
    },
  });

  const addTargetsMutation = useMutation({
    mutationFn: ({ jobId, resultIds, isBulk }: { jobId: string; resultIds: string[]; isBulk: boolean }) =>
      addDiscoveryTargets(jobId, resultIds, isBulk),
    onSuccess: async (result, variables) => {
      const refreshedResults = new Map(result.results.map((item) => [item.id, item]));
      queryClient.setQueryData<DiscoveryJobDto[]>(["discoveries"], (current) => current?.map((job) =>
        job.id !== variables.jobId
          ? job
          : {
              ...job,
              supportedDatabases: job.supportedDatabases.map((item) => refreshedResults.get(item.id) ?? item),
              unsupportedDatabases: job.unsupportedDatabases.map((item) => refreshedResults.get(item.id) ?? item),
            }));
      await refreshDiscoveryAndTargets();
      setSelectedResultIds([]);
      setFeedback(
        `${result.addedCount} target eklendi, ${result.alreadyExistingCount} target zaten listede bulunuyor, ${result.failedCount} target eklenemedi.`,
      );
      const failureMessages = result.items
        .filter((item) => item.status === "failed")
        .map((item) => item.message)
        .filter(Boolean);
      setError(failureMessages.length > 0 ? failureMessages.join(" ") : null);
      window.requestAnimationFrame(() => {
        document.getElementById("registered-targets")?.scrollIntoView({ behavior: "smooth", block: "start" });
      });
    },
    onError: async (mutationError: Error) => {
      if (mutationError instanceof ApiError && mutationError.code === "TARGET_ALREADY_EXISTS") {
        setError("Bu servis zaten target listesinde bulunuyor.");
        await refreshDiscoveryAndTargets();
      } else {
        setError(mutationError.message);
      }
      setFeedback(null);
    },
  });

  async function refreshDiscoveryAndTargets() {
    const [refreshedDiscoveries, refreshedTargets] = await Promise.all([
      getDiscoveries(),
      getTargets(),
    ]);
    queryClient.setQueryData(["discoveries"], refreshedDiscoveries);
    queryClient.setQueryData(["targets"], refreshedTargets);
  }

  async function submitDiscovery(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!form.name.trim() || !form.environment.trim()) {
      setError("Discovery Name ve Environment zorunludur.");
      return;
    }

    if (!form.scopeMode) {
      setError("Bir discovery kapsamı seçin.");
      return;
    }

    const advancedScope = serializeAdvancedTargets(advancedTargets);
    if (form.scopeMode === "advanced" && advancedTargets.length === 0) {
      setError("Gelişmiş kapsam kullanılırken en az bir host, IP veya CIDR girilmelidir.");
      return;
    }

    if (form.engines.length === 0) {
      setError("En az bir supported engine seçilmelidir.");
      return;
    }

    if (form.scopeMode === "localNetwork") {
      if (selectedNetworks.length === 0) {
        setError("Taranabilir en az bir private IPv4 network seçilmelidir.");
        return;
      }

      setTargetValidations([]);
      setError(null);
      localCreateMutation.mutate({
        name: form.name.trim(),
        environment: form.environment,
        supportedEngineFilter: form.engines,
        networks: selectedNetworks,
      });
      return;
    }

    setError(null);
    if (form.scopeMode === "advanced") {
      if (targetValidations.length === 0) {
        setIsValidating(true);
        try {
          const validation = await validateDiscoveryScope({
            scope: advancedScope,
            environment: form.environment,
            supportedEngineFilter: form.engines,
          });
          setTargetValidations(validation.targets);
          if (!validation.targets.some((target) => target.status === "Authorized")) {
            setError("Geçerli bir discovery hedefi bulunamadı.");
          }
          return;
        } catch (validationError) {
          setError(validationError instanceof Error ? validationError.message : "Hedef doğrulaması başarısız oldu.");
          return;
        } finally {
          setIsValidating(false);
        }
      }
    } else {
      setTargetValidations([]);
    }

    const authorizedScope = targetValidations
      .filter((target) => target.status === "Authorized")
      .map((target) => target.target)
      .join("\n");
    if (!authorizedScope) {
      setError("Discovery başlatmak için en az bir geçerli hedef gerekli.");
      return;
    }

    createMutation.mutate({
      name: form.name.trim(),
      scope: authorizedScope,
      environment: form.environment,
      supportedEngineFilter: form.engines,
      useAuthorizedScope: false,
    });
  }

  function toggleEngine(engine: SupportedDiscoveryEngine) {
    setForm((current) => ({
      ...current,
      engines: current.engines.includes(engine)
        ? current.engines.filter((item) => item !== engine)
        : [...current.engines, engine],
    }));
    setTargetValidations([]);
    setError(null);
  }

  function changeScopeMode(scopeMode: DiscoveryScopeMode) {
    setForm((current) => ({ ...current, scopeMode }));
    setError(null);
    setAdvancedInputError(null);
    setTargetValidations([]);
  }

  function addAdvancedTargets() {
    const rawValue = advancedTargetValue.trim();
    if (!rawValue) {
      setAdvancedInputError("Bir hedef değeri girin.");
      return;
    }

    const validationMessage = validateAdvancedTarget(advancedTargetType, rawValue);
    if (validationMessage) {
      setAdvancedInputError(validationMessage);
      return;
    }

    const normalizedValue = normalizeAdvancedTarget(advancedTargetType, rawValue);
    const isDuplicate = advancedTargets.some((target) =>
      target.id !== editingTargetId && target.normalizedValue === normalizedValue);
    if (isDuplicate) {
      setAdvancedInputError("Bu hedef zaten listeye eklenmiş.");
      return;
    }

    if (editingTargetId) {
      setAdvancedTargets((current) => current.map((target) => target.id === editingTargetId
        ? { ...target, type: advancedTargetType, value: normalizedValue, normalizedValue }
        : target));
    } else {
      setAdvancedTargets((current) => [...current, {
        id: createTargetId(),
        type: advancedTargetType,
        value: normalizedValue,
        normalizedValue,
      }]);
    }
    setAdvancedTargetValue("");
    setEditingTargetId(null);
    setAdvancedInputError(null);
    setTargetValidations([]);
    setError(null);
  }

  function editAdvancedTarget(target: AdvancedTarget) {
    setAdvancedTargetType(target.type);
    setAdvancedTargetValue(target.value);
    setEditingTargetId(target.id);
    setAdvancedInputError(null);
    document.getElementById("advanced-target-value")?.focus();
  }

  function removeAdvancedTarget(targetId: string) {
    setAdvancedTargets((current) => current.filter((target) => target.id !== targetId));
    if (editingTargetId === targetId) {
      setEditingTargetId(null);
      setAdvancedTargetValue("");
      setAdvancedInputError(null);
    }
    setTargetValidations([]);
    setError(null);
  }

  function addBulkAdvancedTargets() {
    const existingValues = new Set(advancedTargets.map((target) => target.normalizedValue));
    const nextTargets: AdvancedTarget[] = [];
    const nextErrors: BulkTargetError[] = [];

    bulkTargetValue.split(/\r?\n/).forEach((lineValue, index) => {
      const rawValue = lineValue.trim();
      if (!rawValue) return;
      const type = inferAdvancedTargetType(rawValue);
      const validationMessage = validateAdvancedTarget(type, rawValue);
      if (validationMessage) {
        nextErrors.push({ line: index + 1, value: rawValue, message: validationMessage });
        return;
      }

      const normalizedValue = normalizeAdvancedTarget(type, rawValue);
      if (existingValues.has(normalizedValue)) {
        nextErrors.push({ line: index + 1, value: rawValue, message: "Bu hedef zaten listede." });
        return;
      }
      existingValues.add(normalizedValue);
      nextTargets.push({ id: createTargetId(), type, value: normalizedValue, normalizedValue });
    });

    setBulkTargetErrors(nextErrors);
    if (nextTargets.length > 0) {
      setAdvancedTargets((current) => [...current, ...nextTargets]);
      setTargetValidations([]);
      setError(null);
      if (nextErrors.length === 0) {
        setBulkTargetValue("");
        setIsBulkTargetInputOpen(false);
      } else {
        setBulkTargetValue(nextErrors.map((item) => item.value).join("\n"));
      }
    }
  }

  function addResults(resultIds: string[], isBulk: boolean) {
    if (!selectedJob || resultIds.length === 0) {
      return;
    }

    addTargetsMutation.mutate({ jobId: selectedJob.id, resultIds, isBulk });
  }

  const addableResults = selectedJob?.supportedDatabases.filter(
    (item) => item.canAddAsTarget && !item.isAlreadyTarget,
  ) ?? [];
  const validSelectedResultIds = selectedResultIds.filter((id) =>
    addableResults.some((result) => result.id === id));
  const allAddableSelected = addableResults.length > 0
    && addableResults.every((item) => validSelectedResultIds.includes(item.id));
  const isSubmitting = createMutation.isPending || localCreateMutation.isPending || isValidating;
  const hasValidationResults = targetValidations.length > 0;
  const authorizedTargetCount = targetValidations.filter((target) => target.status === "Authorized").length;
  const rejectedTargetCount = targetValidations.length - authorizedTargetCount;
  const canSubmit = Boolean(
    form.name.trim()
      && form.scopeMode
      && form.engines.length > 0
      && (form.scopeMode !== "localNetwork" || selectedNetworks.length > 0)
      && (form.scopeMode !== "advanced" || (
        advancedTargets.length > 0
        && (!hasValidationResults || authorizedTargetCount > 0)
      )),
  );
  const selectedEngineLabels = form.engines
    .map((engine) => engineOptions.find((option) => option.value === engine)?.label)
    .filter((label): label is string => Boolean(label));
  const scopeSummary = form.scopeMode === "localNetwork"
    ? `${selectedNetworks.length} network`
    : form.scopeMode === "advanced"
      ? hasValidationResults
        ? formatValidationSummary(authorizedTargetCount, rejectedTargetCount)
        : `${advancedTargets.length} hedef`
      : "Kapsam seçilmedi";
  const discoverySummary = `${scopeSummary} • ${selectedEngineLabels.join(" + ") || "Engine seçilmedi"} • ${form.environment}`;
  const submitDisabledReason = !form.name.trim()
    ? "Discovery başlatmak için bir ad girin."
    : !form.scopeMode
      ? "Discovery başlatmak için bir çalışma biçimi seçin."
      : form.scopeMode === "advanced" && advancedTargets.length === 0
        ? "Discovery başlatmak için en az bir hedef ekleyin."
        : form.scopeMode === "advanced" && hasValidationResults && authorizedTargetCount === 0
          ? "Discovery başlatmak için en az bir geçerli hedef gerekli."
        : form.scopeMode === "localNetwork" && selectedNetworks.length === 0
          ? "Discovery başlatmak için en az bir network seçin."
          : form.engines.length === 0
            ? "Discovery başlatmak için en az bir database engine seçin."
            : null;
  const advancedDraftValidation = advancedTargetValue.trim()
    ? validateAdvancedTarget(advancedTargetType, advancedTargetValue)
    : null;
  const displayedAdvancedInputError = advancedInputError ?? advancedDraftValidation;
  const canAddAdvancedTarget = Boolean(advancedTargetValue.trim() && !advancedDraftValidation);
  const isIpv6Warning = displayedAdvancedInputError === "IPv6 discovery şu anda desteklenmiyor.";

  return (
    <section className="discovery-panel placeholder">
      <div className="discovery-panel__heading">
        <div>
          <p className="detail-label">Database Discovery Agent</p>
          <h2>Discovery Results</h2>
          <p className="muted">
            Seçilen private yerel ağda veya belirli hedeflerde database servislerini sınırlı TCP/protokol kontrolleriyle keşfeder.
          </p>
        </div>
        <Button type="button" onClick={() => { setIsModalOpen(true); setError(null); setTargetValidations([]); }}>
          Discovery Başlat
        </Button>
      </div>

      {feedback ? <p className="feedback feedback--success">{feedback}</p> : null}
      {error && !isModalOpen ? <p className="feedback feedback--error">{error}</p> : null}

      {jobs.length > 0 ? (
        <div className="discovery-job-toolbar">
          <div className="discovery-job-left">
            <label className="discovery-job-label" htmlFor="discovery-job-select">
              Görüntülenecek discovery job
            </label>
            <div className="discovery-job-controls">
              <select
                id="discovery-job-select"
                className="input discovery-job-picker"
                value={selectedJob?.id ?? ""}
                onChange={(event) => { setSelectedJobId(event.target.value); setSelectedResultIds([]); }}
              >
                {jobs.map((job) => (
                  <option key={job.id} value={job.id}>
                    {job.name} · {formatJobStatus(job)}
                  </option>
                ))}
              </select>
              {canDeleteDiscoveryJobs && selectedJob ? (
                <Button
                  className="button button--danger discovery-job-delete"
                  type="button"
                  disabled={selectedJob.status === "pending" || selectedJob.status === "running"}
                  title={selectedJob.status === "pending" || selectedJob.status === "running"
                    ? "Aktif discovery job tamamlanmadan silinemez."
                    : undefined}
                  onClick={() => {
                    setJobPendingDelete(selectedJob);
                    setDeleteNotification(null);
                    setFeedback(null);
                    setError(null);
                  }}
                >
                  Sil
                </Button>
              ) : null}
            </div>
          </div>
          <div className="discovery-job-toolbar__meta" aria-label="Discovery job özeti">
            <strong>{jobs.length}</strong>
            <span>kayıtlı job</span>
          </div>
        </div>
      ) : null}

      {discoveriesQuery.isLoading ? <p className="muted">Discovery jobları yükleniyor…</p> : null}
      {discoveriesQuery.isError ? <p className="feedback feedback--error">Discovery sonuçları yüklenemedi.</p> : null}
      {selectedJob ? (
        <DiscoveryResults
          job={selectedJob}
          selectedResultIds={validSelectedResultIds}
          allAddableSelected={allAddableSelected}
          isAdding={addTargetsMutation.isPending}
          addingResultIds={addTargetsMutation.variables?.resultIds ?? []}
          isBulkAdding={addTargetsMutation.variables?.isBulk ?? false}
          onToggleResult={(resultId) => setSelectedResultIds((current) =>
            current.includes(resultId) ? current.filter((id) => id !== resultId) : [...current, resultId])}
          onToggleAll={() => setSelectedResultIds(allAddableSelected ? [] : addableResults.map((item) => item.id))}
          onAdd={addResults}
        />
      ) : (
        <div className="discovery-empty discovery-empty--prominent">
          <span className="discovery-empty__icon" aria-hidden="true">⌁</span>
          <strong>Henüz discovery job başlatılmadı</strong>
          <span>Bir tarama kapsamı seçerek ilk database discovery işlemini başlatabilirsiniz.</span>
        </div>
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
            <div className="modal-panel__header discovery-modal__header">
              <div>
                <p className="eyebrow">Database discovery</p>
                <h2 id="discovery-title">Database Discovery Başlat</h2>
                <p className="muted">Kapsamı, hedefleri ve kontrol edilecek database engine seçeneklerini belirleyin.</p>
              </div>
              <Button className="button discovery-modal__close" type="button" aria-label="Discovery penceresini kapat" onClick={() => setIsModalOpen(false)}>Kapat</Button>
            </div>
            <form className="form-grid discovery-form" onSubmit={submitDiscovery}>
              <section className="discovery-form-section discovery-form-section--basics" aria-labelledby="discovery-basics-title">
                <div className="discovery-step-heading">
                  <span className="discovery-step-heading__number">1</span>
                  <div><strong id="discovery-basics-title">Temel bilgiler</strong><small>Job adı ve environment</small></div>
                </div>
                <div className="discovery-form__basics">
                  <label className="field-group" htmlFor="discovery-name">
                    <span>Discovery adı</span>
                    <input id="discovery-name" className="input" required value={form.name} onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))} />
                  </label>
                  <label className="field-group" htmlFor="discovery-environment">
                    <span>Environment</span>
                    <select id="discovery-environment" className="input" value={form.environment} onChange={(event) => { setForm((current) => ({ ...current, environment: event.target.value as DiscoveryEnvironment })); setTargetValidations([]); setError(null); }}>
                      <option value="demo">demo</option>
                      <option value="test">test</option>
                      <option value="staging">staging</option>
                      <option value="prod">prod</option>
                    </select>
                  </label>
                </div>
              </section>

              <fieldset className="discovery-form-section discovery-scope-mode">
                <legend>
                  <span className="discovery-step-heading__number">2</span>
                  <span><strong>Nasıl keşfetmek istiyorsunuz?</strong><small>Yalnızca bir çalışma biçimi seçin</small></span>
                </legend>
                <label className={form.scopeMode === "localNetwork" ? "discovery-scope-option discovery-scope-option--active" : "discovery-scope-option"}>
                  <input
                    type="radio"
                    name="discovery-scope-mode"
                    checked={form.scopeMode === "localNetwork"}
                    onChange={() => changeScopeMode("localNetwork")}
                  />
                  <span className="discovery-scope-option__content">
                    <span className="discovery-scope-option__heading">
                      <strong>Yerel Ağda Database Bul</strong>
                      <span className="discovery-mode-badge">OTOMATİK</span>
                    </span>
                    <small>Bu bilgisayarın bağlı olduğu private IPv4 ağlarını otomatik algılar ve seçtiğiniz database servislerini arar.</small>
                  </span>
                </label>
                <label className={form.scopeMode === "advanced" ? "discovery-scope-option discovery-scope-option--active" : "discovery-scope-option"}>
                  <input
                    type="radio"
                    name="discovery-scope-mode"
                    checked={form.scopeMode === "advanced"}
                    onChange={() => changeScopeMode("advanced")}
                  />
                  <span className="discovery-scope-option__content">
                    <span className="discovery-scope-option__heading">
                      <strong>Belirli Hedef / Ağda Database Bul</strong>
                      <span className="discovery-mode-badge">MANUEL</span>
                    </span>
                    <small>Belirli bir IP, hostname, CIDR veya IP aralığı girerek yalnızca belirlediğiniz hedeflerde database servislerini arayın.</small>
                  </span>
                </label>
              </fieldset>

              {!form.scopeMode ? (
                <div className="discovery-scope-placeholder discovery-form__scope">
                  <strong>Önce bir discovery kapsamı seçin</strong>
                  <span>Seçiminize uygun hedef ayarları burada açılacak.</span>
                </div>
              ) : null}

              {form.scopeMode === "localNetwork" ? (
                <div className="local-network-list discovery-form__scope discovery-scope-detail">
                  <div className="discovery-scope-detail__heading">
                    <div>
                      <strong>Algılanan private network aralıkları</strong>
                      <p className="muted">Loopback ve link-local adresler tarama kapsamına alınmaz.</p>
                    </div>
                    <div className="discovery-scope-detail__actions">
                      <span>{selectedNetworks.length} seçili</span>
                      <Button
                        className="button button--secondary discovery-redetect"
                        type="button"
                        disabled={localNetworksQuery.isFetching}
                        aria-describedby="network-detection-status"
                        onClick={() => void localNetworksQuery.refetch()}
                      >
                        Yeniden Algıla
                      </Button>
                    </div>
                  </div>
                  {localNetworksQuery.isFetching ? <p id="network-detection-status" className="muted" role="status">Networkler algılanıyor...</p> : null}
                  {localNetworksQuery.isError ? <p className="feedback feedback--error">Network aralıkları algılanamadı.</p> : null}
                  {(localNetworksQuery.data ?? []).map((network) => (
                    <label className={network.canScan ? "local-network-option" : "local-network-option local-network-option--disabled"} key={network.cidr}>
                      <input
                        type="checkbox"
                        disabled={!network.canScan}
                        checked={selectedNetworks.includes(network.cidr)}
                        onChange={() => setSelectedNetworks((current) => current.includes(network.cidr)
                          ? current.filter((cidr) => cidr !== network.cidr)
                          : [...current, network.cidr])}
                      />
                      <span>
                        <strong>{network.cidr}</strong>
                        <small>{network.interfaceName} · Bu makine: {network.address} · {network.hostCount.toLocaleString("tr-TR")} taranabilir adres</small>
                        {network.canScan && network.hostCount >= 256 ? (
                          <small className="local-network-option__warning">
                            Bu ağ {network.hostCount.toLocaleString("tr-TR")} taranabilir adres içeriyor. Discovery işlemi uzun sürebilir.
                          </small>
                        ) : null}
                        {!network.canScan && network.reason ? <small>{network.reason}</small> : null}
                      </span>
                    </label>
                  ))}
                  {!localNetworksQuery.isLoading && !localNetworksQuery.isError && (localNetworksQuery.data?.length ?? 0) === 0 ? (
                    <div className="discovery-target-list__empty discovery-network-empty">
                      <strong>Private network algılanamadı</strong>
                      <span>Bu cihazda taranabilir bir private IPv4 ağı bulunamadı. Hedefleri manuel olarak ekleyebilirsiniz.</span>
                      <Button className="button button--secondary" type="button" onClick={() => changeScopeMode("advanced")}>Manuel hedef ekle</Button>
                    </div>
                  ) : null}
                  <p className="muted discovery-environment-helper">Seçili environment, job ve bulunan sonuçlarla birlikte kaydedilir.</p>
                </div>
              ) : null}

              {form.scopeMode === "advanced" ? (
                <section className="discovery-advanced discovery-form__scope" aria-labelledby="advanced-target-title">
                  <div className="discovery-scope-detail__heading">
                    <div><strong id="advanced-target-title">Belirli hedefleri ekleyin</strong><p className="muted">Discovery yalnızca eklediğiniz IP, CIDR, IP aralığı veya hostname hedeflerinde çalışır.</p></div>
                    <span>{advancedTargets.length} hedef</span>
                  </div>
                  <div className="discovery-advanced__builder">
                    <div className="field-group discovery-advanced__field">
                      <label htmlFor="advanced-target-type">Hedef tipi</label>
                      <select id="advanced-target-type" className="input" value={advancedTargetType} onChange={(event) => { setAdvancedTargetType(event.target.value as AdvancedTargetType); setAdvancedTargetValue(""); setEditingTargetId(null); setAdvancedInputError(null); }}>
                        {advancedTargetOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
                      </select>
                    </div>
                    <div className="field-group discovery-advanced__field discovery-advanced__value">
                      <label htmlFor="advanced-target-value">{advancedTargetMeta[advancedTargetType].label}</label>
                      <input
                        id="advanced-target-value"
                        className="input"
                        value={advancedTargetValue}
                        onChange={(event) => { setAdvancedTargetValue(event.target.value); setAdvancedInputError(null); }}
                        onKeyDown={(event) => { if (event.key === "Enter") { event.preventDefault(); addAdvancedTargets(); } }}
                        placeholder={advancedTargetMeta[advancedTargetType].placeholder}
                        aria-describedby={displayedAdvancedInputError ? "advanced-target-help advanced-target-error" : "advanced-target-help"}
                        aria-invalid={Boolean(displayedAdvancedInputError && !isIpv6Warning)}
                      />
                      <small id="advanced-target-help">{advancedTargetMeta[advancedTargetType].example}</small>
                      {displayedAdvancedInputError ? (
                        <small id="advanced-target-error" className={isIpv6Warning ? "discovery-inline-warning" : "field-error"} role="alert">
                          {displayedAdvancedInputError}
                        </small>
                      ) : null}
                    </div>
                    <Button className="button discovery-advanced__add" type="button" disabled={!canAddAdvancedTarget} onClick={addAdvancedTargets}>
                      {editingTargetId ? "Hedefi Güncelle" : "Listeye Ekle"}
                    </Button>
                  </div>
                  <div className="discovery-bulk-targets">
                    <Button
                      className="button button--secondary discovery-bulk-targets__toggle"
                      type="button"
                      aria-expanded={isBulkTargetInputOpen}
                      aria-controls="bulk-target-panel"
                      onClick={() => { setIsBulkTargetInputOpen((current) => !current); setBulkTargetErrors([]); }}
                    >
                      Birden fazla hedef ekle
                    </Button>
                    {isBulkTargetInputOpen ? (
                      <div id="bulk-target-panel" className="discovery-bulk-targets__panel">
                        <label className="field-group" htmlFor="bulk-target-value">
                          <span>Toplu hedefler</span>
                          <textarea
                            id="bulk-target-value"
                            className="input discovery-bulk-targets__textarea"
                            value={bulkTargetValue}
                            onChange={(event) => { setBulkTargetValue(event.target.value); setBulkTargetErrors([]); }}
                            placeholder={"192.168.1.25\n192.168.10.0/24\n192.168.2.10 - 192.168.2.25\ndb-server.company.local"}
                            aria-describedby="bulk-target-help"
                          />
                          <small id="bulk-target-help">Her satıra bir hedef girin. Hedef tipi otomatik belirlenir.</small>
                        </label>
                        <Button type="button" disabled={!bulkTargetValue.trim()} onClick={addBulkAdvancedTargets}>Geçerli Hedefleri Ekle</Button>
                        {bulkTargetErrors.length > 0 ? (
                          <div className="discovery-bulk-targets__errors" role="alert">
                            <strong>{bulkTargetErrors.length} satır eklenemedi</strong>
                            {bulkTargetErrors.map((item) => <small key={`${item.line}-${item.value}`}>Satır {item.line}: {item.value} — {item.message}</small>)}
                          </div>
                        ) : null}
                      </div>
                    ) : null}
                  </div>
                  <div className="discovery-target-list" aria-live="polite">
                    {advancedTargets.map((target) => (
                      <div className="discovery-target-chip" key={target.id}>
                        <span className="discovery-target-chip__type">{advancedTargetTypeLabel(target.type)}</span>
                        <code>{target.value}</code>
                        <span className="discovery-target-chip__actions">
                          <button className="discovery-target-chip__edit" type="button" onClick={() => editAdvancedTarget(target)} aria-label={`${target.value} hedefini düzenle`}>Düzenle</button>
                          <button className="discovery-target-chip__remove" type="button" onClick={() => removeAdvancedTarget(target.id)} aria-label={`${target.value} hedefini sil`}>Sil</button>
                        </span>
                      </div>
                    ))}
                    {advancedTargets.length === 0 ? (
                      <div className="discovery-target-list__empty">
                        <strong>Henüz hedef eklenmedi.</strong>
                        <span>Yukarıdaki alana IP, CIDR, IP aralığı veya hostname girerek ilk discovery hedefinizi ekleyin.</span>
                      </div>
                    ) : null}
                  </div>
                  <p className="muted discovery-environment-helper">Seçili environment, job ve bulunan sonuçlarla birlikte kaydedilir.</p>
                </section>
              ) : null}

              {form.scopeMode === "advanced" && targetValidations.length > 0 ? (
                <div className="discovery-validation-list">
                  <div className="discovery-scope-detail__heading"><strong>Backend hedef doğrulaması</strong><span>{targetValidations.length} sonuç</span></div>
                  {targetValidations.map((validation, index) => (
                    <div className={validation.status === "Authorized" ? "discovery-validation discovery-validation--authorized" : "discovery-validation discovery-validation--rejected"} key={`${validation.target}-${index}`}>
                      <span className="discovery-validation__icon" aria-hidden="true">{validation.status === "Authorized" ? "✓" : "×"}</span>
                      <span className="discovery-validation__content">
                        <strong>{validation.target}</strong>
                        <small>{getValidationMessage(validation)}</small>
                      </span>
                      <span className="discovery-validation__status">{getValidationStatusLabel(validation.status)}</span>
                    </div>
                  ))}
                  <div className={authorizedTargetCount > 0 ? "discovery-validation-summary" : "discovery-validation-summary discovery-validation-summary--rejected"}>
                    <strong>{formatValidationSummary(authorizedTargetCount, rejectedTargetCount)}</strong>
                    {rejectedTargetCount > 0 && authorizedTargetCount > 0 ? <span>Geçersiz hedefler tarama kapsamına eklenmeyecek.</span> : null}
                    {authorizedTargetCount === 0 ? <span>Geçerli bir discovery hedefi bulunamadı.</span> : null}
                  </div>
                </div>
              ) : null}

              <fieldset className="discovery-form-section discovery-engine-filter">
                <legend>
                  <span className="discovery-step-heading__number">3</span>
                  <span><strong>Database engine seçenekleri</strong><small>Yalnızca seçilen engine portları kontrol edilir</small></span>
                </legend>
                {engineOptions.map((option) => (
                  <label className={form.engines.includes(option.value) ? "discovery-engine-option discovery-engine-option--active" : "discovery-engine-option"} key={option.value}>
                    <input type="checkbox" checked={form.engines.includes(option.value)} onChange={() => toggleEngine(option.value)} />
                    <span>{option.label}</span>
                  </label>
                ))}
              </fieldset>
              <div className="discovery-security-note">
                <span className="discovery-security-note__icon" aria-hidden="true">i</span>
                <p><strong>Sınırlandırılmış bağlantı kontrolü</strong> Kimlik bilgisi, parola denemesi, brute force, exploit veya veri sorgusu kullanılmaz. Keşif yalnızca kullanıcı tarafından seçilen hedeflerde veya private yerel ağda, tanımlı database portlarıyla gerçekleştirilir.</p>
              </div>
              {error ? <p className="feedback feedback--error discovery-form__feedback">{error}</p> : null}
              <div className="discovery-form__summary" aria-live="polite">
                <span>Discovery özeti</span>
                <strong>{discoverySummary}</strong>
              </div>
              <div className="form-actions discovery-form__actions">
                {submitDisabledReason && !isSubmitting ? <small className="discovery-submit-helper">{submitDisabledReason}</small> : null}
                <Button className="button button--secondary" type="button" onClick={() => setIsModalOpen(false)}>Vazgeç</Button>
                <Button type="submit" disabled={!canSubmit || isSubmitting}>
                  {isValidating
                    ? "Doğrulanıyor…"
                    : createMutation.isPending || localCreateMutation.isPending
                      ? "Kuyruğa alınıyor…"
                      : form.scopeMode === "advanced" && !hasValidationResults
                        ? "Hedefleri Doğrula"
                        : "Discovery Başlat"}
                </Button>
              </div>
            </form>
          </section>
        </div>
      ) : null}

      {jobPendingDelete ? (
        <div
          className="modal-backdrop"
          role="presentation"
          onMouseDown={(event) => {
            if (event.target === event.currentTarget && !deleteJobMutation.isPending) {
              setJobPendingDelete(null);
            }
          }}
        >
          <section
            className="modal-panel modal-panel--confirm"
            role="dialog"
            aria-modal="true"
            aria-labelledby="delete-discovery-job-title"
          >
            <div className="modal-panel__header">
              <div>
                <h2 id="delete-discovery-job-title">Discovery Job Silinsin mi?</h2>
                <p className="muted">
                  ‘{jobPendingDelete.name}’ job’ını silmek istediğinize emin misiniz?
                </p>
              </div>
            </div>

            <div className="confirm-summary">
              <strong>{jobPendingDelete.name}</strong>
              <span className="muted">Bu işlem geri alınamaz.</span>
            </div>

            <div className="modal-actions">
              <Button
                className="button button--secondary"
                type="button"
                disabled={deleteJobMutation.isPending}
                onClick={() => {
                  setJobPendingDelete(null);
                }}
              >
                İptal
              </Button>
              <Button
                className="button button--danger"
                type="button"
                disabled={deleteJobMutation.isPending}
                onClick={() => deleteJobMutation.mutate(jobPendingDelete)}
              >
                {deleteJobMutation.isPending ? (
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

      {deleteNotification ? (
        <OperationToast
          message={deleteNotification.message}
          variant={deleteNotification.variant}
          onClose={() => setDeleteNotification(null)}
        />
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
  addingResultIds: string[];
  isBulkAdding: boolean;
  onAdd: (resultIds: string[], isBulk: boolean) => void;
};

function DiscoveryResults({
  job,
  selectedResultIds,
  allAddableSelected,
  isAdding,
  addingResultIds,
  isBulkAdding,
  onToggleResult,
  onToggleAll,
  onAdd,
}: DiscoveryResultsProps) {
  const isActive = job.status === "pending" || job.status === "running";
  const source = getDiscoverySource(job);

  return (
    <div className="discovery-results">
      <div className="discovery-export-actions">
        <ExportMenu
          options={[
            { label: "Excel (.xlsx)", description: "Özet, supported ve unsupported sonuçlar", onSelect: () => exportDiscoveryAsXlsx(job) },
            { label: "CSV", description: "Tüm discovery sonuçları tek tabloda", onSelect: () => exportDiscoveryAsCsv(job) },
            { label: "JSON", description: "Discovery job ham veri çıktısı", onSelect: () => exportDiscoveryAsJson(job) },
          ]}
        />
      </div>
      <div className="discovery-job-summary">
        <div className="discovery-summary-item">
          <span>Durum</span>
          <strong><span className={`status-badge status-badge--${job.status}`}>{formatJobStatus(job)}</span></strong>
        </div>
        <div className="discovery-summary-item discovery-summary-item--scope">
          <span>Kapsam</span>
          <strong title={job.scope}>{job.scope.replaceAll("\n", ", ")}</strong>
        </div>
        <div className="discovery-summary-item">
          <span>Environment</span>
          <strong>{job.environment}</strong>
        </div>
        <div className="discovery-summary-item">
          <span>Engine</span>
          <strong>{job.supportedEngineFilter.join(", ")}</strong>
        </div>
        <div className="discovery-summary-item">
          <span>Sonuç</span>
          <strong>{job.supportedDatabases.length} desteklenen · {job.unsupportedDatabases.length} diğer</strong>
        </div>
      </div>
      {isActive ? <p className="discovery-progress">Güvenli discovery worker tarafından işleniyor…</p> : null}
      {job.status === "failed" ? <p className="feedback feedback--error">{job.errorMessage ?? "Discovery başarısız oldu."}</p> : null}
      <section className="discovery-result-section">
        <div className="discovery-result-heading">
          <div>
            <h3>Supported Databases</h3>
            <p className="muted">Target olarak eklenebilir database servisleri · {job.supportedDatabases.length} sonuç</p>
          </div>
          <div className="discovery-result-actions">
            <Button type="button" disabled={selectedResultIds.length === 0 || isAdding} onClick={() => onAdd(selectedResultIds, true)}>
              {isAdding && isBulkAdding ? "Ekleniyor…" : `Seçilenleri Target Olarak Ekle (${selectedResultIds.length})`}
            </Button>
          </div>
        </div>
        <div className="discovery-table discovery-table--supported">
          <div className="discovery-table__header">
            <span><input type="checkbox" aria-label="Tüm eklenebilir sonuçları seç" disabled={isAdding || job.supportedDatabases.every((result) => result.isAlreadyTarget || !result.canAddAsTarget)} checked={allAddableSelected} onChange={onToggleAll} /></span>
            <span>Host</span><span>Port</span><span>Engine</span><span>Güven</span><span>Environment</span><span>Kaynak</span><span>Durum</span><span>İşlemler</span>
          </div>
          {job.supportedDatabases.map((result) => (
            <div className="discovery-table__row" key={result.id}>
              <span data-label="Seç"><input type="checkbox" disabled={result.isAlreadyTarget || !result.canAddAsTarget || isAdding} checked={selectedResultIds.includes(result.id)} onChange={() => onToggleResult(result.id)} /></span>
              <span data-label="Host">{result.host}</span>
              <span data-label="Port">{result.port}</span>
              <span data-label="Engine"><strong>{result.detectedService}</strong></span>
              <span data-label="Güven">{result.confidence}% · {result.fingerprintLevel}</span>
              <span data-label="Environment">{result.environment}</span>
              <span data-label="Kaynak">{formatDiscoverySource(source)}</span>
              <span data-label="Durum" className="discovery-status-stack">
                <span className="status-badge status-badge--supported">{result.status === "alreadyDiscovered" ? "Already Discovered" : "Supported"}</span>
                {result.isAlreadyTarget ? <span className="status-badge status-badge--addedAsTarget">✓ Target olarak ekli</span> : null}
              </span>
              <span data-label="İşlemler" className="discovery-row-actions">
                {result.isAlreadyTarget ? (
                  result.existingTargetId ? (
                    <Link
                      className="button button--secondary discovery-target-link"
                      href={`/targets#target-${result.existingTargetId}`}
                    >
                      Targetı Gör
                    </Link>
                  ) : (
                    <span title="Target bilgisi alınamadı">
                      <Button
                        className="button button--secondary"
                        type="button"
                        disabled
                        aria-label="Target bilgisi alınamadı"
                      >
                        Targetı Gör
                      </Button>
                    </span>
                  )
                ) : (
                  <Button type="button" disabled={!result.canAddAsTarget || isAdding} onClick={() => onAdd([result.id], false)}>
                    {isAdding && addingResultIds.includes(result.id) ? "Ekleniyor…" : "Target Olarak Ekle"}
                  </Button>
                )}
              </span>
            </div>
          ))}
          {job.supportedDatabases.length === 0 ? <div className="discovery-table__empty"><strong>Supported database bulunmadı</strong><span>Seçili kapsam ve engine seçeneklerinde doğrulanmış bir servis yok.</span></div> : null}
        </div>
      </section>

      <section className="discovery-result-section">
        <div>
          <h3>Unsupported / Not Supported Yet</h3>
          <p className="muted">Bilgi amaçlı gösterilir; target olarak eklenemez.</p>
        </div>
        <div className="discovery-table discovery-table--unsupported">
          <div className="discovery-table__header">
            <span>Host</span><span>Port</span><span>Servis</span><span>Kaynak</span><span>Neden</span>
          </div>
          {job.unsupportedDatabases.map((result) => (
            <div className="discovery-table__row" key={result.id}>
              <span data-label="Host">{result.host}</span>
              <span data-label="Port">{result.port}</span>
              <span data-label="Servis"><strong>{result.detectedService}</strong></span>
              <span data-label="Kaynak">{formatDiscoverySource(source)}</span>
              <span data-label="Neden">{result.reason}</span>
            </div>
          ))}
          {job.unsupportedDatabases.length === 0 ? <div className="discovery-table__empty"><strong>Unsupported service bulunmadı</strong><span>Bilgi amaçlı gösterilecek ek bir servis yok.</span></div> : null}
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

function validateAdvancedTarget(type: AdvancedTargetType, value: string): string | null {
  const trimmedValue = value.trim();
  if (!trimmedValue) return "Hedef boş bırakılamaz.";
  if (trimmedValue.includes(":")) return "IPv6 discovery şu anda desteklenmiyor.";

  if (type === "ipv4") {
    return isValidIpv4(trimmedValue) ? null : "Geçerli bir IPv4 adresi girin. Örnek: 192.168.1.25";
  }

  if (type === "cidr") {
    const [address, prefix, ...rest] = trimmedValue.split("/");
    const prefixNumber = Number(prefix);
    if (rest.length > 0 || !isValidIpv4(address) || prefix === undefined || !Number.isInteger(prefixNumber) || prefixNumber < 0 || prefixNumber > 32) {
      return "Geçerli bir IPv4 CIDR aralığı girin. Örnek: 192.168.10.0/24";
    }
    return null;
  }

  if (type === "range") {
    const match = trimmedValue.match(/^(\d{1,3}(?:\.\d{1,3}){3})\s*-\s*(\d{1,3}(?:\.\d{1,3}){3})$/);
    if (!match || !isValidIpv4(match[1]) || !isValidIpv4(match[2])) {
      return "Geçerli bir IP aralığı girin. Örnek: 192.168.1.10 - 192.168.1.50";
    }
    const start = ipv4ToNumber(match[1]);
    const end = ipv4ToNumber(match[2]);
    if (start > end) return "Başlangıç IP adresi bitiş IP adresinden büyük olamaz.";
    if (end - start + 1 > 512) return "IP aralığı en fazla 512 adres içerebilir.";
    return null;
  }

  if (trimmedValue.length > 253 || trimmedValue.endsWith(".")) return "Geçerli bir hostname girin. Örnek: db-server.company.local";
  const labels = trimmedValue.split(".");
  const isValidHostname = labels.every((label) => /^[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?$/.test(label));
  if (!isValidHostname || isValidIpv4(trimmedValue)) return "Geçerli bir hostname girin. Örnek: db-server.company.local";
  return null;
}

function isValidIpv4(value: string) {
  const parts = value.split(".");
  return parts.length === 4 && parts.every((part) => /^\d{1,3}$/.test(part) && Number(part) >= 0 && Number(part) <= 255);
}

function normalizeAdvancedTarget(type: AdvancedTargetType, value: string) {
  const trimmedValue = value.trim();
  if (type === "hostname") return trimmedValue.toLocaleLowerCase("en-US");
  if (type === "ipv4") return normalizeIpv4(trimmedValue);
  if (type === "cidr") {
    const [address, prefix] = trimmedValue.split("/");
    return `${normalizeIpv4(address)}/${Number(prefix)}`;
  }
  const [start, end] = trimmedValue.split(/\s*-\s*/);
  return `${normalizeIpv4(start)} - ${normalizeIpv4(end)}`;
}

function advancedTargetTypeLabel(type: AdvancedTargetType) {
  if (type === "ipv4") return "IPv4";
  if (type === "cidr") return "CIDR";
  if (type === "range") return "IP ARALIĞI";
  return "HOST";
}

function inferAdvancedTargetType(value: string): AdvancedTargetType {
  if (/^\d{1,3}(?:\.\d{1,3}){3}\s*-\s*\d{1,3}(?:\.\d{1,3}){3}$/.test(value)) return "range";
  if (value.includes("/")) return "cidr";
  if (isValidIpv4(value)) return "ipv4";
  return "hostname";
}

function normalizeIpv4(value: string) {
  return value.split(".").map((part) => String(Number(part))).join(".");
}

function ipv4ToNumber(value: string) {
  return value.split(".").reduce((result, part) => result * 256 + Number(part), 0);
}

function numberToIpv4(value: number) {
  return [24, 16, 8, 0].map((shift) => Math.floor(value / (2 ** shift)) % 256).join(".");
}

function expandIpRange(value: string) {
  const [startValue, endValue] = value.split(/\s*-\s*/);
  const start = ipv4ToNumber(startValue);
  const end = ipv4ToNumber(endValue);
  return Array.from({ length: end - start + 1 }, (_, index) => numberToIpv4(start + index));
}

function serializeAdvancedTargets(targets: AdvancedTarget[]) {
  return [...new Set(targets.flatMap((target) =>
    target.type === "range" ? expandIpRange(target.normalizedValue) : [target.normalizedValue]))].join("\n");
}

function createTargetId() {
  return globalThis.crypto?.randomUUID?.() ?? `target-${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

function getValidationStatusLabel(status: DiscoveryTargetValidationStatus) {
  return status === "Authorized" ? "Taramaya hazır" : "Taranamayacak";
}

function getValidationMessage(validation: DiscoveryTargetValidationDto) {
  if (validation.status === "Authorized") {
    return validation.target.includes("/")
      ? `${validation.resolvedAddresses.length} taranabilir host`
      : "Backend güvenlik doğrulamasından geçti.";
  }

  if (validation.status === "InvalidFormat") return "Geçersiz IP, hostname veya CIDR.";
  if (validation.status === "NotInAllowlist") return "İzin verilen ağ kapsamı dışında.";
  if (validation.status === "InactiveAllowlist") return "Hedefin izin kaydı etkin değil.";
  if (validation.status === "EnvironmentMismatch") return "Seçili environment için yetkilendirilmemiş.";
  if (validation.status === "UnsupportedAddressType") {
    return validation.target.includes(":")
      ? "IPv6 şu anda desteklenmiyor."
      : "Yalnızca güvenli private IPv4 hedefleri taranabilir.";
  }
  if (validation.status === "CidrTooLarge") return "CIDR izin verilen güvenli host limitini aşıyor.";
  if (validation.status === "DnsResolutionFailed") return "Hostname IPv4 adresine çözümlenemedi.";
  if (validation.status === "ResolvedAddressNotAuthorized") {
    return "Hostname güvenli kapsam dışındaki bir IPv4 adresine çözümlendi.";
  }

  return "Hedef backend güvenlik doğrulamasından geçemedi.";
}

function formatValidationSummary(authorizedCount: number, rejectedCount: number) {
  if (authorizedCount === 0) return `${rejectedCount} hedef atlanacak`;
  if (rejectedCount === 0) return `${authorizedCount} hedef taramaya hazır`;
  return `${authorizedCount} taranacak • ${rejectedCount} atlanacak`;
}

type DiscoverySource = "localNetwork" | "legacyAutomatic" | "advanced";

function getDiscoverySource(job: DiscoveryJobDto): DiscoverySource {
  if (job.scope.startsWith("Local network:")) return "localNetwork";
  if (job.scope === "All authorized endpoints") return "legacyAutomatic";
  return "advanced";
}

function formatDiscoverySource(source: DiscoverySource) {
  if (source === "localNetwork") return "Yerel Ağ (LocalNetwork)";
  if (source === "legacyAutomatic") return "Eski Otomatik Kapsam";
  return "Belirli Hedef (Advanced)";
}
