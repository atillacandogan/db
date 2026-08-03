"use client";

import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { EmptyState } from "@/components/feedback/empty-state";
import { ScanStatusBadge } from "@/components/feedback/scan-status-badge";
import { getNseScripts } from "@/lib/api/nse-scripts";
import { getRules } from "@/lib/api/rules";
import { getTargets } from "@/lib/api/targets";
import { resolveRiskLevel, type RiskLevel } from "@/lib/utils/risk";
import type { RuleDto } from "@/schemas/rule.schema";
import type { NseScriptDto } from "@/schemas/nse-script.schema";
import type { TargetDto } from "@/schemas/target.schema";

export default function DashboardPage() {
  const dashboardQuery = useQuery({
    queryKey: ["dashboard"],
    retry: 1,
    queryFn: async () => {
      const [targets, rules, scripts] = await Promise.all([getTargets(), getRules(), getNseScripts()]);
      return { targets, rules, scripts };
    },
  });

  if (dashboardQuery.isPending) {
    return <section className="placeholder">Dashboard yükleniyor...</section>;
  }

  if (dashboardQuery.isError) {
    return (
      <section className="placeholder dashboard-error-state" role="alert">
        <strong>Dashboard yüklenemedi.</strong>
        <p>{dashboardQuery.error.message}</p>
        <button className="button" type="button" onClick={() => dashboardQuery.refetch()}>
          Tekrar Dene
        </button>
      </section>
    );
  }

  const { targets, rules, scripts } = dashboardQuery.data;
  const scannedTargets = targets.filter((target) => target.lastScan?.status === "scanned");
  const activeTargets = targets.filter((target) => target.enabled);
  const latestScans = targets
    .filter((target) => target.lastScan)
    .sort((left, right) => new Date(right.lastScan!.createdAt).getTime() - new Date(left.lastScan!.createdAt).getTime())
    .slice(0, 5);
  const latestScanTarget = latestScans[0];

  return (
    <section className="page-stack page-stack--wide dashboard-page">
      <header className="rules-hero dashboard-hero">
        <div>
          <p className="eyebrow">Overview</p>
          <h1>Db Scanner</h1>
        </div>
        <p className="muted rules-hero__summary">
          Demo akisinin durumu, son taramalar ve katalog kapsami tek ekranda ozetlenir.
        </p>
      </header>

      <section className="dashboard-stats-grid" aria-label="Dashboard metrics">
        <StatsCard title="Targets" value={targets.length} description={`${activeTargets.length} aktif hedef`} />
        <StatsCard
          title="Completed Scans"
          value={scannedTargets.length}
          description="Son scan'i tamamlanan hedefler"
        />
        <StatsCard title="Rules" value={rules.length} description="Rule catalog kaydi" />
        <StatsCard title="NSE Scripts" value={scripts.length} description="Yonetilen script tanimi" />
      </section>

      <section className="dashboard-main-grid">
        <article className="dashboard-panel dashboard-recent-panel">
          <div className="dashboard-panel__heading">
            <div>
              <p className="detail-label">Recent Activity</p>
              <h2>Recent scans</h2>
              <p className="muted">Hedeflerdeki son scan durumlari ve risk sonuclari.</p>
            </div>
            <Link className="link-inline dashboard-panel__heading-link" href="/scans">
              Tum scan&apos;leri gor
            </Link>
          </div>

          {latestScans.length > 0 ? (
            <div className="dashboard-scan-list">
              {latestScans.map((target) => (
                <RecentScanCard key={target.id} target={target} />
              ))}
            </div>
          ) : (
            <div className="dashboard-empty-state">
              <EmptyState
                title="Henuz scan yok."
                description="Targets ekranindan bir hedef secip tarama baslatin."
              />
            </div>
          )}
        </article>

        <aside className="dashboard-sidebar" aria-label="Dashboard summaries">
          <LatestRiskCard target={latestScanTarget} />

          <article className="dashboard-panel dashboard-sidebar-card">
            <div>
              <p className="detail-label">Quick Summary</p>
              <h2>Operational overview</h2>
            </div>
            <dl className="dashboard-summary-list">
              <SummaryRow label="Aktif target" value={activeTargets.length} />
              <SummaryRow label="Tamamlanan son scan" value={scannedTargets.length} />
              <SummaryRow label="Toplam rule" value={rules.length} />
              <SummaryRow label="Toplam NSE script" value={scripts.length} />
            </dl>
          </article>

          <article className="dashboard-panel dashboard-sidebar-card">
            <div>
              <p className="detail-label">Quick Actions</p>
              <h2>Workspace shortcuts</h2>
            </div>
            <nav className="dashboard-quick-actions" aria-label="Dashboard quick actions">
              <Link href="/targets">
                <span>Targets</span>
                <span aria-hidden="true">→</span>
              </Link>
              <Link href="/scans">
                <span>Scans</span>
                <span aria-hidden="true">→</span>
              </Link>
              <Link href="/rules">
                <span>Rule Catalog</span>
                <span aria-hidden="true">→</span>
              </Link>
            </nav>
          </article>
        </aside>
      </section>

      <section className="dashboard-coverage-section" aria-labelledby="coverage-heading">
        <div className="dashboard-section-heading">
          <div>
            <p className="eyebrow">Overview</p>
            <h2 id="coverage-heading">Coverage</h2>
          </div>
          <p className="muted">Rule ve NSE script kataloglarinin guncel kapsami.</p>
        </div>

        <div className="dashboard-coverage-grid">
          <CoverageCard
            title="Rule Coverage"
            description="Katalogdaki ilk rule eslesmeleri."
            href="/rules"
            linkLabel="Rule Catalog'a git"
            items={rules.slice(0, 4).map((rule) => ({
              id: rule.id,
              title: rule.title,
              badge: formatEngine(rule.engine),
              description: formatRuleScripts(rule),
            }))}
            emptyTitle="Rule kaydi yok."
          />
          <CoverageCard
            title="Script Readiness"
            description="Katalogdaki ilk NSE script tanimlari."
            href="/rules"
            linkLabel="NSE Scripts'e git"
            items={scripts.slice(0, 4).map((script) => ({
              id: script.id,
              title: script.displayName,
              badge: formatApprovalStatus(script),
              description: script.supportedEngines.map(formatEngine).join(", ") || "Engine tanimi yok",
            }))}
            emptyTitle="NSE script kaydi yok."
          />
        </div>
      </section>
    </section>
  );
}

