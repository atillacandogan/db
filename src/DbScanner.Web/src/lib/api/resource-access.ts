export type AccessNamed = { id: string; name: string };
export type AccessTeam = AccessNamed & { isArchived: boolean };
export type AccessGrant = { id: string; teamId: string; projectId: string; targetId: string | null; permissions: number };
export type AccessDirectory = {
  projects: AccessNamed[]; teams: AccessTeam[]; members: { teamId: string; subject: string }[];
  grants: AccessGrant[]; targets: (AccessNamed & { projectId: string })[];
};
export type AccessIdentityUser = { id: string; username: string; firstName: string | null; lastName: string | null; enabled: boolean; role: string | null };
export type AccessUserPage = { items: AccessIdentityUser[]; totalCount: number; page: number; pageSize: number };
export type AccessTeamDetail = Omit<AccessDirectory, "teams"> & { team: AccessTeam };
export type AccessProjectDetail = {
  project: AccessNamed & { isDefault: boolean };
  targets: (AccessNamed & { projectId: string })[];
  grants: AccessGrant[];
  teams: AccessTeam[];
};
export type AccessPreview = {
  user: Pick<AccessIdentityUser, "id" | "username" | "role" | "enabled">;
  target: AccessNamed & { projectId: string };
  rolePermissions: number; teamPermissions: number; effectivePermissions: number;
  sources: (AccessGrant & { teamName: string; isArchived: boolean })[];
  decisions: { permission: number; allowed: boolean; reason: string }[];
  evaluatedAt: string; basis: string;
};
export type AccessAudit = {
  id: string; actorSubject: string; actorUsername: string | null; action: string;
  entityType: string; entityId: string; teamId: string | null;
  beforeJson: string | null; afterJson: string | null; timestamp: string;
};
export type AccessAuditPage = { items: AccessAudit[]; totalCount: number; page: number; pageSize: number };
export const accessPermissionBits = [
  { bit: 1, label: "Görüntüle" }, { bit: 2, label: "Tarama / durdurma" },
  { bit: 4, label: "Hedefi yönet" }, { bit: 8, label: "Bulguları dışa aktar" },
];
export const accessActionLabels: Record<string, string> = {
  "project.created": "Proje oluşturuldu", "project.renamed": "Proje adı değiştirildi", "project.deleted": "Proje silindi",
  "team.created": "Ekip oluşturuldu", "team.renamed": "Ekip adı değiştirildi",
  "team.archived": "Ekip arşivlendi", "team.restored": "Ekip yeniden etkinleştirildi", "member.added": "Üye eklendi",
  "member.removed": "Üyelik kaldırıldı", "grant.created": "İzin verildi", "grant.updated": "İzin güncellendi",
  "grant.revoked": "İzin kaldırıldı", "target.project_changed": "Hedefin projesi değiştirildi",
};
