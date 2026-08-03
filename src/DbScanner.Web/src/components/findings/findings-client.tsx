"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ScanStatusBadge } from "@/components/feedback/scan-status-badge";
import { Button } from "@/components/ui/button";
import { getFindings } from "@/lib/api/findings";
import { queryKeys } from "@/lib/query/query-keys";
import { resolveRiskLevel, type RiskLevel } from "@/lib/utils/risk";
import { getSeverityLabel } from "@/lib/utils/severity";
import type { FindingListItemDto } from "@/schemas/finding.schema";

export function FindingsClient() {
  const [selectedFinding, setSelectedFinding] = useState<FindingListItemDto | null>(null);
  const findingsQuery = useQuery({
    queryKey: queryKeys.findings,
    queryFn: getFindings,
  });

  const findings = useMemo(
    () =>
      [...(findingsQuery.data ?? [])].sort(
        (left, right) => new Date(right.detectedAt).getTime() - new Date(left.detectedAt).getTime(),
      ),
    [findingsQuery.data],
  );

  useEffect(() => {
    if (!selectedFinding) {
      return;
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        setSelectedFinding(null);
      }
    }

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [selectedFinding]);

  return (
    <>
      <section className="placeholder admin-grid-panel">
        <div className="admin-grid-panel__heading">
          <div>
            <p className="detail-label">Latest Findings</p>
            <p className="muted">Son 50 bulgu; detayli kanit ve remediation modal icinde sunulur.</p>
          </div>
          <span className="targets-count">{findings.length} finding</span>
        </div>

        {findingsQuery.isLoading ? <div className="admin-grid-state muted">Loading findings...</div> : null}

        {findingsQuery.isError ? (
          <div className="admin-grid-state admin-grid-state--error" role="alert">
            <strong>Failed to load findings.</strong>
            <span>{getErrorMessage(findingsQuery.error)}</span>
            <Button className="button button--secondary" type="button" onClick={() => findingsQuery.refetch()}>
              Tekrar Dene
            </Button>
          </div>
        ) : null}

        {!findingsQuery.isLoading && !findingsQuery.isError && findings.length === 0 ? (
          <div className="admin-grid-state muted">No findings found.</div>
        ) : null}

        {!findingsQuery.isLoading && !findingsQuery.isError && findings.length > 0 ? (
          <div className="admin-table admin-table--findings">
            <div className="admin-table__header findings-table-grid" aria-hidden="true">
              <span>Severity</span>
              <span>Finding Title</span>
              <span>Target</span>
              <span>Engine</span>
              <span>Source Script</span>
              <span>Risk</span>
              <span>Status</span>
              <span>Actions</span>
            </div>

            {findings.map((finding) => {
              const riskLevel = resolveRiskLevel(finding.riskScore, finding.overallSeverity);
              const findingStatus = finding.status ?? "open";

              return (
                <article key={finding.id} className="admin-table__row findings-table-grid">
                  <div className="admin-table__cell" data-label="Severity">
                    <span className={`severity-badge severity-badge--${finding.severity}`}>
                      {getSeverityLabel(finding.severity)}
                    </span>
                  </div>

                  <div className="admin-table__cell admin-table__cell--primary" data-label="Finding Title">
                    <strong>{finding.title}</strong>
                    <span className="muted admin-table__secondary">{finding.ruleId}</span>
                  </div>

                  <div className="admin-table__cell" data-label="Target">
                    <strong>{finding.targetName}</strong>
                    <span className="muted admin-table__secondary">{finding.targetHost}</span>
                  </div>

                  <div className="admin-table__cell" data-label="Engine">
                    <span className="catalog-pill">{formatEngine(finding.targetEngine)}</span>
                  </div>

                  <div className="admin-table__cell" data-label="Source Script">
                    <span className="catalog-pill admin-table__script-pill">{finding.sourceScript || "-"}</span>
                  </div>

                  <div className="admin-table__cell" data-label="Risk">
                    {riskLevel && typeof finding.riskScore === "number" ? (
                      <span className={`risk-badge risk-badge--${riskLevel}`}>
                        {finding.riskScore} / {formatRiskLevel(riskLevel)}
                      </span>
                    ) : (
                      <span className="catalog-pill catalog-pill--muted">N/A</span>
                    )}
                  </div>

                  <div className="admin-table__cell" data-label="Status">
                    <span className={`status-badge status-badge--${findingStatus}`}>
                      {formatFindingStatus(findingStatus)}
                    </span>
                  </div>

                  <div className="admin-table__cell admin-table__actions" data-label="Actions">
                    <Button
                      className="button button--secondary button--compact"
                      type="button"
                      onClick={() => setSelectedFinding(finding)}
                    >
                      Detaylar
                    </Button>
                  </div>
                </article>
              );
            })}
          </div>
        ) : null}
      </section>

      {selectedFinding ? (
        <FindingDetailsModal finding={selectedFinding} onClose={() => setSelectedFinding(null)} />
      ) : null}
    </>
  );
}

