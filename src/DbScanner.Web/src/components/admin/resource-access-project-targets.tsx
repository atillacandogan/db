"use client";

import Link from "next/link";
import { useState } from "react";
import type { AccessDirectory, AccessProjectDetail } from "@/lib/api/resource-access";
import { Button } from "@/components/ui/button";

type ProjectTargetsProps = {
  projectId: string;
  targets: AccessProjectDetail["targets"];
  directory?: AccessDirectory;
  directoryPending: boolean;
  directoryFetching: boolean;
  directoryError: Error | null;
  onRetry: () => void;
  disabled: boolean;
  onMove: (targetId: string, destinationProjectId: string) => void;
};

export function ResourceAccessProjectTargets({ projectId, targets, directory, directoryPending,
  directoryFetching, directoryError, onRetry, disabled, onMove }: ProjectTargetsProps) {
  const [destinations, setDestinations] = useState<Record<string, string>>({});
  const [selectedTarget, setSelectedTarget] = useState("");
  const otherProjects = directory?.projects.filter(project => project.id !== projectId) ?? [];
  const outsideTargets = directory?.targets.filter(target => target.projectId !== projectId) ?? [];
  const activeTarget = outsideTargets.find(target => target.id === selectedTarget);
  const blocked = disabled || directoryFetching || !!directoryError || !directory;
  const names = (id: string) => directory?.projects.find(project => project.id === id)?.name ?? "Diğer proje";

  return <section className="resource-access-card" aria-labelledby="project-targets-heading">
    <div className="resource-access-card-heading"><span className="resource-access-step">02</span><div><h2 id="project-targets-heading">Bağlı hedefler <span className="resource-access-count">{targets.length}</span></h2><p>Yalnızca bu projeye bağlı hedefler burada gösterilir.</p></div></div>
    <p id="project-target-transfer-help" className="resource-access-empty">Taşıma, hedefe özel eski ekip izinlerini kaldırır; yeni projenin izinleri geçerli olur. Tarama geçmişi korunur. Kuyrukta veya çalışan taraması olan hedef taşınamaz.</p>
    {directoryError ? <div role="alert"><p>{directoryError.message}</p><Button type="button" disabled={directoryFetching} onClick={onRetry}>Projeleri ve hedefleri yeniden yükle</Button></div>
      : directoryPending && <p role="status">Taşıma seçenekleri yükleniyor…</p>}
    {targets.length ? <ul className="resource-access-project-target-list" tabIndex={0} aria-label="Projeye bağlı hedefler">{targets.map(target => {
      const destination = otherProjects.some(project => project.id === destinations[target.id]) ? destinations[target.id] : "";
      return <li key={target.id} className="resource-access-project-target-row">
        <div className="resource-access-project-target-name"><strong>{target.name}</strong></div>
        <form className="resource-access-project-target-move" onSubmit={event => {
          event.preventDefault();
          if (!blocked && destination) onMove(target.id, destination);
        }}>
          <label className="sr-only" htmlFor={`project-target-destination-${target.id}`}>{target.name} için taşınacak proje</label>
          <select id={`project-target-destination-${target.id}`} value={destination} disabled={blocked || !otherProjects.length} aria-describedby="project-target-transfer-help" onChange={event => setDestinations(value => ({ ...value, [target.id]: event.target.value }))}>
            <option value="">Başka projeye taşı…</option>{otherProjects.map(project => <option key={project.id} value={project.id}>{project.name}</option>)}
          </select>
          <Button type="submit" className="button button--secondary" disabled={blocked || !destination} aria-label={`${target.name} hedefini seçilen projeye taşı`}>Taşı</Button>
        </form>
      </li>;
    })}</ul> : <p className="resource-access-empty">Bu projeye henüz hedef bağlanmadı.</p>}
    <form className="resource-access-project-bind" onSubmit={event => {
      event.preventDefault();
      if (!blocked && activeTarget && directory?.projects.some(project => project.id === projectId)) onMove(activeTarget.id, projectId);
    }}>
      <label htmlFor="project-bind-existing-target">Mevcut hedefi bu projeye bağla</label>
      <div className="resource-access-input-action"><select id="project-bind-existing-target" value={activeTarget?.id ?? ""} disabled={blocked || !outsideTargets.length} aria-describedby="project-target-transfer-help" onChange={event => setSelectedTarget(event.target.value)}>
        <option value="">{outsideTargets.length ? "Diğer projelerden hedef seçin" : "Başka projede hedef yok"}</option>{outsideTargets.map(target => <option key={target.id} value={target.id}>{target.name} · {names(target.projectId)}</option>)}
      </select><Button type="submit" disabled={blocked || !activeTarget || !directory?.projects.some(project => project.id === projectId)}>Bu projeye bağla</Button></div>
    </form>
    <div className="resource-access-project-links"><Link href="/targets">Yeni hedef eklemek için Hedefler →</Link></div>
  </section>;
}
