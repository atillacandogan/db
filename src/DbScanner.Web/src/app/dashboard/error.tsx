"use client";

import { Button } from "@/components/ui/button";

export default function DashboardError({ reset }: { error: Error; reset: () => void }) {
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
      <section className="dashboard-panel dashboard-error-state" role="alert">
        <strong>Dashboard verileri alinamadi.</strong>
        <p className="muted">API baglantisini kontrol edip tekrar deneyin.</p>
        <Button type="button" onClick={reset}>
          Tekrar Dene
        </Button>
      </section>
    </section>
  );
}
