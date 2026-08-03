"use client";

import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import { ScanStatusBadge } from "@/components/feedback/scan-status-badge";
import { ScanQuickViewModal } from "@/components/scans/scan-quick-view-modal";
import { cancelScan, deleteScan, getScans } from "@/lib/api/scans";
import { queryKeys } from "@/lib/query/query-keys";
import { resolveRiskLevel, type RiskLevel } from "@/lib/utils/risk";

export function ScanHistoryClient() {
  const queryClient = useQueryClient();
  const [feedback, setFeedback] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selectedScanId, setSelectedScanId] = useState<string | null>(null);

  const scansQuery = useQuery({
    queryKey: queryKeys.scans,
    queryFn: getScans,
  });

  const scans = useMemo(
    () =>
      [...(scansQuery.data ?? [])].sort(
        (left, right) => new Date(right.createdAt).getTime() - new Date(left.createdAt).getTime(),
      ),
    [scansQuery.data],
  );

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

  const cancelScanMutation = useMutation({
    mutationFn: cancelScan,
    onSuccess: async () => {
      setFeedback("Scan durduruldu.");
      setError(null);
      await queryClient.invalidateQueries({ queryKey: queryKeys.scans });
    },
    onError: (mutationError: Error) => {
      setError(mutationError.message);
      setFeedback(null);
    },
  });

  const deleteScanMutation = useMutation({
    mutationFn: deleteScan,
    onSuccess: async () => {
      setFeedback("Scan silindi.");
      setError(null);
      await queryClient.invalidateQueries({ queryKey: queryKeys.scans });
    },
    onError: (mutationError: Error) => {
      setError(mutationError.message);
      setFeedback(null);
    },
  });

  return (
    <>
      <section className="placeholder admin-grid-panel">
        <div className="admin-grid-panel__heading">
          <div>
            <p className="detail-label">Scan History</p>
            <p className="muted">Target, zamanlama, durum ve risk bilgileri.</p>
          </div>
          <span className="targets-count">{scans.length} scan</span>
        </div>

        {scansQuery.isLoading ? <div className="admin-grid-state muted">Loading scans...</div> : null}

        {scansQuery.isError ? (
          <div className="admin-grid-state admin-grid-state--error" role="alert">
            <strong>Failed to load scans.</strong>
            <span>{getErrorMessage(scansQuery.error)}</span>
            <Button className="button button--secondary" type="button" onClick={() => scansQuery.refetch()}>
              Tekrar Dene
            </Button>
          </div>
        ) : null}

        {!scansQuery.isLoading && !scansQuery.isError && scans.length === 0 ? (
          <div className="admin-grid-state muted">No scans found.</div>
        ) : null}

        {!scansQuery.isLoading && !scansQuery.isError && scans.length > 0 ? (
          <div className="admin-table admin-table--scans">
            <div className="admin-table__header scans-table-grid" aria-hidden="true">
              <span>Target</span>
              <span>Host / Port</span>
              <span>Engine</span>
              <span>Status</span>
              <span>Created / Started</span>
              <span>Finished</span>
              <span>Risk</span>
              <span>Actions</span>
            </div>

            {scans.map((scan) => {
              const isActive = scan.status === "pending" || scan.status === "scanning";
              const riskLevel = resolveRiskLevel(scan.riskScore, scan.overallSeverity);

              return (
                <article key={scan.id} className="admin-table__row scans-table-grid">
                  <div className="admin-table__cell admin-table__cell--primary" data-label="Target">
                    <strong>{scan.target?.name ?? scan.targetId}</strong>
                    <span className="muted admin-table__secondary">{scan.id}</span>
                  </div>

                  <div className="admin-table__cell" data-label="Host / Port">
                    <strong>{scan.target?.host ?? "-"}</strong>
                    <span className="muted admin-table__secondary">
                      {scan.target ? `Port ${scan.target.port}` : scan.targetId}
                    </span>
                  </div>

                  <div className="admin-table__cell" data-label="Engine">
                    <span className="catalog-pill">{formatEngine(scan.target?.engine)}</span>
                  </div>

                  <div className="admin-table__cell" data-label="Status">
                    <ScanStatusBadge status={scan.status} />
                    {scan.errorMessage ? (
                      <span className="admin-table__error" title={scan.errorMessage}>
                        {scan.errorMessage}
                      </span>
                    ) : null}
                  </div>

                  <div className="admin-table__cell admin-table__date" data-label="Created / Started">
                    <span><strong>Created</strong> {formatDate(scan.createdAt)}</span>
                    <span><strong>Started</strong> {formatDate(scan.startedAt)}</span>
                  </div>

                  <div className="admin-table__cell admin-table__date" data-label="Finished">
                    <span>{formatDate(scan.finishedAt)}</span>
                  </div>

                  <div className="admin-table__cell" data-label="Risk">
                    {riskLevel && typeof scan.riskScore === "number" ? (
                      <span className={`risk-badge risk-badge--${riskLevel}`}>
                        {scan.riskScore} / {formatRiskLevel(riskLevel)}
                      </span>
                    ) : (
                      <span className="catalog-pill catalog-pill--muted">N/A</span>
                    )}
                  </div>

                  <div className="admin-table__cell admin-table__actions" data-label="Actions">
                    <Button
                      className="button button--secondary button--compact"
                      type="button"
                      onClick={() => setSelectedScanId(scan.id)}
                    >
                      Detaylar
                    </Button>
                    {scan.status === "scanning" ? (
                      <Button
                        className="button button--secondary button--compact"
                        type="button"
                        disabled={cancelScanMutation.isPending || deleteScanMutation.isPending}
                        onClick={() => {
                          setFeedback(null);
                          setError(null);
                          cancelScanMutation.mutate(scan.id);
                        }}
                      >
                        Durdur
                      </Button>
                    ) : null}
                    <Button
                      className="button button--danger button--compact"
                      type="button"
                      disabled={isActive || cancelScanMutation.isPending || deleteScanMutation.isPending}
                      onClick={() => {
                        setFeedback(null);
                        setError(null);
                        deleteScanMutation.mutate(scan.id);
                      }}
                    >
                      Sil
                    </Button>
                  </div>
                </article>
              );
            })}
          </div>
        ) : null}
      </section>

      {selectedScanId ? (
        <ScanQuickViewModal scanId={selectedScanId} onClose={() => setSelectedScanId(null)} />
      ) : null}

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
            x
          </button>
        </div>
      ) : null}
    </>
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

function getErrorMessage(error: unknown) {
  return error instanceof Error ? error.message : "Scan listesi alinamadi.";
}
