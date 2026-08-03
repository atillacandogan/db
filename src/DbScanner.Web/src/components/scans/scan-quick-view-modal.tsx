"use client";

import Link from "next/link";
import { useEffect, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { ScanStatusBadge } from "@/components/feedback/scan-status-badge";
import { Button } from "@/components/ui/button";
import { getScan } from "@/lib/api/scans";
import { queryKeys } from "@/lib/query/query-keys";
import { resolveRiskLevel, type RiskLevel } from "@/lib/utils/risk";
import { getScriptResultStatus } from "@/lib/utils/script-result";
import { getSeverityLabel } from "@/lib/utils/severity";
import type { ScanFindingDto, ScanScriptResultDto } from "@/schemas/scan.schema";

type ScanQuickViewModalProps = {
  scanId: string;
  onClose: () => void;
};

export function ScanQuickViewModal({ scanId, onClose }: ScanQuickViewModalProps) {
  const scanQuery = useQuery({
    queryKey: queryKeys.scan(scanId),
    queryFn: () => getScan(scanId),
  });

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        onClose();
      }
    }

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [onClose]);

  const scan = scanQuery.data;
  const riskLevel = resolveRiskLevel(scan?.riskScore, scan?.overallSeverity);

  return (
    <div
      className="modal-backdrop"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          onClose();
        }
      }}
    >
      <section
        className="modal-panel modal-panel--scan-quick-view"
        role="dialog"
        aria-modal="true"
        aria-labelledby="scan-quick-view-title"
      >
        <div className="modal-panel__header scan-quick-view__header">
          <div>
            <p className="detail-label">Quick Scan Summary</p>
            <h2 id="scan-quick-view-title">{scan?.target?.name ?? "Scan Details"}</h2>
            <p className="muted scan-quick-view__id">{scanId}</p>
          </div>
          <Button className="button button--secondary button--compact" type="button" onClick={onClose}>
            Kapat
          </Button>
        </div>

        {scanQuery.isLoading ? (
          <div className="scan-quick-view__state muted">Loading scan details...</div>
        ) : null}

        {scanQuery.isError ? (
          <div className="scan-quick-view__state scan-quick-view__state--error" role="alert">
            <strong>Failed to load scan details.</strong>
            <span>{getErrorMessage(scanQuery.error)}</span>
            <Button className="button button--secondary" type="button" onClick={() => scanQuery.refetch()}>
              Tekrar Dene
            </Button>
          </div>
        ) : null}

        {scan ? (
          <div className="scan-quick-view__content">
            <section className="scan-quick-summary-grid" aria-label="Scan summary">
              <SummaryItem label="Target Name" value={scan.target?.name ?? scan.targetId} />
              <SummaryItem label="Host" value={scan.target?.host ?? "-"} />
              <SummaryItem label="Port" value={scan.target ? String(scan.target.port) : "-"} />
              <SummaryItem label="Engine" value={formatEngine(scan.target?.engine)} />
              <SummaryItem label="Status" value={<ScanStatusBadge status={scan.status} />} />
              <SummaryItem label="Created" value={formatDate(scan.createdAt)} />
              <SummaryItem label="Started" value={formatDate(scan.startedAt)} />
              <SummaryItem label="Finished" value={formatDate(scan.finishedAt)} />
              <SummaryItem label="Duration" value={formatDuration(scan.startedAt, scan.finishedAt)} />
              <SummaryItem
                label="Risk Score"
                value={typeof scan.riskScore === "number" ? String(scan.riskScore) : "N/A"}
              />
              <SummaryItem
                label="Risk Level"
                value={riskLevel ? (
                  <span className={`risk-badge risk-badge--${riskLevel}`}>{formatRiskLevel(riskLevel)}</span>
                ) : "N/A"}
              />
              <SummaryItem label="Finding Count" value={String(scan.findings.length)} />
              <SummaryItem label="Executed Script Count" value={String(scan.scriptResults.length)} />
            </section>

            {scan.errorMessage ? (
              <section className="scan-quick-view__notice" role="alert">
                <p className="detail-label">Scan Error</p>
                <p>{scan.errorMessage}</p>
              </section>
            ) : null}

            <section className="scan-quick-view__section">
              <div className="scan-quick-view__section-heading">
                <div>
                  <p className="detail-label">Findings Summary</p>
                  <p className="muted">Bu scan icin kaydedilen bulgular.</p>
                </div>
                <span className="targets-count">{scan.findings.length} finding</span>
              </div>

              {scan.findings.length > 0 ? (
                <div className="scan-quick-table scan-quick-findings-table">
                  <div className="scan-quick-table__header">
                    <span>Severity</span>
                    <span>Title</span>
                    <span>Source Script</span>
                    <span>Score</span>
                    <span>Recommendation</span>
                  </div>
                  {scan.findings.map((finding) => (
                    <FindingSummaryRow key={finding.id} finding={finding} />
                  ))}
                </div>
              ) : (
                <div className="scan-quick-view__empty muted">No findings found for this scan.</div>
              )}
            </section>

            <section className="scan-quick-view__section">
              <div className="scan-quick-view__section-heading">
                <div>
                  <p className="detail-label">Executed NSE Scripts</p>
                  <p className="muted">Script sonucu ve kisa cikti ozeti.</p>
                </div>
                <span className="targets-count">{scan.scriptResults.length} script</span>
              </div>

              {scan.scriptResults.length > 0 ? (
                <div className="scan-quick-table scan-quick-scripts-table">
                  <div className="scan-quick-table__header">
                    <span>Script Name</span>
                    <span>Exit Code</span>
                    <span>Status</span>
                    <span>Short Summary</span>
                  </div>
                  {scan.scriptResults.map((result) => (
                    <ScriptSummaryRow key={result.id} result={result} />
                  ))}
                </div>
              ) : (
                <div className="scan-quick-view__empty muted">No executed NSE scripts found.</div>
              )}
            </section>
          </div>
        ) : null}

        <div className="modal-actions scan-quick-view__actions">
          <Button className="button button--secondary" type="button" onClick={onClose}>
            Kapat
          </Button>
          <Link className="button" href={`/scans/${scanId}`}>
            Tam Rapora Git
          </Link>
        </div>
      </section>
    </div>
  );
}

