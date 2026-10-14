import { apiDelete, apiGet, apiPut } from "@/lib/api/client";
import type { AccessProjectDetail } from "./resource-access";

export function getAccessProject(projectId: string, signal?: AbortSignal) {
  return apiGet<AccessProjectDetail>(`/api/access/projects/${encodeURIComponent(projectId)}`, { signal });
}

export function renameAccessProject(projectId: string, name: string) {
  const trimmed = name.trim();
  if (!trimmed || trimmed.length > 160) throw new Error("Proje adı 1–160 karakter olmalıdır.");
  return apiPut(`/api/access/projects/${encodeURIComponent(projectId)}`, { name: trimmed });
}

export function moveAccessTargetToProject(targetId: string, projectId: string) {
  if (!targetId.trim() || !projectId.trim()) throw new Error("Hedef ve proje seçilmelidir.");
  return apiPut(`/api/access/targets/${encodeURIComponent(targetId)}/project`, { projectId });
}

export function projectDeletionBlockers(data: AccessProjectDetail): string[] {
  const blockers = [];
  if (data.project.isDefault) blockers.push("Varsayılan proje silinemez; yeni hedefler için güvenli başlangıç alanıdır.");
  if (data.targets.length) blockers.push(`${data.targets.length} bağlı hedef var. Önce hedefleri başka bir projeye taşıyın.`);
  if (data.grants.length) blockers.push(`${data.grants.length} kayıtlı ekip izni var. Önce bu izinleri kaldırın; arşivlenmiş ekiplerin izinleri de buna dahildir.`);
  return blockers;
}

// The server rechecks these conditions transactionally. This check prevents an
// accidental UI submission; it is not an authorization or concurrency boundary.
export async function deleteAccessProject(data: AccessProjectDetail, confirmation: string) {
  const blockers = projectDeletionBlockers(data);
  if (blockers.length) throw new Error(blockers.join(" "));
  if (confirmation !== data.project.name) throw new Error("Silmek için proje adını tam olarak yazın.");
  await apiDelete(`/api/access/projects/${encodeURIComponent(data.project.id)}`);
}
