"use client";

import Link from "next/link";
import { useState, type ReactNode } from "react";
import { useParams, useSearchParams } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import { ScanStatusBadge } from "@/components/feedback/scan-status-badge";
import { OperationToast } from "@/components/feedback/operation-toast";
import { ExportMenu } from "@/components/ui/export-menu";
import { getScan, getScanForExport } from "@/lib/api/scans";
import { exportScanAsJson, exportScanAsPdf, exportScanAsXlsx } from "@/lib/utils/report-exports";
import { resolveRiskLevel, type RiskLevel } from "@/lib/utils/risk";
import { getScriptResultStatus } from "@/lib/utils/script-result";
import { getSeverityLabel } from "@/lib/utils/severity";
import type { ScanFindingDto, ScanScriptResultDto } from "@/schemas/scan.schema";

const severityFilters = ["info", "low", "medium", "high", "critical"] as const;

export default function ScanDetailPage() {
  const { id } = useParams<{ id: string }>();
  const searchParams = useSearchParams();
  const severity = searchParams.get("severity") ?? undefined;
  const [exportError, setExportError] = useState<string | null>(null);
  const scanQuery = useQuery({
    queryKey: ["scans", id],
    queryFn: () => getScan(id),
    enabled: Boolean(id),
  });

  if (scanQuery.isPending) {
    return <section className="placeholder">Scan raporu yükleniyor...</section>;
  }

  const scan = scanQuery.data ?? null;
  const loadError = scanQuery.isError ? getErrorMessage(scanQuery.error) : "";

  if (!scan) {
    return (
      <section className="page-stack page-stack--wide scan-report-page">
        <div>
          <p className="eyebrow">Scans</p>
          <h1>Scan Report</h1>
        </div>
        <section className="placeholder report-load-state" role="alert">
          <strong>Failed to load scan report.</strong>
          <p>{loadError}</p>
          <div className="report-load-state__actions">
            <a className="button" href={`/scans/${id}`}>Tekrar Dene</a>
            <Link className="button button--secondary" href="/scans">Scan History</Link>
          </div>
        </section>
      </section>
    );
  }

  const riskLevel = resolveRiskLevel(scan.riskScore, scan.overallSeverity);
  const selectedSeverity = severityFilters.includes(severity as (typeof severityFilters)[number]) ? severity : "all";
  const findings =
    selectedSeverity === "all"
      ? scan.findings
      : scan.findings.filter((finding) => finding.severity === selectedSeverity);
  const duration = formatDuration(scan.startedAt, scan.finishedAt);

  async function exportFile(format: "pdf" | "xlsx" | "json") {
    if (!scan) {
      return;
    }

    setExportError(null);
    try {
      const authorized = await getScanForExport(scan.id);
      if (format === "pdf") await exportScanAsPdf(authorized);
      else if (format === "xlsx") exportScanAsXlsx(authorized);
      else exportScanAsJson(authorized);
    } catch (error) {
      console.error("Scan PDF export failed.", error);
      setExportError("Rapor dışa aktarılamadı. Hedefin dışa aktarma yetkisini kontrol edin.");
    }
  }

  return (
    <>
      <section className="page-stack page-stack--wide scan-report-page">
      <div className="report-hero">
        <div>
          <p className="eyebrow">Scans</p>
          <h1>Scan Report</h1>
        </div>
        <div className="report-hero__meta">
          <ScanStatusBadge status={scan.status} />
          {riskLevel ? <span className={`risk-badge risk-badge--${riskLevel}`}>Overall Risk: {formatRiskLevel(riskLevel)}</span> : null}
          <span className="no-print">
            <ExportMenu
              options={[
                { label: "PDF", description: "Gerçek PDF dosyası olarak indir", onSelect: () => void exportFile("pdf") },
                { label: "Excel (.xlsx)", description: "Özet, findings ve script sonuçları", onSelect: () => void exportFile("xlsx") },
                { label: "JSON", description: "Scan raporunun ham veri çıktısı", onSelect: () => void exportFile("json") },
              ]}
            />
          </span>
        </div>
      </div>

      <section className="report-summary-grid">
        <SummaryItem label="Target" value={scan.target?.name ?? scan.targetId} detail={formatEngine(scan.target?.engine)} />
        <SummaryItem label="Host" value={scan.target?.host ?? "-"} detail={scan.target ? `Port ${scan.target.port}` : undefined} />
        <SummaryItem label="Status" value={<ScanStatusBadge status={scan.status} />} />
        <SummaryItem label="Risk Score" value={typeof scan.riskScore === "number" ? String(scan.riskScore) : "N/A"} detail={riskLevel ? formatRiskLevel(riskLevel) : undefined} />
        <SummaryItem label="Started" value={formatDate(scan.startedAt)} />
        <SummaryItem label="Finished" value={formatDate(scan.finishedAt)} detail={duration} />
        <SummaryItem label="Findings" value={String(scan.findings.length)} />
        <SummaryItem label="Executed Scripts" value={String(scan.scriptResults.length)} />
      </section>

      {scan.errorMessage ? (
        <section className="placeholder">
          <p className="detail-label">Error</p>
          <p>{scan.errorMessage}</p>
        </section>
      ) : null}

      <section className="placeholder report-section">
        <div className="section-heading">
          <div>
            <p className="detail-label">Executed Scan Checks</p>
            <p className="muted">{scan.scriptResults.length} scan check sonucu kaydedildi.</p>
          </div>
        </div>

        {scan.scriptResults.length > 0 ? (
          <div className="report-table script-result-table">
            <div className="report-table__header">
              <span>Scan Check</span>
              <span>Status</span>
              <span>Exit Code</span>
              <span>Summary</span>
              <span>Actions</span>
            </div>
            {scan.scriptResults.map((result) => (
              <ScriptResultRow key={result.id} result={result} />
            ))}
          </div>
        ) : (
          <p className="muted">Bu scan icin henuz scan check sonucu yok.</p>
        )}
      </section>

      <section className="placeholder report-section">
        <div className="section-heading">
          <div>
            <p className="detail-label">Findings</p>
            <p className="muted">
              {findings.length} / {scan.findings.length} finding gosteriliyor.
            </p>
          </div>
          <div className="severity-filter-list">
            <Link className={`severity-filter${selectedSeverity === "all" ? " severity-filter--active" : ""}`} href={`/scans/${scan.id}`}>
              All
            </Link>
            {severityFilters.map((filter) => (
              <Link
                key={filter}
                className={`severity-filter${selectedSeverity === filter ? " severity-filter--active" : ""}`}
                href={`/scans/${scan.id}?severity=${filter}`}
              >
                {getSeverityLabel(filter)}
              </Link>
            ))}
          </div>
        </div>

        {findings.length > 0 ? (
          <div className="report-table finding-report-table">
            <div className="report-table__header">
              <span>Severity</span>
              <span>Title</span>
              <span>Source Script</span>
              <span>Score</span>
              <span>Recommendation</span>
              <span>Actions</span>
            </div>
            {findings.map((finding) => (
              <FindingRow key={finding.id} finding={finding} />
            ))}
          </div>
        ) : (
          <p className="muted">Secili filtre icin finding yok.</p>
        )}
      </section>
      </section>

      {exportError ? (
        <OperationToast message={exportError} variant="error" onClose={() => setExportError(null)} />
      ) : null}
    </>
  );
}