type StatsCardProps = {
  title: string;
  value: number;
  description: string;
};

function StatsCard({ title, value, description }: StatsCardProps) {
  return (
    <article className="dashboard-stat-card">
      <p className="detail-label">{title}</p>
      <strong className="dashboard-stat-card__value">{value}</strong>
      <p className="muted">{description}</p>
    </article>
  );
}

function RecentScanCard({ target }: { target: TargetDto }) {
  const scan = target.lastScan!;
  const riskLevel = resolveRiskLevel(scan.riskScore, scan.overallSeverity);

  return (
    <article className="dashboard-scan-card">
      <div className="dashboard-scan-card__header">
        <div className="dashboard-scan-card__identity">
          <strong title={target.name}>{target.name}</strong>
          <p className="muted">
            <span>{target.host}:{target.port}</span>
            <span className="dashboard-engine-pill">{formatEngine(target.engine)}</span>
          </p>
        </div>
        <ScanStatusBadge status={scan.status} />
      </div>

      <div className="dashboard-scan-card__meta">
        <div>
          <span>Started</span>
          <time dateTime={scan.createdAt}>{formatDate(scan.createdAt)}</time>
        </div>
        <div>
          <span>Risk summary</span>
          {riskLevel && typeof scan.riskScore === "number" ? (
            <span className={`risk-badge risk-badge--${riskLevel}`}>
              {scan.riskScore} / {formatRiskLevel(riskLevel)}
            </span>
          ) : (
            <span className="muted">N/A</span>
          )}
        </div>
      </div>

      <div className="dashboard-scan-card__footer">
        <span className="muted dashboard-scan-card__id" title={scan.scanId}>
          Scan {scan.scanId}
        </span>
        <Link className="link-inline" href={`/scans/${scan.scanId}`}>
          Scan detayina git
        </Link>
      </div>
    </article>
  );
}

