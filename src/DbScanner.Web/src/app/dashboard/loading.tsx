export default function DashboardLoading() {
  return (
    <section className="page-stack page-stack--wide dashboard-page" lang="tr" aria-busy="true" aria-label="Dashboard yükleniyor">
      <header className="rules-hero dashboard-hero">
        <div>
          <p className="eyebrow">Genel bakış</p>
          <h1>Db Scanner</h1>
        </div>
        <span className="dashboard-skeleton dashboard-skeleton--hero" />
      </header>

      <section className="dashboard-stats-grid" aria-hidden="true">
        {Array.from({ length: 4 }, (_, index) => (
          <article key={index} className="dashboard-stat-card dashboard-skeleton-card">
            <span className="dashboard-skeleton dashboard-skeleton--label" />
            <span className="dashboard-skeleton dashboard-skeleton--value" />
            <span className="dashboard-skeleton dashboard-skeleton--text" />
          </article>
        ))}
      </section>

      <section className="dashboard-main-grid" aria-hidden="true">
        <article className="dashboard-panel dashboard-recent-panel">
          <span className="dashboard-skeleton dashboard-skeleton--heading" />
          <div className="dashboard-scan-list">
            {Array.from({ length: 3 }, (_, index) => (
              <div key={index} className="dashboard-scan-card dashboard-skeleton-card">
                <span className="dashboard-skeleton dashboard-skeleton--heading" />
                <span className="dashboard-skeleton dashboard-skeleton--text" />
                <span className="dashboard-skeleton dashboard-skeleton--text-short" />
              </div>
            ))}
          </div>
        </article>
        <aside className="dashboard-sidebar">
          {Array.from({ length: 3 }, (_, index) => (
            <article key={index} className="dashboard-panel dashboard-sidebar-card dashboard-skeleton-card">
              <span className="dashboard-skeleton dashboard-skeleton--heading" />
              <span className="dashboard-skeleton dashboard-skeleton--text" />
              <span className="dashboard-skeleton dashboard-skeleton--text-short" />
            </article>
          ))}
        </aside>
      </section>
    </section>
  );
}
