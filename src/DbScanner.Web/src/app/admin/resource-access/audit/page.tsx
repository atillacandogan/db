import Link from "next/link";
import { AdminRouteGuard } from "@/components/auth/admin-route-guard";
import { ResourceAccessAudit } from "@/components/admin/resource-access-audit";

export default function ResourceAccessAuditPage() {
  return <AdminRouteGuard><div className="resource-access-page" lang="tr">
    <Link className="resource-access-back" href="/admin/resource-access">← Ekip ve Hedef Yetkileri</Link>
    <header className="resource-access-header"><div><p className="eyebrow">KİMLİK VE ERİŞİM</p><h1>Denetim geçmişi</h1>
      <p>Proje, ekip, üyelik ve izin değişikliklerini filtreleyip sayfalar arasında inceleyin.</p></div></header>
    <ResourceAccessAudit title="Tüm işlemler" description="Yalnızca başarıyla kaydedilmiş proje, ekip, üyelik ve izin değişiklikleri listelenir." />
  </div></AdminRouteGuard>;
}
