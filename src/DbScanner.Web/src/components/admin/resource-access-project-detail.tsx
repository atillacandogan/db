"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiGet, type ApiError } from "@/lib/api/client";
import { accessPermissionBits, type AccessDirectory } from "@/lib/api/resource-access";
import { deleteAccessProject, getAccessProject, moveAccessTargetToProject, projectDeletionBlockers, renameAccessProject } from "@/lib/api/resource-access-projects";
import { Button } from "@/components/ui/button";
import { ResourceAccessAudit } from "./resource-access-audit";
import { ResourceAccessProjectTargets } from "./resource-access-project-targets";

const directoryPath = "/admin/resource-access";
const relatedQueryKeys = ["resource-access", "resource-access-team", "resource-access-audit", "resource-access-preview", "targets", "dashboard"];

export function ResourceAccessProjectDetail({ projectId }: { projectId: string }) {
  const cache = useQueryClient();
  const router = useRouter();
  const [name, setName] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [confirmation, setConfirmation] = useState("");
  const [deleted, setDeleted] = useState(false);
  const [notice, setNotice] = useState<{ message: string; error: boolean } | null>(null);
  const [auditOpen, setAuditOpen] = useState(false);
  const deleteToggle = useRef<HTMLButtonElement>(null);
  const confirmationInput = useRef<HTMLInputElement>(null);
  const restoreDeleteFocus = useRef(false);
  useEffect(() => {
    if (confirmDelete) confirmationInput.current?.focus();
    else if (restoreDeleteFocus.current) { deleteToggle.current?.focus(); restoreDeleteFocus.current = false; }
  }, [confirmDelete]);
  const project = useQuery({
    queryKey: ["resource-access-project", projectId],
    queryFn: ({ signal }) => getAccessProject(projectId, signal), retry: false, enabled: !deleted,
  });
  const directory = useQuery({
    queryKey: ["resource-access"],
    queryFn: ({ signal }) => apiGet<AccessDirectory>("/api/access/directory", { signal }),
    retry: false, enabled: !deleted && project.isSuccess,
  });
  const refreshRelated = () => Promise.all(relatedQueryKeys.map(key => cache.invalidateQueries({ queryKey: [key] })));
  const rename = useMutation({
    mutationFn: (newName: string) => renameAccessProject(projectId, newName),
    onSuccess: async () => {
      await Promise.all([refreshRelated(), cache.invalidateQueries({ queryKey: ["resource-access-project", projectId] })]);
      setName(null); setConfirmDelete(false); setConfirmation("");
      setNotice({ message: "Proje adı ve denetim kaydı kaydedildi. Hedefler ve izinler korundu.", error: false });
    },
    onError: (error: Error) => setNotice({ message: error.message, error: true }),
  });
  const remove = useMutation({
    mutationFn: () => {
      if (!project.data) throw new Error("Proje bilgileri alınmadan silme yapılamaz.");
      return deleteAccessProject(project.data, confirmation);
    },
    onSuccess: async () => {
      setDeleted(true);
      cache.removeQueries({ queryKey: ["resource-access-project", projectId], exact: true });
      await refreshRelated();
      router.replace(directoryPath);
    },
    onError: async (error: Error) => {
      setNotice({ message: error.message, error: true });
      await cache.invalidateQueries({ queryKey: ["resource-access-project", projectId] });
    },
  });
  const move = useMutation({
    mutationFn: ({ targetId, destinationProjectId }: { targetId: string; destinationProjectId: string }) => {
      if (!project.data || !directory.data) throw new Error("Proje ve hedef bilgileri yüklenmeden taşıma yapılamaz.");
      const target = directory.data.targets.find(item => item.id === targetId);
      if (!target || !directory.data.projects.some(item => item.id === destinationProjectId)) throw new Error("Hedef veya proje değişmiş olabilir. Sayfayı yenileyip tekrar deneyin.");
      if (target.projectId !== projectId && destinationProjectId !== projectId) throw new Error("Bu sayfada yalnızca seçili projenin hedefleri yönetilebilir.");
      if (target.projectId === destinationProjectId) throw new Error("Hedef zaten bu projeye bağlı.");
      return moveAccessTargetToProject(targetId, destinationProjectId);
    },
    onSuccess: async () => {
      await Promise.all([refreshRelated(), cache.invalidateQueries({ queryKey: ["resource-access-project"] })]);
      setNotice({ message: "Hedefin projesi değiştirildi. Tarama geçmişi korundu; hedefe özel eski ekip izinleri kaldırıldı.", error: false });
    },
    onError: async (error: Error) => {
      setNotice({ message: error.message, error: true });
      await Promise.all([cache.invalidateQueries({ queryKey: ["resource-access"] }), cache.invalidateQueries({ queryKey: ["resource-access-project", projectId] })]);
    },
  });

  if (deleted) return <section className="resource-access-page" lang="tr"><p role="status">Proje silindi. Proje listesine dönülüyor…</p><Link className="resource-access-back" href={directoryPath}>Proje listesine dön</Link></section>;
  if (project.isPending) return <p role="status" lang="tr">Proje ayrıntıları yükleniyor…</p>;
  if (project.isError) {
    const missing = (project.error as ApiError).status === 404;
    return <section className="resource-access-page" role="alert" lang="tr"><h1>{missing ? "Proje bulunamadı" : "Proje ayrıntıları alınamadı"}</h1>
      <p>{missing ? "Bu proje silinmiş olabilir veya mevcut kurumunuzda bulunmuyor." : project.error.message}</p>
      {!missing && <Button type="button" disabled={project.isFetching} onClick={() => void project.refetch()}>Tekrar dene</Button>}
      <Link className="resource-access-back" href={directoryPath}>← Proje listesine dön</Link>
    </section>;
  }
  const data = project.data;
  const blockers = projectDeletionBlockers(data);
  const busy = rename.isPending || remove.isPending || move.isPending;
  const editedName = name ?? data.project.name;
  const teamCount = new Set(data.grants.map(grant => grant.teamId)).size;
  const canDelete = blockers.length === 0 && !project.isFetching;

  return <div className="resource-access-page resource-access-project-detail" lang="tr">
    <Link className="resource-access-back" href={directoryPath}>← Ekip ve Hedef Yetkileri</Link>
    <header className="resource-access-header">
      <div><p className="eyebrow">PROJE AYRINTILARI · {data.project.isDefault ? "VARSAYILAN PROJE" : "ÇALIŞMA ALANI"}</p><h1>{data.project.name}</h1>
        <p>Proje adını ve bağlı hedefleri yönetin. Erişimi olan ekipleri aşağıda inceleyin.</p></div>
      <dl className="resource-access-stats"><div><dt>Hedef</dt><dd>{data.targets.length}</dd></div><div><dt>Ekip</dt><dd>{teamCount}</dd></div><div><dt>İzin</dt><dd>{data.grants.length}</dd></div></dl>
    </header>
    {notice && <div className={`resource-access-notice${notice.error ? " resource-access-notice--error" : ""}`} role={notice.error ? "alert" : "status"}>
      <span>{notice.message}</span><button type="button" aria-label="Bildirimi kapat" onClick={() => setNotice(null)}>×</button>
    </div>}
    <div className="resource-access-detail-grid">
      <section className="resource-access-card" aria-labelledby="project-info-heading">
        <div className="resource-access-card-heading"><span className="resource-access-step">01</span><div><h2 id="project-info-heading">Proje bilgileri</h2><p>İsim değişikliği hedefleri veya erişim izinlerini değiştirmez.</p></div></div>
        <form onSubmit={event => { event.preventDefault(); if (!busy && editedName.trim() && editedName.trim() !== data.project.name) rename.mutate(editedName); }}>
          <label htmlFor="detail-project-name">Proje adı</label><input id="detail-project-name" required maxLength={160} value={editedName} disabled={busy} onChange={event => { setName(event.target.value); setConfirmDelete(false); setConfirmation(""); }} />
          <Button type="submit" disabled={busy || !editedName.trim() || editedName.trim() === data.project.name}>{rename.isPending ? "Kaydediliyor…" : "Adı kaydet"}</Button>
        </form>
        <p className="resource-access-policy">{data.project.isDefault ? "Varsayılan proje yeni hedeflerin başlangıç alanıdır. Adını değiştirebilirsiniz; bu proje silinemez." : "Projeler hedefleri gruplar; ekip üyeleri ve roller ayrı yönetilir."}</p>
      </section>
      <ResourceAccessProjectTargets projectId={projectId} targets={data.targets} directory={directory.data}
        directoryPending={directory.isPending} directoryFetching={directory.isFetching} directoryError={directory.isError ? directory.error : null}
        onRetry={() => void directory.refetch()} disabled={busy || project.isFetching}
        onMove={(targetId, destinationProjectId) => move.mutate({ targetId, destinationProjectId })} />
    </div>
    <section className="resource-access-card" aria-labelledby="project-grants-heading">
      <div className="resource-access-card-heading resource-access-project-section-heading"><div><h2 id="project-grants-heading">Erişimi olan ekipler <span className="resource-access-count">{teamCount}</span></h2><p>İzinler ilgili ekibin detaylarında yönetilir; her kullanıcının rolünün izin verdiği işlemlerle sınırlıdır.</p></div></div>
      {data.grants.length ? <div className="resource-access-table-wrap" tabIndex={0} aria-label="Bu projenin ekip izinleri"><table><thead><tr><th>Ekip</th><th>Hedef kapsamı</th><th>İzinler</th><th>Durum</th></tr></thead><tbody>{data.grants.map(grant => {
        const team = data.teams.find(item => item.id === grant.teamId);
        return <tr key={grant.id}><td><Link href={`${directoryPath}/teams/${encodeURIComponent(grant.teamId)}#team-permissions`}>{team?.name ?? grant.teamId}</Link></td>
          <td>{grant.targetId ? data.targets.find(target => target.id === grant.targetId)?.name ?? grant.targetId : "Projedeki tüm hedefler"}</td>
          <td><div className="resource-access-tags">{accessPermissionBits.filter(bit => (grant.permissions & bit.bit) !== 0).map(bit => <span key={bit.bit}>{bit.label}</span>)}</div></td>
          <td>{team?.isArchived ? "Arşivlenmiş · İzin etkin değil" : "Aktif ekip"}</td></tr>;
      })}</tbody></table></div> : <p className="resource-access-empty">Bu projeye henüz ekip izni verilmedi.</p>}
      {data.teams.filter(team => !team.isArchived).length > 0 ? <details className="resource-access-disclosure">
        <summary>Bir ekibe bu proje için izin ver</summary><p className="resource-access-empty">İlgili ekibin detaylarından bu projeyi seçip izinlerini kaydedin.</p>
        <div className="resource-access-project-team-links">{data.teams.filter(team => !team.isArchived).map(team => <Link key={team.id} href={`${directoryPath}/teams/${encodeURIComponent(team.id)}#team-permissions`}>{team.name} →</Link>)}</div>
      </details> : <p className="resource-access-empty">İzin vermek için önce <Link href={directoryPath}>bir ekip oluşturun</Link>.</p>}
    </section>
    <details className="resource-access-card resource-access-project-danger resource-access-disclosure">
      <summary>Projeyi sil</summary><p className="resource-access-empty">Yalnızca hedefi ve ekip izni olmayan, varsayılan olmayan projeler silinebilir.</p>
      {blockers.length > 0 && <ul id="project-delete-blockers" className="resource-access-project-blockers">{blockers.map(reason => <li key={reason}>{reason}</li>)}</ul>}
      {!confirmDelete ? <button ref={deleteToggle} type="button" className="button button--danger" disabled={busy || !canDelete} aria-describedby={blockers.length ? "project-delete-blockers" : undefined} onClick={() => { setConfirmDelete(true); setConfirmation(""); }}>Projeyi sil</button>
        : <form className="resource-access-project-delete-confirm" onSubmit={event => { event.preventDefault(); if (!busy && canDelete && confirmation === data.project.name) remove.mutate(); }}>
          <strong>Bu işlem geri alınamaz.</strong><p id="project-delete-help">Silme işlemi sadece boş proje kaydını kaldırır; kullanıcılar ve ekipler silinmez. Onaylamak için <strong>{data.project.name}</strong> yazın.</p>
          <label htmlFor="project-delete-confirmation">Silinecek proje adı</label><input ref={confirmationInput} id="project-delete-confirmation" autoComplete="off" value={confirmation} disabled={busy || !canDelete} aria-describedby="project-delete-help" onChange={event => setConfirmation(event.target.value)} />
          <div className="resource-access-project-confirm-actions"><Button type="submit" className="button button--danger" disabled={busy || !canDelete || confirmation !== data.project.name}>{remove.isPending ? "Siliniyor…" : "Kalıcı silmeyi onayla"}</Button>
            <Button type="button" className="button button--secondary" disabled={busy} onClick={() => { restoreDeleteFocus.current = true; setConfirmDelete(false); setConfirmation(""); }}>İptal</Button></div>
        </form>}
    </details>
    <details className="resource-access-disclosure resource-access-card" open={auditOpen} onToggle={event => setAuditOpen(event.currentTarget.open)}><summary>Denetim geçmişi</summary>{auditOpen && <ResourceAccessAudit entityType="project" entityId={projectId} />}</details>
  </div>;
}
