"use client";

import Link from "next/link";
import { useEffect, useId, useRef, useState } from "react";
import { apiDelete, apiPut } from "@/lib/api/client";
import { accessPermissionBits, type AccessGrant, type AccessTeamDetail } from "@/lib/api/resource-access";
import { Button } from "@/components/ui/button";

export function ResourceAccessTeamGrants({ data, busy, onChange }: {
  data: AccessTeamDetail;
  busy: boolean;
  onChange: (action: () => Promise<unknown>) => void;
}) {
  const id = useId();
  const [formOpen, setFormOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [projectId, setProjectId] = useState("");
  const [targetId, setTargetId] = useState("");
  const [permissions, setPermissions] = useState(1);
  const form = useRef<HTMLFormElement>(null);
  const projectSelect = useRef<HTMLSelectElement>(null);
  const permissionInput = useRef<HTMLInputElement>(null);
  const editing = data.grants.find(grant => grant.id === editingId);
  const validProject = projectId ? data.projects.some(project => project.id === projectId) : data.projects.length > 0;
  const activeProject = validProject ? projectId || data.projects[0]?.id || "" : projectId ? "unavailable" : "";
  const projectTargets = data.targets.filter(target => target.projectId === activeProject);
  const validTarget = !targetId || projectTargets.some(target => target.id === targetId);
  const activeTarget = validTarget ? targetId : "unavailable";
  const archived = data.team.isArchived;

  const validEditingScope = !editing || (editing.projectId === projectId && (editing.targetId ?? "") === targetId);

  // Removed or moved grants must not be recreated from stale editor state.
  if (editingId && (!editing || !validEditingScope)) {
    setEditingId(null);
    setFormOpen(false);
    setProjectId("");
    setTargetId("");
    setPermissions(1);
  }

  useEffect(() => {
    if (!formOpen) return;
    form.current?.scrollIntoView?.({ block: "nearest", behavior: "smooth" });
    if (editingId) permissionInput.current?.focus();
    else projectSelect.current?.focus();
  }, [formOpen, editingId]);

  const reset = () => {
    setFormOpen(false); setEditingId(null); setProjectId(""); setTargetId(""); setPermissions(1);
  };
  const edit = (grant: AccessGrant) => {
    setEditingId(grant.id); setProjectId(grant.projectId); setTargetId(grant.targetId ?? "");
    setPermissions(grant.permissions | 1); setFormOpen(true);
  };
  const name = (items: { id: string; name: string }[], itemId: string) => items.find(item => item.id === itemId)?.name ?? itemId;

  return <section id="team-permissions" className="resource-access-card" aria-labelledby={`${id}-heading`}>
    <div className="resource-access-section-toolbar"><div><h2 id={`${id}-heading`}>Ekip izinleri <span className="resource-access-count">{data.grants.length}</span></h2>
      <p className="resource-access-empty">{archived ? "Arşivlenmiş ekibin izinleri etkin değildir. Mevcut izinleri kaldırabilirsiniz." : "Bu ekibin erişebileceği proje ve hedefleri belirleyin. İşlemler, üyelerin uygulama rolleriyle sınırlıdır."}</p>
    </div>{!archived && !formOpen && <Button type="button" disabled={busy || !data.projects.length} aria-expanded={formOpen} aria-controls={`${id}-form`} onClick={() => { reset(); setProjectId(data.projects[0]?.id ?? ""); setFormOpen(true); }}>İzin ekle</Button>}</div>

    {formOpen && <form id={`${id}-form`} ref={form} className="resource-access-grant-editor" onSubmit={event => {
      event.preventDefault();
      if (busy || archived || !validProject || !activeProject || !validTarget || !validEditingScope || (editingId && !editing)) return;
      onChange(async () => {
        await apiPut("/api/access/grants", { teamId: data.team.id, projectId: activeProject, targetId: targetId || null, permissions: permissions | 1 });
        reset();
      });
    }}>
      <h3>{editing ? "İzni düzenle" : "Yeni proje veya hedef izni"}</h3>
      <div className="resource-access-fields"><div><label htmlFor={`${id}-project`}>Proje</label><select ref={projectSelect} id={`${id}-project`} required value={activeProject} disabled={busy || archived || !!editing} onChange={event => { setProjectId(event.target.value); setTargetId(""); }}>
        {!validProject && projectId && <option value="unavailable" disabled>Proje kaldırılmış; yeniden seçin</option>}{!data.projects.length && !projectId && <option value="">Önce proje oluşturun</option>}{data.projects.map(project => <option key={project.id} value={project.id}>{project.name}</option>)}
      </select></div><div><label htmlFor={`${id}-target`}>Hedef kapsamı</label><select id={`${id}-target`} value={activeTarget} disabled={busy || archived || !!editing} onChange={event => setTargetId(event.target.value)}>
        {!validTarget && <option value="unavailable" disabled>Hedef değişmiş; kapsamı yeniden seçin</option>}<option value="">Projedeki tüm hedefler</option>{projectTargets.map(target => <option key={target.id} value={target.id}>{target.name}</option>)}
      </select></div></div>
      {!validProject && projectId ? <p role="alert">Seçilen proje artık mevcut değil. İzin kaydetmeden önce proje ve hedef kapsamını yeniden seçin.</p>
        : !validTarget ? <p role="alert">Seçilen hedef artık bu projede değil. Hedef kapsamını yeniden seçin.</p>
        : editing ? <small>Bu kaydın işlemlerini düzenliyorsunuz. Kapsamı değiştirmek için izni kaldırıp yeni izin ekleyin.</small>
        : !targetId && <small>İleride bu projeye eklenen hedefler de bu izne dahil olur.</small>}
      <fieldset disabled={busy || archived}><legend>İzin verilen işlemler</legend>{accessPermissionBits.map(permission => <label key={permission.bit} className="resource-access-check"><input ref={permission.bit === 2 ? permissionInput : undefined} type="checkbox" disabled={permission.bit === 1} checked={(permissions & permission.bit) !== 0} onChange={event => setPermissions(value => event.target.checked ? value | permission.bit : value & ~permission.bit)} />{permission.label}</label>)}</fieldset>
      <div className="resource-access-inline-actions"><Button type="submit" disabled={busy || archived || !validProject || !activeProject || !validTarget || !validEditingScope}>{editing ? "İzni güncelle" : "İzni kaydet"}</Button><Button type="button" className="button button--secondary" disabled={busy} onClick={reset}>İptal</Button></div>
    </form>}

    {data.grants.length ? <div className="resource-access-table-wrap" tabIndex={0} aria-label="Ekibin proje ve hedef izinleri"><table><thead><tr><th>Proje</th><th>Hedef kapsamı</th><th>İzinler</th><th>İşlem</th></tr></thead><tbody>{data.grants.map(grant => <tr key={grant.id}>
      <td><Link href={`/admin/resource-access/projects/${encodeURIComponent(grant.projectId)}`}>{name(data.projects, grant.projectId)}</Link></td><td>{grant.targetId ? name(data.targets, grant.targetId) : "Projedeki tüm hedefler"}</td>
      <td><div className="resource-access-tags">{accessPermissionBits.filter(bit => (grant.permissions & bit.bit) !== 0).map(bit => <span key={bit.bit}>{bit.label}</span>)}</div></td>
      <td><div className="resource-access-inline-actions"><button type="button" disabled={busy || archived} onClick={() => edit(grant)}>Düzenle</button><button type="button" className="resource-access-remove" disabled={busy} onClick={() => onChange(async () => { await apiDelete(`/api/access/grants/${encodeURIComponent(grant.id)}`); if (editingId === grant.id) reset(); })}>İzni kaldır</button></div></td>
    </tr>)}</tbody></table></div> : <p className="resource-access-empty">Bu ekibe henüz proje veya hedef izni verilmedi.</p>}
    {!data.projects.length && <p className="resource-access-empty">İzin vermeden önce <Link href="/admin/resource-access">bir proje oluşturun</Link>.</p>}
  </section>;
}
