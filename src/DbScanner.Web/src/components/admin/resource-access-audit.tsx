"use client";

import { useId, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { apiGet } from "@/lib/api/client";
import { accessActionLabels, type AccessAuditPage } from "@/lib/api/resource-access";
import { Button } from "@/components/ui/button";

function snapshot(value: string | null) {
  if (!value) return "—";
  try { return JSON.stringify(JSON.parse(value), null, 2); } catch { return value; }
}
export function ResourceAccessAudit({ teamId, entityType, entityId }: { teamId?: string; entityType?: string; entityId?: string }) {
  const id = useId();
  const [page, setPage] = useState(1);
  const [action, setAction] = useState("");
  const audit = useQuery({ queryKey: ["resource-access-audit", teamId, entityType, entityId, action, page],
    queryFn: ({ signal }) => apiGet<AccessAuditPage>(`/api/access/audit?page=${page}&action=${encodeURIComponent(action)}${teamId ? `&teamId=${encodeURIComponent(teamId)}` : ""}${entityType && entityId ? `&entityType=${encodeURIComponent(entityType)}&entityId=${encodeURIComponent(entityId)}` : ""}`, { signal }), retry: false,
  });
  return <section className="resource-access-card resource-access-audit" lang="tr">
    <div className="resource-access-card-heading"><div><h2>Denetim geçmişi</h2><p>Kimin, neyi, ne zaman değiştirdiğini görün. Yalnızca başarıyla kaydedilen değişiklikler gösterilir.</p></div></div>
    <div className="resource-access-audit-toolbar"><div><label htmlFor={id}>İşlem türü</label><select id={id} value={action} onChange={event => { setAction(event.target.value); setPage(1); }}><option value="">Tüm işlemler</option>{Object.entries(accessActionLabels).map(([value, label]) => <option value={value} key={value}>{label}</option>)}</select></div><Button type="button" disabled={audit.isFetching} onClick={() => void audit.refetch()}>Yenile</Button></div>
    {audit.isPending ? <p role="status">Denetim geçmişi yükleniyor…</p> : audit.isError ? <div role="alert"><p>{audit.error.message}</p><Button onClick={() => void audit.refetch()}>Tekrar dene</Button></div>
      : <><div className="resource-access-table-wrap"><table><thead><tr><th>Zaman</th><th>İşlemi yapan</th><th>İşlem / kayıt</th><th>Değişiklik</th></tr></thead><tbody>{audit.data.items.map(item => <tr key={item.id}>
        <td><time dateTime={item.timestamp}>{new Date(item.timestamp).toLocaleString("tr-TR")}</time></td><td><strong>{item.actorUsername ?? "Kullanıcı"}</strong><code>{item.actorSubject}</code></td>
        <td>{accessActionLabels[item.action] ?? item.action}<code>{item.entityId}</code></td><td><details><summary>Önce / Sonra</summary><div className="resource-access-audit-snapshots"><div><strong>Önce</strong><pre>{snapshot(item.beforeJson)}</pre></div><div><strong>Sonra</strong><pre>{snapshot(item.afterJson)}</pre></div></div></details></td>
      </tr>)}</tbody></table></div>{!audit.data.items.length && <p className="resource-access-empty">Bu kapsamda henüz denetim kaydı yok. Önceki değişiklikler geriye dönük kaydedilmez.</p>}
      <div className="resource-access-pager"><button type="button" disabled={page === 1 || audit.isFetching} onClick={() => setPage(value => value - 1)}>Önceki</button><span>{audit.data.totalCount} kayıt · Sayfa {page}</span><button type="button" disabled={page * audit.data.pageSize >= audit.data.totalCount || audit.isFetching} onClick={() => setPage(value => value + 1)}>Sonraki</button></div></>}
  </section>;
}
