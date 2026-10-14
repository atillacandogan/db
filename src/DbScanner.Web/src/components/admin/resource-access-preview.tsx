"use client";

import { useId, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { apiGet } from "@/lib/api/client";
import { accessPermissionBits, type AccessIdentityUser, type AccessNamed, type AccessPreview } from "@/lib/api/resource-access";
import { ResourceAccessUserPicker } from "./resource-access-user-picker";
import { Button } from "@/components/ui/button";

const roleLabels: Record<string, string> = {
  "dbscanner-admin": "Yönetici", "dbscanner-operator": "Operatör", "dbscanner-viewer": "Gözlemci",
};

export function ResourceAccessPreview({ targets, subject }: { targets: AccessNamed[]; subject?: string }) {
  const id = useId();
  const [user, setUser] = useState<AccessIdentityUser | null>(null);
  const [targetId, setTargetId] = useState("");
  const activeTarget = targets.some(target => target.id === targetId) ? targetId : targets[0]?.id ?? "";
  const activeSubject = subject || user?.id || "";
  const preview = useQuery({
    queryKey: ["resource-access-preview", activeSubject, activeTarget],
    queryFn: ({ signal }) => apiGet<AccessPreview>(`/api/access/preview?subject=${encodeURIComponent(activeSubject)}&targetId=${activeTarget}`, { signal }),
    enabled: !!activeSubject && !!activeTarget, retry: false, staleTime: 0,
  });
  return <section className="resource-access-card resource-access-preview" id="access-preview" lang="tr">
    <div className="resource-access-card-heading"><div><h2>Etkin erişim önizlemesi</h2><p>İzin kaynaklarını ve rol sınırlarını görmek için kullanıcı ve hedef seçin. Bu önizleme izinleri değiştirmez.</p></div></div>
    <div className="resource-access-preview-grid"><div className="resource-access-preview-controls">
      {subject ? <div className="resource-access-selection"><strong>İncelenen ekip üyesi</strong><code>{subject}</code></div> : <ResourceAccessUserPicker selected={user} onSelect={setUser} allowDisabled />}
      <label htmlFor={id}>İncelenecek hedef</label><select id={id} value={activeTarget} disabled={!targets.length} onChange={event => setTargetId(event.target.value)}>
        {!targets.length && <option value="">Henüz hedef yok</option>}{targets.map(target => <option key={target.id} value={target.id}>{target.name}</option>)}
      </select>
    </div><div>
      {!activeSubject || !activeTarget ? <p className="resource-access-empty">Erişimi incelemek için kullanıcı ve hedef seçin.</p>
        : preview.isPending ? <p role="status">İzinler hesaplanıyor…</p>
        : preview.isError ? <div role="alert"><p>{preview.error.message}</p><Button onClick={() => void preview.refetch()}>Tekrar dene</Button></div>
        : <><div className="resource-access-preview-summary"><strong>@{preview.data.user.username}</strong><span>{preview.data.user.role ? roleLabels[preview.data.user.role] ?? preview.data.user.role : "Uygulama rolü yok"}{!preview.data.user.enabled ? " · Devre dışı" : ""}</span><small>{preview.data.target.name}</small><Button type="button" disabled={preview.isFetching} onClick={() => void preview.refetch()}>Yeniden hesapla</Button></div>
          <div className="resource-access-decisions">{preview.data.decisions.map(decision => <div key={decision.permission} className={`resource-access-decision ${decision.allowed ? "is-allowed" : "is-denied"}`}>
            <span aria-hidden="true">{decision.allowed ? "✓" : "−"}</span><div><strong>{accessPermissionBits.find(bit => bit.bit === decision.permission)?.label}</strong><small>{decision.reason}</small></div><b>{decision.allowed ? "İzinli" : "İzinsiz"}</b>
          </div>)}</div><h3>İzin kaynakları</h3>
          {preview.data.sources.length ? <ul className="resource-access-source-list">{preview.data.sources.map(source => <li key={source.id}><strong>{source.teamName}</strong><span>{source.targetId ? "Hedefe özel" : "Proje genelinde"}{source.isArchived ? " · Arşivlenmiş, etkin değil" : ""}</span><small>{accessPermissionBits.filter(bit => (source.permissions & bit.bit) !== 0).map(bit => bit.label).join(", ")}</small></li>)}</ul>
            : <p className="resource-access-empty">Bu hedef için ekip izni yok.{preview.data.user.role === "dbscanner-admin" ? " Erişim yönetici rolünden geliyor." : ""}</p>}
          <p className="resource-access-preview-basis">{preview.data.basis}</p>
        </>}
    </div></div>
  </section>;
}
