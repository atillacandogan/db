"use client";

import { Button } from "@/components/ui/button";

export default function DashboardError({ reset }: { error: Error; reset: () => void }) {
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
      <section className="dashboard-panel dashboard-error-state" role="alert">
        <strong>Dashboard verileri yüklenemedi.</strong>
        <p className="muted">API bağlantınızı kontrol edip tekrar deneyin.</p>
        <Button type="button" onClick={reset}>
          Tekrar Dene
        </Button>
      </section>
    </section>
  );
}