function SummaryItem({ label, value, detail }: { label: string; value: ReactNode; detail?: string }) {
  return (
    <div className="report-summary-item">
      <p className="detail-label">{label}</p>
      <strong>{value}</strong>
      {detail ? <p className="muted">{detail}</p> : null}
    </div>
  );
}

function ScriptResultRow({ result }: { result: ScanScriptResultDto }) {
  const preview = createOutputPreview(result.normalizedOutput);
  const details = extractOutputDetails(result.normalizedOutput);
  const exitCode = result.exitCode ?? null;
  const status = getScriptResultStatus(result);

  return (
    <article className="report-table__row">
      <div className="report-table__cell report-table__cell--primary" data-label="Scan Check">
        <div className="report-table__main">
          <strong>{result.scriptName}</strong>
          {result.scanCheckId ? <p className="muted">{result.scanCheckId}</p> : null}
        </div>
      </div>

      <div className="report-table__cell" data-label="Status">
        <span className={`status-badge ${status.className}`}>{status.label}</span>
      </div>

      <div className="report-table__cell" data-label="Exit Code">
        <span className="catalog-pill">{exitCode ?? "-"}</span>
      </div>

      <div className="report-table__cell report-table__cell--summary" data-label="Summary">
        <p>{preview}</p>
      </div>

      <details className="report-table__details">
        <summary>Detaylar</summary>
        <div className="report-detail-panel">
          {details.length > 0 ? (
            <div className="report-subsection">
              <p className="detail-label">Parsed Details</p>
              <ul className="report-detail-list">
                {details.map((detail) => (
                  <li key={`${result.id}-${detail}`}>{detail}</li>
                ))}
              </ul>
            </div>
          ) : (
            <p className="muted">Parsed detail yok.</p>
          )}

          <details className="raw-output-panel">
            <summary>Show Raw Output</summary>
            <pre className="detail-pre">{escapeRawOutput(result.rawXml ?? result.normalizedOutput ?? "No raw output.")}</pre>
          </details>
        </div>
      </details>
    </article>
  );
}

