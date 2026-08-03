import { ScanHistoryClient } from "@/components/scans/scan-history-client";

export const dynamic = "force-dynamic";

export default function ScansPage() {
  return (
    <section className="page-stack page-stack--wide scans-page">
      <div className="rules-hero">
        <div>
          <p className="eyebrow">Scans</p>
          <h1>Scan History</h1>
        </div>
        <p className="muted rules-hero__summary">
          Tum scan gecmisi, durum akisi ve son risk sonucu bu ekranda listelenir.
        </p>
      </div>
      <ScanHistoryClient />
    </section>
  );
}