function LatestRiskCard({ target }: { target?: TargetDto }) {
  const scan = target?.lastScan;
  const riskLevel = resolveRiskLevel(scan?.riskScore, scan?.overallSeverity);

  return (
    <article className="dashboard-panel dashboard-sidebar-card dashboard-risk-card">
      <div>
        <p className="detail-label">Latest Risk</p>
        <h2>Last scan summary</h2>
      </div>
      {target && scan ? (
        <>
          <div className="dashboard-risk-card__score">
            {riskLevel && typeof scan.riskScore === "number" ? (
              <>
                <strong>{scan.riskScore}</strong>
                <span className={`risk-badge risk-badge--${riskLevel}`}>{formatRiskLevel(riskLevel)}</span>
              </>
            ) : (
              <strong className="muted">N/A</strong>
            )}
          </div>
          <div className="dashboard-risk-card__target">
            <strong>{target.name}</strong>
            <span className="muted">{formatDate(scan.createdAt)}</span>
          </div>
        </>
      ) : (
        <p className="muted dashboard-sidebar-card__empty">Risk ozeti icin tamamlanmis bir scan bekleniyor.</p>
      )}
    </article>
  );
}

function SummaryRow({ label, value }: { label: string; value: number }) {
  return (
    <div>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}

type CoverageItem = {
  id: string;
  title: string;
  badge: string;
  description: string;
};

type CoverageCardProps = {
  title: string;
  description: string;
  href: string;
  linkLabel: string;
  items: CoverageItem[];
  emptyTitle: string;
};

function CoverageCard({ title, description, href, linkLabel, items, emptyTitle }: CoverageCardProps) {
  return (
    <article className="dashboard-panel dashboard-coverage-card">
      <div className="dashboard-panel__heading">
        <div>
          <p className="detail-label">{title}</p>
          <p className="muted">{description}</p>
        </div>
        <Link className="link-inline" href={href}>
          {linkLabel}
        </Link>
      </div>
      {items.length > 0 ? (
        <div className="dashboard-coverage-list">
          {items.map((item) => (
            <div key={item.id} className="dashboard-coverage-item">
              <div>
                <strong title={item.title}>{item.title}</strong>
                <p className="muted" title={item.description}>{item.description}</p>
              </div>
              <span className="dashboard-engine-pill">{item.badge}</span>
            </div>
          ))}
        </div>
      ) : (
        <p className="muted dashboard-sidebar-card__empty">{emptyTitle}</p>
      )}
    </article>
  );
}

function formatDate(value: string): string {
  const date = new Date(value);

  if (Number.isNaN(date.getTime())) {
    return "-";
  }

  return new Intl.DateTimeFormat("tr-TR", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(date);
}

function formatEngine(engine: string): string {
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

  return engine.charAt(0).toUpperCase() + engine.slice(1);
}

function formatRiskLevel(level: RiskLevel): string {
  return level.charAt(0).toUpperCase() + level.slice(1);
}

function formatRuleScripts(rule: RuleDto): string {
  return rule.nseScripts.length > 0 ? rule.nseScripts.join(", ") : "Script eslemesi yok";
}

function formatApprovalStatus(script: NseScriptDto): string {
  return script.approvalStatus.charAt(0).toUpperCase() + script.approvalStatus.slice(1);
}