function FindingDetailsModal({ finding, onClose }: { finding: FindingListItemDto; onClose: () => void }) {
  const riskLevel = resolveRiskLevel(finding.riskScore, finding.overallSeverity);
  const findingStatus = finding.status ?? "open";

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
        className="modal-panel modal-panel--details finding-details-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="finding-details-title"
      >
        <div className="modal-panel__header finding-details-modal__header">
          <div>
            <p className="detail-label">Finding Details</p>
            <h2 id="finding-details-title">{finding.title}</h2>
          </div>
          <Button className="button button--secondary button--compact" type="button" onClick={onClose}>
            Kapat
          </Button>
        </div>

        <dl className="script-details-grid finding-details-grid">
          <DetailItem label="Severity">
            <span className={`severity-badge severity-badge--${finding.severity}`}>
              {getSeverityLabel(finding.severity)}
            </span>
          </DetailItem>
          <DetailItem label="Finding Status">
            <span className={`status-badge status-badge--${findingStatus}`}>
              {formatFindingStatus(findingStatus)}
            </span>
          </DetailItem>
          <DetailItem label="Risk Score">
            {riskLevel && typeof finding.riskScore === "number" ? (
              <span className={`risk-badge risk-badge--${riskLevel}`}>
                {finding.riskScore} / {formatRiskLevel(riskLevel)}
              </span>
            ) : (
              <span>N/A</span>
            )}
          </DetailItem>
          <DetailItem label="Target">
            <strong>{finding.targetName}</strong>
            <span className="muted">{finding.targetHost}</span>
          </DetailItem>
          <DetailItem label="Engine">{formatEngine(finding.targetEngine)}</DetailItem>
          <DetailItem label="Source Script">{finding.sourceScript || "-"}</DetailItem>
          <DetailItem label="Rule">{finding.ruleId}</DetailItem>
          <DetailItem label="Score Contribution">+{finding.scoreContribution}</DetailItem>
          <DetailItem label="Detected At">{formatDate(finding.detectedAt)}</DetailItem>

          {finding.description ? (
            <DetailItem label="Description" wide>
              <p className="finding-details-modal__text">{finding.description}</p>
            </DetailItem>
          ) : null}

          <DetailItem label="Evidence" wide>
            <pre className="finding-details-modal__text finding-details-modal__evidence">{finding.evidence || "-"}</pre>
          </DetailItem>

          <DetailItem label="Remediation" wide>
            <p className="finding-details-modal__text">{finding.remediation || "-"}</p>
          </DetailItem>

          <DetailItem label="Scan Information" wide>
            <div className="finding-details-modal__scan">
              <div>
                <strong>{finding.scanId}</strong>
                <span className="muted">Scan status</span>
              </div>
              <ScanStatusBadge status={finding.scanStatus} />
              <Link className="button button--secondary button--compact" href={`/scans/${finding.scanId}`}>
                Scan Detayina Git
              </Link>
            </div>
          </DetailItem>
        </dl>
      </section>
    </div>
  );
}

function DetailItem({
  label,
  wide = false,
  children,
}: {
  label: string;
  wide?: boolean;
  children: React.ReactNode;
}) {
  return (
    <div className={`script-details-item${wide ? " script-details-item--wide" : ""}`}>
      <dt>{label}</dt>
      <dd>{children}</dd>
    </div>
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

function formatEngine(engine: string) {
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

  return engine || "-";
}

function formatRiskLevel(level: RiskLevel) {
  return level.charAt(0).toUpperCase() + level.slice(1);
}

function formatFindingStatus(status: "open" | "resolved") {
  return status === "resolved" ? "Resolved" : "Open";
}

function getErrorMessage(error: unknown) {
  return error instanceof Error ? error.message : "Finding listesi alinamadi.";
}
