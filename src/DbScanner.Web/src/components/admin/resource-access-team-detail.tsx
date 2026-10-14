"use client";

import Link from "next/link";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiDelete, apiGet, apiPut } from "@/lib/api/client";
import { type AccessTeamDetail, type AccessIdentityUser } from "@/lib/api/resource-access";
import { Button } from "@/components/ui/button";
import { ResourceAccessPreview } from "./resource-access-preview";
import { ResourceAccessAudit } from "./resource-access-audit";
import { ResourceAccessUserPicker } from "./resource-access-user-picker";
import { ResourceAccessTeamGrants } from "./resource-access-team-grants";

const relatedQueryKeys = ["resource-access", "resource-access-team", "resource-access-project", "resource-access-audit", "resource-access-preview", "targets", "dashboard"];
const maximumTeamBatchSize = 50;

export function ResourceAccessTeamDetail({ teamId }: { teamId: string }) {
  return <ResourceAccessTeamDetailView key={teamId} teamId={teamId} />;
}

function ResourceAccessTeamDetailView({ teamId }: { teamId: string }) {
  const cache = useQueryClient();
  const teamPath = `/api/access/teams/${encodeURIComponent(teamId)}`;
  const teamQuery = useQuery({ queryKey: ["resource-access-team", teamId], queryFn: () => apiGet<AccessTeamDetail>(teamPath), retry: false });
  const [name, setName] = useState<string | null>(null);
  const [confirmArchive, setConfirmArchive] = useState(false);
  const [selectedUsers, setSelectedUsers] = useState<AccessIdentityUser[]>([]);
  const [previewSubject, setPreviewSubject] = useState("");
  const [previewOpen, setPreviewOpen] = useState(false);
  const [auditOpen, setAuditOpen] = useState(false);
  const [notice, setNotice] = useState<{ message: string; error: boolean } | null>(null);
  const change = useMutation({ mutationFn: (action: () => Promise<unknown>) => {
    if (teamQuery.isFetching) throw new Error("Ekip bilgileri yenileniyor. Güncelleme tamamlanınca tekrar deneyin.");
    return action();
  },
    onSuccess: async result => {
      await Promise.all(relatedQueryKeys.map(key => cache.invalidateQueries({ queryKey: [key] })));
      const batchResult = result && typeof result === "object" && "addedCount" in result
        && typeof result.addedCount === "number" ? result as { addedCount: number; alreadyMemberCount?: number } : null;
      const message = batchResult
        ? `${batchResult.addedCount} kullanıcı eklendi.${batchResult.alreadyMemberCount ? ` ${batchResult.alreadyMemberCount} kullanıcı zaten ekip üyesiydi.` : ""} Denetim kayıtları kaydedildi.`
        : "Değişiklikler ve denetim kaydı kaydedildi.";
      setNotice({ message, error: false }); setConfirmArchive(false);
    },
    onError: (error: Error) => setNotice({ message: error.message, error: true }),
  });
  if (teamQuery.isPending) return <p role="status" lang="tr">Ekip ayrıntıları yükleniyor…</p>;
  if (teamQuery.isError) return <section role="alert" lang="tr"><p>{teamQuery.error.message}</p><Button onClick={() => void teamQuery.refetch()}>Tekrar dene</Button><p><Link href="/admin/resource-access">Ekip yönetimine dön</Link></p></section>;
  const data = teamQuery.data;
  const busy = change.isPending || teamQuery.isFetching;
  const memberIds = data.members.map(member => member.subject);
  const availableSelectedUsers = selectedUsers.filter(user => user.enabled && !memberIds.includes(user.id));
  const toggleSelectedUser = (user: AccessIdentityUser) => setSelectedUsers(current =>
    current.some(selectedUser => selectedUser.id === user.id)
      ? current.filter(selectedUser => selectedUser.id !== user.id)
      : [...current, user]);
  const affectedTargets = data.targets.filter(target => data.grants.some(grant => grant.projectId === target.projectId && (!grant.targetId || grant.targetId === target.id)));
  return <div className="resource-access-page" lang="tr">
    <Link className="resource-access-back" href="/admin/resource-access">← Ekip ve Hedef Yetkileri</Link>
    <header className="resource-access-header"><div><p className="eyebrow">EKİP AYRINTILARI · {data.team.isArchived ? "ARŞİVLENMİŞ" : "AKTİF"}</p><h1>{data.team.name}</h1><code>{data.team.id}</code></div>
      <dl className="resource-access-stats"><div><dt>Üyeler</dt><dd>{data.members.length}</dd></div><div><dt>İzinler</dt><dd>{data.grants.length}</dd></div><div><dt>Kapsamdaki hedefler</dt><dd>{affectedTargets.length}</dd></div></dl>
    </header>
    {data.team.isArchived && <p className="resource-access-policy">Bu ekip arşivlenmiş. Üyelikler ve izinler korunur ancak hedef erişiminde geçerli değildir. Ekip yeniden etkinleştirildiğinde mevcut izinleri de etkinleşir.</p>}
    {notice && <p className={`resource-access-notice${notice.error ? " resource-access-notice--error" : ""}`} role={notice.error ? "alert" : "status"}>{notice.message}</p>}
    <div className="resource-access-detail-grid">
      <section className="resource-access-card"><h2>Ekip bilgileri</h2><form onSubmit={event => { event.preventDefault(); if (busy) return; change.mutate(async () => { await apiPut(teamPath, { name: name ?? data.team.name }); setName(null); }); }}>
        <label htmlFor="detail-team-name">Ekip adı</label><input id="detail-team-name" value={name ?? data.team.name} disabled={busy} required maxLength={160} onChange={event => setName(event.target.value)} /><Button disabled={busy || !(name ?? data.team.name).trim()}>Adı kaydet</Button>
      </form><h3>{data.team.isArchived ? "Ekibi yeniden etkinleştir" : "Güvenli arşivleme"}</h3><p className="resource-access-empty">Ekip silinmez. Kullanıcılar ve tarama geçmişi etkilenmez.{data.team.isArchived ? " Mevcut ekip izinleri yeniden etkinleşir." : " Bu ekibin izinleri geçersiz olur; diğer ekiplerin izinleri geçerli kalır. Kuyruktaki veya çalışan taramalar otomatik iptal edilmez."}</p>
        {!confirmArchive ? <Button type="button" disabled={busy} onClick={() => setConfirmArchive(true)}>{data.team.isArchived ? "Ekibi yeniden etkinleştir" : "Ekibi arşivle"}</Button>
          : <div className="resource-access-archive-confirm" role="alert"><strong>{data.team.isArchived ? "Mevcut izinler yeniden etkinleşecek." : "Bu ekibin sağladığı erişim devre dışı kalacak."}</strong><p>{data.members.length} üye · {data.grants.length} izin · {affectedTargets.length} hedef kapsamda</p>
            <Button type="button" disabled={busy} onClick={() => change.mutate(() => apiPut(`${teamPath}/archive`, { archived: !data.team.isArchived }))}>{data.team.isArchived ? "Etkinleştirmeyi onayla" : "Arşivlemeyi onayla"}</Button><Button type="button" disabled={busy} onClick={() => setConfirmArchive(false)}>İptal</Button>
          </div>}
      </section>
      <section className="resource-access-card"><h2>Üyeler</h2>
        <ul className="resource-access-detail-members">{data.members.map(member => <li key={member.subject}><code>{member.subject}</code><div><a href="#team-access-preview" onClick={() => { setPreviewSubject(member.subject); setPreviewOpen(true); }}>İzinleri incele</a><button type="button" className="resource-access-remove" disabled={busy} onClick={() => change.mutate(() => apiDelete(`${teamPath}/members/${encodeURIComponent(member.subject)}`))}>Üyeliği kaldır</button></div></li>)}</ul>
        {!data.members.length && <p className="resource-access-empty">Henüz üye yok.</p>}
        {!data.team.isArchived && <form onSubmit={event => {
          event.preventDefault();
          if (busy || availableSelectedUsers.length === 0 || availableSelectedUsers.length > maximumTeamBatchSize) return;
          const subjects = availableSelectedUsers.map(user => user.id);
          change.mutate(async () => {
            const result = await apiPut<{ addedCount: number; alreadyMemberCount: number }>(`${teamPath}/members/batch`, { subjects });
            setSelectedUsers([]);
            return result;
          });
        }}>
          <ResourceAccessUserPicker selectedUsers={selectedUsers} onToggleUser={toggleSelectedUser} existingUserIds={memberIds} maxSelected={maximumTeamBatchSize} disabled={busy} />
          <Button disabled={busy || availableSelectedUsers.length === 0 || availableSelectedUsers.length > maximumTeamBatchSize}>
            {selectedUsers.length > 0 && availableSelectedUsers.length === 0 ? "Seçilenler zaten ekip üyesi" : `Seçilen ${availableSelectedUsers.length} kullanıcıyı ekle`}
          </Button>
        </form>}
      </section>
    </div>
    <ResourceAccessTeamGrants data={data} busy={busy} onChange={action => change.mutate(action)} />
    <details className="resource-access-disclosure" id="team-access-preview" open={previewOpen} onToggle={event => setPreviewOpen(event.currentTarget.open)}>
      <summary>Etkin erişim önizlemesi</summary>{previewOpen && <ResourceAccessPreview targets={data.targets} subject={previewSubject || undefined} />}
    </details>
    <details className="resource-access-disclosure" open={auditOpen} onToggle={event => setAuditOpen(event.currentTarget.open)}>
      <summary>Değişiklik geçmişi</summary>{auditOpen && <ResourceAccessAudit teamId={teamId} />}
    </details>
  </div>;
}
