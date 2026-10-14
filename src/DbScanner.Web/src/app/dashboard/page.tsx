"use client";

import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { EmptyState } from "@/components/feedback/empty-state";
import { ScanStatusBadge } from "@/components/feedback/scan-status-badge";
import { getScanChecks } from "@/lib/api/scan-checks";
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
      const [targets, rules, scripts] = await Promise.all([getTargets(), getRules(), getScanChecks()]);
      return { targets, rules, scripts };
    },
  });

  if (dashboardQuery.isPending) {
    return <section className="placeholder" lang="tr">Dashboard yükleniyor...</section>;
  }

  if (dashboardQuery.isError) {
    return (
      <section className="placeholder dashboard-error-state" role="alert" lang="tr">
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
    <section className="page-stack page-stack--wide dashboard-page" lang="tr">
      <header className="rules-hero dashboard-hero">
        <div>
          <p className="eyebrow">Genel bakış</p>
          <h1>Db Scanner</h1>
        </div>
        <p className="muted rules-hero__summary">
          Tarama etkinliğini, son sonuçları ve katalog kapsamını tek ekranda takip edin.
        </p>
      </header>

      <section className="dashboard-stats-grid" aria-label="Dashboard ölçümleri">
        <StatsCard title="Hedefler" value={targets.length} description={`${activeTargets.length} aktif hedef`} />
        <StatsCard
          title="Tamamlanan Taramalar"
          value={scannedTargets.length}
          description="Son taraması tamamlanan hedefler"
        />
        <StatsCard title="Kurallar" value={rules.length} description="Kural kataloğu kayıtları" />
        <StatsCard title="Tarama Kontrolleri" value={scripts.length} description="Yönetilen kontrol tanımları" />
      </section>

      <section className="dashboard-main-grid">
        <article className="dashboard-panel dashboard-recent-panel">
          <div className="dashboard-panel__heading">
            <div>
              <p className="detail-label">Son etkinlik</p>
              <h2>Son taramalar</h2>
              <p className="muted">Hedeflerinizin son tarama durumları ve risk sonuçları.</p>
            </div>
            <Link className="link-inline dashboard-panel__heading-link" href="/scans">
              Tüm Taramaları Gör
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
                title="Henüz tarama yok."
                description="Targets ekranından bir hedef seçip tarama başlatın."
              />
            </div>
          )}
        </article>

        <aside className="dashboard-sidebar" aria-label="Dashboard özetleri">
          <LatestRiskCard target={latestScanTarget} />

          <article className="dashboard-panel dashboard-sidebar-card">
            <div>
              <p className="detail-label">Hızlı özet</p>
              <h2>Operasyon özeti</h2>
            </div>
            <dl className="dashboard-summary-list">
              <SummaryRow label="Aktif hedefler" value={activeTargets.length} />
              <SummaryRow label="Tamamlanan son taramalar" value={scannedTargets.length} />
              <SummaryRow label="Toplam kural" value={rules.length} />
              <SummaryRow label="Toplam tarama kontrolü" value={scripts.length} />
            </dl>
          </article>

          <article className="dashboard-panel dashboard-sidebar-card">
            <div>
              <p className="detail-label">Hızlı işlemler</p>
              <h2>Çalışma alanı kısayolları</h2>
            </div>
            <nav className="dashboard-quick-actions" aria-label="Dashboard hızlı işlemleri">
              <Link href="/targets">
                <span>Hedefler</span>
                <span aria-hidden="true">→</span>
              </Link>
              <Link href="/scans">
                <span>Taramalar</span>
                <span aria-hidden="true">→</span>
              </Link>
              <Link href="/rules">
                <span>Kural Kataloğu</span>
                <span aria-hidden="true">→</span>
              </Link>
            </nav>
          </article>
        </aside>
      </section>

      <section className="dashboard-coverage-section" aria-labelledby="coverage-heading">
        <div className="dashboard-section-heading">
          <div>
            <p className="eyebrow">Genel bakış</p>
            <h2 id="coverage-heading">Kapsam</h2>
          </div>
          <p className="muted">Kural ve tarama kontrolü kataloglarının güncel kapsamı.</p>
        </div>

        <div className="dashboard-coverage-grid">
          <CoverageCard
            title="Kural Kapsamı"
            description="Kataloğunuzdaki kurallardan bir seçki."
            href="/rules"
            linkLabel="Kural Kataloğunu Gör"
            items={rules.slice(0, 4).map((rule) => ({
              id: rule.id,
              title: rule.title,
              badge: formatEngine(rule.engine),
              description: formatRuleScripts(rule),
            }))}
            emptyTitle="Katalogda kural kaydı yok."
          />
          <CoverageCard
            title="Kontrol Hazırlığı"
            description="Kataloğunuzdaki tarama kontrollerinden bir seçki."
            href="/rules"
            linkLabel="Tarama Kontrollerini Gör"
            items={scripts.slice(0, 4).map((script) => ({
              id: script.id,
              title: script.displayName,
              badge: formatApprovalStatus(script),
              description: script.supportedEngines.map(formatEngine).join(", ") || "Veritabanı motoru tanımlanmamış",
            }))}
            emptyTitle="Katalogda tarama kontrolü yok."
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
          <span>Başlangıç</span>
          <time dateTime={scan.createdAt}>{formatDate(scan.createdAt)}</time>
        </div>
        <div>
          <span>Risk özeti</span>
          {riskLevel && typeof scan.riskScore === "number" ? (
            <span className={`risk-badge risk-badge--${riskLevel}`}>
              {scan.riskScore} / {formatRiskLevel(riskLevel)}
            </span>
          ) : (
            <span className="muted">—</span>
          )}
        </div>
      </div>

      <div className="dashboard-scan-card__footer">
        <span className="muted dashboard-scan-card__id" title={scan.scanId}>
          Tarama {scan.scanId}
        </span>
        <Link className="link-inline" href={`/scans/${scan.scanId}`}>
          Tarama Detayını Gör
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
        <p className="detail-label">Son risk</p>
        <h2>Son tarama özeti</h2>
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
              <strong className="muted">—</strong>
            )}
          </div>
          <div className="dashboard-risk-card__target">
            <strong>{target.name}</strong>
            <span className="muted">{formatDate(scan.createdAt)}</span>
          </div>
        </>
      ) : (
        <p className="muted dashboard-sidebar-card__empty">Risk özeti için tamamlanmış bir tarama bekleniyor.</p>
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
  const labels: Record<RiskLevel, string> = {
    info: "Bilgi",
    low: "Düşük",
    medium: "Orta",
    high: "Yüksek",
    critical: "Kritik",
  };
  return labels[level];
}

function formatRuleScripts(rule: RuleDto): string {
  const scanChecks = rule.scanChecks;
  return scanChecks.length > 0 ? scanChecks.join(", ") : "Tarama kontrolü atanmamış";
}

function formatApprovalStatus(script: NseScriptDto): string {
  const labels: Record<NseScriptDto["approvalStatus"], string> = {
    draft: "Taslak",
    approved: "Onaylı",
    disabled: "Devre dışı",
  };
  return labels[script.approvalStatus];
}