function SummaryItem({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="scan-quick-summary-item">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function FindingSummaryRow({ finding }: { finding: ScanFindingDto }) {
  return (
    <article className="scan-quick-table__row">
      <div className="scan-quick-table__cell" data-label="Severity">
        <span className={`severity-badge severity-badge--${finding.severity}`}>
          {getSeverityLabel(finding.severity)}
        </span>
      </div>
      <div className="scan-quick-table__cell scan-quick-table__cell--primary" data-label="Title">
        <strong>{finding.title}</strong>
        <span className="muted">{finding.ruleId}</span>
      </div>
      <div className="scan-quick-table__cell" data-label="Source Script">
        <span className="catalog-pill">{finding.sourceScript || "-"}</span>
      </div>
      <div className="scan-quick-table__cell" data-label="Score">
        <span className="catalog-pill">+{finding.scoreContribution}</span>
      </div>
      <div className="scan-quick-table__cell scan-quick-table__summary" data-label="Recommendation">
        <p>{createPreview(finding.remediation, 180)}</p>
      </div>
    </article>
  );
}

function ScriptSummaryRow({ result }: { result: ScanScriptResultDto }) {
  const exitCode = result.exitCode ?? null;
  const status = getScriptResultStatus(result);
  const output = sanitizeOutput(result.normalizedOutput);

  return (
    <article className="scan-quick-table__row">
      <div className="scan-quick-table__cell scan-quick-table__cell--primary" data-label="Script Name">
        <strong>{result.scriptName}</strong>
        {result.nseScriptId ? <span className="muted">{result.nseScriptId}</span> : null}
      </div>
      <div className="scan-quick-table__cell" data-label="Exit Code">
        <span className="catalog-pill">{exitCode ?? "-"}</span>
      </div>
      <div className="scan-quick-table__cell" data-label="Status">
        <span className={`status-badge ${status.className}`}>{status.label}</span>
      </div>
      <div className="scan-quick-table__cell scan-quick-table__summary" data-label="Short Summary">
        <p>{createPreview(output, 200) || "No normalized output."}</p>
        {output.length > 200 ? (
          <details className="scan-quick-table__details">
            <summary>More</summary>
            <p>{output}</p>
          </details>
        ) : null}
      </div>
    </article>
  );
}

function formatDate(value?: string | null) {
  if (!value) {
    return "-";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "-";
  }

  return new Intl.DateTimeFormat("tr-TR", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(date);
}

function formatDuration(startedAt?: string | null, finishedAt?: string | null) {
  if (!startedAt || !finishedAt) {
    return "-";
  }

  const milliseconds = new Date(finishedAt).getTime() - new Date(startedAt).getTime();
  if (!Number.isFinite(milliseconds) || milliseconds < 0) {
    return "-";
  }

  const totalSeconds = Math.round(milliseconds / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;

  if (minutes === 0) {
    return `${seconds}s`;
  }

  return `${minutes}m ${seconds}s`;
}

function formatEngine(engine?: string) {
  if (!engine) {
    return "-";
  }

  if (engine.toLowerCase() === "postgresql") {
    return "PostgreSQL";
  }

  if (engine.toLowerCase() === "mysql") {
    return "MySQL";
  }

  if (engine.toLowerCase() === "mssql") {
    return "Microsoft SQL Server";
  }

  if (engine.toLowerCase() === "oracle") {
    return "Oracle Database";
  }

  return engine;
}

function formatRiskLevel(level: RiskLevel) {
  return level.charAt(0).toUpperCase() + level.slice(1);
}

function sanitizeOutput(output?: string | null) {
  return (output ?? "")
    .replace(/\\x[0-9a-fA-F]{2}/g, " ")
    .replace(/[\u0000-\u001f\u007f-\u009f]/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

function createPreview(value: string, maxLength: number) {
  const normalized = value.trim();
  return normalized.length > maxLength ? `${normalized.slice(0, maxLength - 3)}...` : normalized;
}

function getErrorMessage(error: unknown) {
  return error instanceof Error ? error.message : "Scan detayi alinamadi.";
}
