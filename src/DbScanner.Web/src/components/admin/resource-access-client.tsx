"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiGet, apiPost } from "@/lib/api/client";
import { Button } from "@/components/ui/button";
import Link from "next/link";
import type { AccessDirectory } from "@/lib/api/resource-access";
import { ResourceAccessAudit } from "./resource-access-audit";

export function ResourceAccessClient() {
  const cache = useQueryClient();
  const directory = useQuery({
    queryKey: ["resource-access"],
    queryFn: ({ signal }) => apiGet<AccessDirectory>("/api/access/directory", { signal }),
  });
  const tenant = useQuery({ queryKey: ["access-me"], queryFn: () => apiGet<{ tenantId: string; dedicatedTenant: boolean }>("/api/access/me") });
  const [projectName, setProjectName] = useState("");
  const [teamName, setTeamName] = useState("");
  const [notice, setNotice] = useState<{ message: string; error: boolean } | null>(null);
  const change = useMutation({
    mutationFn: async (action: () => Promise<unknown>) => action(),
    onSuccess: async () => {
      setNotice({ message: "Kayıt oluşturuldu. Yönetmek için detaylarını açın.", error: false });
      await cache.invalidateQueries({ queryKey: ["resource-access"] });
      await cache.invalidateQueries({ queryKey: ["resource-access-audit"] });
    },
    onError: (error: Error) => setNotice({ message: error.message, error: true }),
  });
  if (directory.isPending) return <p role="status" lang="tr">Projeler ve ekipler yükleniyor…</p>;
  if (directory.isError) return <section role="alert" lang="tr"><p>{directory.error.message}</p><Button onClick={() => void directory.refetch()}>Tekrar dene</Button></section>;
  const data = directory.data;
  return <div className="resource-access-page" lang="tr">
    <header className="resource-access-header">
      <div><p className="eyebrow">KİMLİK VE ERİŞİM · {tenant.data?.tenantId ?? "…"}</p><h1>Ekip ve Hedef Yetkileri</h1>
        <p>Hedefleri proje detaylarından, üyeleri ve izinleri ekip detaylarından yönetin.</p></div>
      <dl className="resource-access-stats"><div><dt>Proje</dt><dd>{data.projects.length}</dd></div><div><dt>Ekip</dt><dd>{data.teams.length}</dd></div><div><dt>İzin</dt><dd>{data.grants.length}</dd></div></dl>
    </header>
    <p className="resource-access-policy">Roller yapılabilecek işlemleri, ekip izinleri erişilebilecek hedefleri belirler. Yönetici tüm hedefleri görür; ekip üyeliği olmayan operator/viewer hedef göremez.</p>
    {notice && <div className={`resource-access-notice${notice.error ? " resource-access-notice--error" : ""}`} role={notice.error ? "alert" : "status"}>
      <span>{notice.message}</span><button type="button" onClick={() => setNotice(null)} aria-label="Bildirimi kapat">×</button>
    </div>}
    <div className="resource-access-grid">
      <section className="resource-access-card resource-access-projects" aria-labelledby="projects-heading">
        <div className="resource-access-card-heading"><span className="resource-access-step">01</span><div><h2 id="projects-heading">Projeler</h2><p>Veritabanı hedeflerini ayrı çalışma alanlarında düzenleyin.</p></div></div>
        <form onSubmit={e => {
          e.preventDefault();
          const name = projectName.trim();
          if (!name || change.isPending) return;
          change.mutate(async () => { await apiPost("/api/access/projects", { name }); setProjectName(""); });
        }}>
          <label htmlFor="project-name">Yeni proje adı</label><div className="resource-access-input-action"><input id="project-name" required maxLength={160} placeholder="Örn. Üretim veritabanları" value={projectName} onChange={e => setProjectName(e.target.value)} /><Button disabled={change.isPending || !projectName.trim()}>Proje oluştur</Button></div>
        </form>
        <div className="resource-access-summary">
          <h3>Kayıtlı projeler <span className="resource-access-count">{data.projects.length}</span></h3>
          {data.projects.length ? <ul className="resource-access-directory-list" tabIndex={0} aria-label="Kayıtlı projeler">{data.projects.map(project => {
            const targetCount = data.targets.filter(target => target.projectId === project.id).length;
            return <li key={project.id}><Link className="resource-access-directory-row resource-access-team-link" href={`/admin/resource-access/projects/${project.id}`}>
              <div><strong>{project.name}</strong><small>{targetCount} hedef</small></div><span>Detaylar →</span>
            </Link></li>;
          })}</ul> : <p className="resource-access-empty">Henüz proje oluşturulmadı.</p>}
        </div>
      </section>
      <section className="resource-access-card resource-access-teams" aria-labelledby="teams-heading">
        <div className="resource-access-card-heading"><span className="resource-access-step">02</span><div><h2 id="teams-heading">Ekipler</h2><p>Birlikte çalışacak kullanıcıları ekiplerde bir araya getirin.</p></div></div>
        <form onSubmit={e => {
          e.preventDefault();
          const name = teamName.trim();
          if (!name || change.isPending) return;
          change.mutate(async () => { await apiPost("/api/access/teams", { name }); setTeamName(""); });
        }}>
          <label htmlFor="team-name">Yeni ekip adı</label><div className="resource-access-input-action"><input id="team-name" required maxLength={160} placeholder="Örn. Güvenlik ekibi" value={teamName} onChange={e => setTeamName(e.target.value)} /><Button disabled={change.isPending || !teamName.trim()}>Ekip oluştur</Button></div>
        </form>
        <div className="resource-access-summary">
          <h3>Kayıtlı ekipler <span className="resource-access-count">{data.teams.length}</span></h3>
          {data.teams.length ? <ul className="resource-access-directory-list">{data.teams.map(team => <li key={team.id}>
            <Link className="resource-access-directory-row resource-access-team-link" href={`/admin/resource-access/teams/${team.id}`}>
              <div><strong>{team.name}</strong><small>{data.members.filter(member => member.teamId === team.id).length} üye{team.isArchived ? " · Arşivlenmiş" : " · Aktif"}</small></div><span>Detaylar →</span>
            </Link>
          </li>)}</ul> : <p className="resource-access-empty">Henüz ekip oluşturulmadı.</p>}
        </div>
      </section>
    </div>
    <ResourceAccessAudit limit={5} title="Son işlemler"
      description="Proje, ekip, üyelik ve izin değişiklikleri. Tarama ve oturum olayları bu geçmişin kapsamında değildir."
      allHref="/admin/resource-access/audit" allLabel="Tüm denetim geçmişini gör →" />
  </div>;
}