function FindingRow({ finding }: { finding: ScanFindingDto }) {
  return (
    <article className="report-table__row">
      <div className="report-table__cell" data-label="Severity">
        <span className={`severity-badge severity-badge--${finding.severity}`}>{getSeverityLabel(finding.severity)}</span>
      </div>

      <div className="report-table__cell report-table__cell--primary" data-label="Title">
        <div className="report-table__main">
          <strong>{finding.title}</strong>
          <p className="muted">{finding.ruleId}</p>
        </div>
      </div>

      <div className="report-table__cell" data-label="Source Script">
        <span className="catalog-pill">{finding.sourceScript}</span>
      </div>

      <div className="report-table__cell" data-label="Score">
        <span className="catalog-pill">+{finding.scoreContribution}</span>
      </div>

      <div className="report-table__cell report-table__cell--summary" data-label="Recommendation">
        <p>{finding.remediation}</p>
      </div>

      <details className="report-table__details">
        <summary>Detaylar</summary>
        <div className="report-detail-panel">
          <div className="report-subsection">
            <p className="detail-label">Evidence</p>
            <p className="detail-pre">{escapeRawOutput(finding.evidence)}</p>
          </div>

          <div className="report-subsection">
            <p className="detail-label">Remediation</p>
            <p>{finding.remediation}</p>
          </div>
        </div>
      </details>
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
    timeStyle: "medium",
  }).format(date);
}

function formatDuration(startedAt?: string | null, finishedAt?: string | null) {
  if (!startedAt || !finishedAt) {
    return undefined;
  }

  const milliseconds = new Date(finishedAt).getTime() - new Date(startedAt).getTime();
  if (!Number.isFinite(milliseconds) || milliseconds < 0) {
    return undefined;
  }

  let seconds = Math.round(milliseconds / 1000);
  if (seconds < 60) {
    return `${seconds}s`;
  }

  const minutes = Math.floor(seconds / 60);
  seconds %= 60;
  return `${minutes}m ${seconds}s`;
}

function formatRiskLevel(level: RiskLevel) {
  return level.charAt(0).toUpperCase() + level.slice(1);
}

function formatEngine(engine?: string) {
  if (!engine) {
    return undefined;
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

function createOutputPreview(output?: string | null) {
  const sanitized = sanitizeOutput(output);
  if (!sanitized) {
    return "No normalized output.";
  }

  return sanitized.length > 220 ? `${sanitized.slice(0, 217)}...` : sanitized;
}

function extractOutputDetails(output?: string | null) {
  const sanitized = sanitizeOutput(output);
  if (!sanitized) {
    return [];
  }

  return sanitized
    .split(";")
    .map((item) => item.trim())
    .filter((item) => item.length > 0 && item.length <= 160)
    .slice(0, 8);
}

function sanitizeOutput(output?: string | null) {
  return (output ?? "")
    .replace(/\\x[0-9a-fA-F]{2}/g, " ")
    .replace(/[\u0000-\u001f\u007f-\u009f]/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

function escapeRawOutput(output: string) {
  return output.replace(/[\u0000-\u001f\u007f-\u009f]/g, (character) => {
    if (character === "\n" || character === "\r" || character === "\t") {
      return character;
    }

    return `\\x${character.charCodeAt(0).toString(16).padStart(2, "0")}`;
  });
}

function getErrorMessage(error: unknown) {
  return error instanceof Error ? error.message : "Scan raporu alinamadi.";
}
