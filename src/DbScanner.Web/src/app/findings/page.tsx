import { FindingsClient } from "@/components/findings/findings-client";

export const dynamic = "force-dynamic";

export default function FindingsPage() {
  return (
    <section className="page-stack page-stack--wide findings-page">
      <div className="rules-hero">
        <div>
          <p className="eyebrow">Findings</p>
          <h1>Findings</h1>
        </div>
        <p className="muted rules-hero__summary">
          Son scan bulgulari, risk seviyeleri ve kaynak bilgileri tek listede toplanir.
        </p>
      </div>
      <FindingsClient />
    </section>
  );
}
