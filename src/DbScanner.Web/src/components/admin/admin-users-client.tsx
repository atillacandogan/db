"use client";

import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { OperationToast } from "@/components/feedback/operation-toast";
import { useAuth } from "@/components/providers/auth-provider";
import { Button } from "@/components/ui/button";
import { ExportMenu } from "@/components/ui/export-menu";
import {
  createAdminUser,
  getAdminUsers,
  logoutAdminUserSessions,
  updateAdminUser,
  updateAdminUserMfa,
  updateAdminUserRole,
  updateAdminUserStatus,
  type CreateAdminUserRequest,
} from "@/lib/api/admin-users";
import { dbScannerRoles, type DbScannerRole } from "@/lib/auth/authorization";
import type { AdminUserDto } from "@/schemas/admin-user.schema";

const pageSize = 20;
const roleOptions: Array<{ value: DbScannerRole; label: string }> = [
  { value: dbScannerRoles.viewer, label: "Görüntüleyici" },
  { value: dbScannerRoles.operator, label: "Operatör" },
  { value: dbScannerRoles.admin, label: "Yönetici" },
];

type CreateUserForm = CreateAdminUserRequest & {
  temporaryPasswordConfirmation: string;
};

const initialCreateForm: CreateUserForm = {
  username: "",
  firstName: "",
  lastName: "",
  role: dbScannerRoles.viewer,
  requireMfa: false,
  temporaryPassword: "",
  temporaryPasswordConfirmation: "",
};

type EditForm = {
  firstName: string;
  lastName: string;
  requireMfa: boolean;
};

type ToastState = { message: string; variant: "success" | "error" } | null;

export function AdminUsersClient() {
  const queryClient = useQueryClient();
  const { userId } = useAuth();
  const [page, setPage] = useState(1);
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [roleFilter, setRoleFilter] = useState<"" | DbScannerRole>("");
  const [statusFilter, setStatusFilter] = useState<"" | "enabled" | "disabled">("");
  const [toast, setToast] = useState<ToastState>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [createForm, setCreateForm] = useState<CreateUserForm>(initialCreateForm);
  const [editUser, setEditUser] = useState<AdminUserDto | null>(null);
  const [editForm, setEditForm] = useState<EditForm | null>(null);
  const [roleUser, setRoleUser] = useState<AdminUserDto | null>(null);
  const [selectedRole, setSelectedRole] = useState<DbScannerRole>(dbScannerRoles.viewer);

  useEffect(() => {
    const timeoutId = window.setTimeout(() => {
      setSearch(searchInput.trim());
      setPage(1);
    }, 300);
    return () => window.clearTimeout(timeoutId);
  }, [searchInput]);

  const enabledFilter = statusFilter === "" ? undefined : statusFilter === "enabled";
  const usersQuery = useQuery({
    queryKey: ["admin-users", search, roleFilter, enabledFilter, page],
    queryFn: () => getAdminUsers({
      search: search || undefined,
      role: roleFilter || undefined,
      enabled: enabledFilter,
      page,
      pageSize,
    }),
    placeholderData: (previousData) => previousData,
  });

  const refreshUsers = async () => {
    await queryClient.invalidateQueries({ queryKey: ["admin-users"] });
  };

  const createMutation = useMutation({
    mutationFn: createAdminUser,
    onSuccess: async () => {
      setCreateOpen(false);
      setCreateForm(initialCreateForm);
      setToast({
        variant: "success",
        message: "Kullanıcı oluşturuldu. Geçici parola ilk girişte değiştirilmelidir.",
      });
      await refreshUsers();
    },
    onError: (error: Error) => setToast({ variant: "error", message: error.message }),
  });

  const editMutation = useMutation({
    mutationFn: async ({ user, form }: { user: AdminUserDto; form: EditForm }) => {
      let updated = await updateAdminUser(user.id, {
        firstName: form.firstName.trim() || undefined,
        lastName: form.lastName.trim() || undefined,
      });
      if (user.mfaConfigured !== true && form.requireMfa !== user.mfaRequired) {
        updated = await updateAdminUserMfa(user.id, form.requireMfa);
      }
      return updated;
    },
    onSuccess: async () => {
      setEditUser(null);
      setEditForm(null);
      setToast({ variant: "success", message: "Kullanıcı bilgileri güncellendi." });
      await refreshUsers();
    },
    onError: (error: Error) => setToast({ variant: "error", message: error.message }),
  });

  const roleMutation = useMutation({
    mutationFn: ({ id, role }: { id: string; role: DbScannerRole }) => updateAdminUserRole(id, role),
    onSuccess: async () => {
      setRoleUser(null);
      setToast({ variant: "success", message: "Kullanıcı rolü güncellendi." });
      await refreshUsers();
    },
    onError: (error: Error) => setToast({ variant: "error", message: error.message }),
  });

  const statusMutation = useMutation({
    mutationFn: ({ id, enabled }: { id: string; enabled: boolean }) => updateAdminUserStatus(id, enabled),
    onSuccess: async (updated) => {
      setToast({
        variant: "success",
        message: updated.enabled ? "Kullanıcı aktifleştirildi." : "Kullanıcı pasifleştirildi.",
      });
      await refreshUsers();
    },
    onError: (error: Error) => setToast({ variant: "error", message: error.message }),
  });

  const logoutMutation = useMutation({
    mutationFn: logoutAdminUserSessions,
    onSuccess: () => setToast({ variant: "success", message: "Kullanıcının aktif oturumları sonlandırıldı." }),
    onError: (error: Error) => setToast({ variant: "error", message: error.message }),
  });

  const isActionPending = statusMutation.isPending || logoutMutation.isPending;
  const users = usersQuery.data?.items ?? [];
  const totalPages = usersQuery.data?.totalPages ?? 0;
  const serviceState = usersQuery.isError
    ? { label: "Bağlantı sorunu", modifier: "error" }
    : usersQuery.isLoading || usersQuery.isFetching
      ? { label: "Kimlik servisi kontrol ediliyor", modifier: "loading" }
      : { label: "Kimlik servisi bağlı", modifier: "connected" };
  const rangeLabel = useMemo(() => {
    const total = usersQuery.data?.totalCount ?? 0;
    if (total === 0) return "0 kullanıcı";
    const first = (page - 1) * pageSize + 1;
    const last = Math.min(page * pageSize, total);
    return `${first}-${last} / ${total} kullanıcı`;
  }, [page, usersQuery.data?.totalCount]);

  function openEdit(user: AdminUserDto) {
    setEditUser(user);
    setEditForm({
      firstName: user.firstName ?? "",
      lastName: user.lastName ?? "",
      requireMfa: user.mfaConfigured === true || user.mfaRequired,
    });
  }

  function openRole(user: AdminUserDto) {
    setRoleUser(user);
    setSelectedRole(user.role ?? dbScannerRoles.viewer);
  }

  function userActions(user: AdminUserDto) {
    const options = [
      {
        label: "Düzenle",
        description: "Ad, soyad ve MFA zorunluluğunu düzenleyin.",
        onSelect: () => openEdit(user),
      },
    ];

    if (user.id !== userId) {
      options.push(
        {
          label: "Rolü değiştir",
          description: "DBScanner erişim seviyesini değiştirin.",
          onSelect: () => openRole(user),
        },
        {
          label: user.enabled ? "Pasifleştir" : "Aktifleştir",
          description: user.enabled ? "Kullanıcının giriş yapmasını engelleyin." : "Kullanıcının hesabını yeniden etkinleştirin.",
          onSelect: () => {
            const verb = user.enabled ? "pasifleştirmek" : "aktifleştirmek";
            if (window.confirm(`${user.username} kullanıcısını ${verb} istediğinize emin misiniz?`)) {
              statusMutation.mutate({ id: user.id, enabled: !user.enabled });
            }
          },
        },
      );
    }

    options.push(
      {
        label: "Oturumları sonlandır",
        description: "Kullanıcının tüm aktif Keycloak oturumlarını sonlandırın.",
        onSelect: () => {
          if (window.confirm(`${user.username} kullanıcısının tüm oturumları sonlandırılsın mı?`)) {
            logoutMutation.mutate(user.id);
          }
        },
      },
    );

    return options;
  }

  return (
    <section className="page-stack page-stack--wide admin-users-page" lang="tr">
      <section className="admin-access-hero">
        <div className="admin-access-hero__intro">
          <span className="admin-access-hero__icon" aria-hidden="true">
            <svg viewBox="0 0 24 24" role="img">
              <path d="M12 3 5 6v5c0 4.6 2.9 8.1 7 10 4.1-1.9 7-5.4 7-10V6l-7-3Z" />
              <path d="M9.2 12.1 11 14l3.9-4.2" />
            </svg>
          </span>
          <div>
            <p className="eyebrow">Kimlik ve erişim</p>
            <h1>Kullanıcılar ve Yetkiler</h1>
            <p>Ekibin DBScanner erişimini, rol seviyelerini ve hesap güvenliğini tek yerden yönetin.</p>
          </div>
        </div>
        <div className="admin-access-hero__actions">
          <span className={`admin-service-status admin-service-status--${serviceState.modifier}`} role="status">
            <span aria-hidden="true" />
            {serviceState.label}
          </span>
          <Button type="button" onClick={() => setCreateOpen(true)}>Kullanıcı Oluştur</Button>
        </div>
      </section>

      <section className="admin-access-overview" aria-label="Erişim yönetimi özeti">
        <article className="admin-access-metric">
          <span className="admin-access-metric__icon" aria-hidden="true">K</span>
          <div>
            <span>Toplam kullanıcı</span>
            <strong>{usersQuery.data?.totalCount ?? "—"}</strong>
          </div>
        </article>
        <article className="admin-access-metric">
          <span className="admin-access-metric__icon" aria-hidden="true">R</span>
          <div>
            <span>Yönetilen roller</span>
            <strong>{roleOptions.length} erişim seviyesi</strong>
          </div>
        </article>
        <article className="admin-access-metric">
          <span className="admin-access-metric__icon" aria-hidden="true">İ</span>
          <div>
            <span>İşlem güvenliği</span>
            <strong>Denetim kaydı açık</strong>
          </div>
        </article>
      </section>

      <section className="admin-grid-panel admin-users-panel">
        <div className="admin-users-panel__heading">
          <div>
            <p className="eyebrow">Ekip dizini</p>
            <h2>Kullanıcı erişimi</h2>
            <p className="muted">Hesapları bulun, erişim durumunu inceleyin ve güvenlik işlemlerini yönetin.</p>
          </div>
          <span className="admin-users-result-count">{rangeLabel}</span>
        </div>
        <div className="admin-users-toolbar">
          <label className="field-group admin-users-search">
            <span>Kullanıcı ara</span>
            <input
              className="input"
              type="search"
              placeholder="Kullanıcı adı, ad veya soyad"
              value={searchInput}
              onChange={(event) => setSearchInput(event.target.value)}
            />
          </label>
          <label className="field-group">
            <span>Rol filtresi</span>
            <select
              className="input"
              value={roleFilter}
              onChange={(event) => {
                setRoleFilter(event.target.value as "" | DbScannerRole);
                setPage(1);
              }}
            >
              <option value="">Tüm roller</option>
              {roleOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
            </select>
          </label>
          <label className="field-group">
            <span>Durum filtresi</span>
            <select
              className="input"
              value={statusFilter}
              onChange={(event) => {
                setStatusFilter(event.target.value as "" | "enabled" | "disabled");
                setPage(1);
              }}
            >
              <option value="">Tüm durumlar</option>
              <option value="enabled">Aktif</option>
              <option value="disabled">Pasif</option>
            </select>
          </label>
        </div>

        {usersQuery.isLoading ? (
          <div className="admin-grid-state admin-grid-state--loading" role="status">
            <span className="admin-loading-indicator" aria-hidden="true" />
            <strong>Kullanıcı dizini yükleniyor</strong>
            <span className="muted">Kimlik servisi ve rol bilgileri kontrol ediliyor.</span>
          </div>
        ) : null}
        {usersQuery.isError ? (
          <div className="admin-grid-state admin-grid-state--error" role="alert">
            <span className="admin-error-icon" aria-hidden="true">!</span>
            <strong>Kimlik servisine bağlanılamadı</strong>
            <span>{usersQuery.error instanceof Error ? usersQuery.error.message : "İşlem başarısız oldu."}</span>
            <small>API ve Keycloak servis hesabı yapılandırmasını kontrol edip tekrar deneyin.</small>
            <Button className="button button--secondary" type="button" onClick={() => usersQuery.refetch()}>Tekrar Dene</Button>
          </div>
        ) : null}
        {!usersQuery.isLoading && !usersQuery.isError && users.length === 0 ? (
          <div className="admin-grid-state admin-grid-state--empty">
            <span className="admin-empty-icon" aria-hidden="true">⌕</span>
            <strong>{search || roleFilter || statusFilter ? "Filtrelere uygun kullanıcı bulunamadı" : "Henüz kullanıcı yok"}</strong>
            <span className="muted">Arama koşullarını değiştirin veya yeni kullanıcı oluşturun.</span>
          </div>
        ) : null}

        {users.length > 0 ? (
          <div className="admin-table admin-users-table">
            <div className="admin-table__header admin-users-table-grid" aria-hidden="true">
              <span>Kullanıcı</span>
              <span>Rol</span>
              <span>Durum</span>
              <span>Son giriş</span>
              <span>İşlemler</span>
            </div>
            {users.map((user) => (
              <article key={user.id} className="admin-table__row admin-users-table-grid">
                <div className="admin-table__cell admin-table__cell--primary" data-label="Kullanıcı">
                  <div className="admin-user-identity">
                    <span className="admin-user-avatar" aria-hidden="true">{userInitials(user)}</span>
                    <div>
                      <strong>{displayName(user)}</strong>
                      <span className="muted admin-table__secondary">@{user.username}</span>
                    </div>
                  </div>
                </div>
                <div className="admin-table__cell" data-label="Rol">
                  <span className="catalog-pill">{roleLabel(user.role)}</span>
                </div>
                <div className="admin-table__cell" data-label="Durum">
                  <span className={`admin-user-status admin-user-status--${user.enabled ? "enabled" : "disabled"}`}>
                    {user.enabled ? "Aktif" : "Pasif"}
                  </span>
                  <span className="muted admin-table__secondary">MFA: {mfaLabel(user)}</span>
                </div>
                <div className="admin-table__cell admin-table__date" data-label="Son giriş">
                  {formatLastLogin(user.lastLoginAt)}
                </div>
                <div className="admin-table__cell admin-table__actions" data-label="İşlemler">
                  <ExportMenu label="İşlemler" options={userActions(user)} />
                </div>
              </article>
            ))}
          </div>
        ) : null}

        {!usersQuery.isError ? (
          <div className="pagination-bar admin-users-pagination">
            <span className="muted">{rangeLabel}</span>
            <div className="pagination-actions">
              <Button className="button button--secondary" type="button" disabled={page <= 1 || usersQuery.isFetching} onClick={() => setPage((current) => Math.max(1, current - 1))}>Önceki</Button>
              <span>Sayfa {totalPages === 0 ? 0 : page} / {totalPages}</span>
              <Button className="button button--secondary" type="button" disabled={page >= totalPages || usersQuery.isFetching} onClick={() => setPage((current) => current + 1)}>Sonraki</Button>
            </div>
          </div>
        ) : null}
      </section>

      {createOpen ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={(event) => {
          if (event.target === event.currentTarget) {
            setCreateOpen(false);
            setCreateForm(initialCreateForm);
          }
        }}>
          <section className="modal-panel admin-user-modal" role="dialog" aria-modal="true" aria-labelledby="create-user-title">
            <div className="modal-panel__header">
              <div><p className="detail-label" id="create-user-title">Kullanıcı Oluştur</p><p className="muted">Geçici parola Keycloak&apos;ta güvenli şekilde saklanır ve ilk girişte değiştirilmesi gerekir.</p></div>
            </div>
            <form className="form-grid" onSubmit={(event) => {
              event.preventDefault();
              if (createForm.temporaryPassword !== createForm.temporaryPasswordConfirmation) {
                setToast({ variant: "error", message: "Geçici parolalar eşleşmiyor." });
                return;
              }
              const request: CreateAdminUserRequest = {
                username: createForm.username,
                firstName: createForm.firstName,
                lastName: createForm.lastName,
                role: createForm.role,
                requireMfa: createForm.requireMfa,
                temporaryPassword: createForm.temporaryPassword,
              };
              createMutation.mutate(request);
            }}>
              <label className="field-group">
                <span>Kullanıcı adı</span>
                <input
                  className="input"
                  value={createForm.username}
                  minLength={3}
                  maxLength={128}
                  onChange={(event) => setCreateForm((current) => ({ ...current, username: event.target.value }))}
                  required
                />
                <small className="muted">En az 3 karakter kullanın.</small>
              </label>
              <label className="field-group"><span>Ad</span><input className="input" value={createForm.firstName} onChange={(event) => setCreateForm((current) => ({ ...current, firstName: event.target.value }))} /></label>
              <label className="field-group"><span>Soyad</span><input className="input" value={createForm.lastName} onChange={(event) => setCreateForm((current) => ({ ...current, lastName: event.target.value }))} /></label>
              <label className="field-group"><span>Rol</span><select className="input" value={createForm.role} onChange={(event) => setCreateForm((current) => ({ ...current, role: event.target.value as DbScannerRole }))}>{roleOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}</select></label>
              <label className="field-group"><span>Geçici parola</span><input className="input" type="password" autoComplete="new-password" minLength={12} maxLength={128} value={createForm.temporaryPassword} onChange={(event) => setCreateForm((current) => ({ ...current, temporaryPassword: event.target.value }))} required /><small className="muted">En az 12 karakter kullanın.</small></label>
              <label className="field-group"><span>Geçici parola tekrarı</span><input className="input" type="password" autoComplete="new-password" minLength={12} maxLength={128} value={createForm.temporaryPasswordConfirmation} onChange={(event) => setCreateForm((current) => ({ ...current, temporaryPasswordConfirmation: event.target.value }))} required /></label>
              <label className="checkbox-item checkbox-item--inline"><input type="checkbox" checked={createForm.requireMfa} onChange={(event) => setCreateForm((current) => ({ ...current, requireMfa: event.target.checked }))} /><span>İlk girişte MFA/TOTP kurulumu zorunlu olsun</span></label>
              <div className="form-actions form-actions--split"><Button className="button button--secondary" type="button" onClick={() => { setCreateOpen(false); setCreateForm(initialCreateForm); }} disabled={createMutation.isPending}>İptal</Button><Button type="submit" disabled={createMutation.isPending}>{createMutation.isPending ? "Kullanıcı oluşturuluyor..." : "Kullanıcı Oluştur"}</Button></div>
            </form>
          </section>
        </div>
      ) : null}

      {editUser && editForm ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={(event) => event.target === event.currentTarget && setEditUser(null)}>
          <section className="modal-panel admin-user-modal" role="dialog" aria-modal="true" aria-labelledby="edit-user-title">
            <div className="modal-panel__header"><div><p className="detail-label" id="edit-user-title">Kullanıcıyı Düzenle</p><p className="muted">@{editUser.username}</p></div></div>
            <form className="form-grid" onSubmit={(event) => { event.preventDefault(); editMutation.mutate({ user: editUser, form: editForm }); }}>
              <label className="field-group"><span>Ad</span><input className="input" value={editForm.firstName} onChange={(event) => setEditForm((current) => current && ({ ...current, firstName: event.target.value }))} /></label>
              <label className="field-group"><span>Soyad</span><input className="input" value={editForm.lastName} onChange={(event) => setEditForm((current) => current && ({ ...current, lastName: event.target.value }))} /></label>
              <label className="checkbox-item checkbox-item--inline"><input type="checkbox" checked={editForm.requireMfa} disabled={editUser.mfaConfigured === true} onChange={(event) => setEditForm((current) => current && ({ ...current, requireMfa: event.target.checked }))} /><span>{editUser.mfaConfigured ? "MFA/TOTP yapılandırılmış" : "MFA/TOTP kurulumu zorunlu olsun"}</span></label>
              <div className="form-actions form-actions--split"><Button className="button button--secondary" type="button" onClick={() => setEditUser(null)} disabled={editMutation.isPending}>İptal</Button><Button type="submit" disabled={editMutation.isPending}>{editMutation.isPending ? "Kaydediliyor..." : "Kaydet"}</Button></div>
            </form>
          </section>
        </div>
      ) : null}

      {roleUser ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={(event) => event.target === event.currentTarget && setRoleUser(null)}>
          <section className="modal-panel modal-panel--confirm admin-user-modal" role="dialog" aria-modal="true" aria-labelledby="role-user-title">
            <div className="modal-panel__header"><div><p className="detail-label" id="role-user-title">Rolü Değiştir</p><p className="muted">@{roleUser.username}</p></div></div>
            <label className="field-group"><span>DBScanner rolü</span><select className="input" value={selectedRole} onChange={(event) => setSelectedRole(event.target.value as DbScannerRole)}>{roleOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}</select></label>
            <div className="form-actions form-actions--split"><Button className="button button--secondary" type="button" onClick={() => setRoleUser(null)} disabled={roleMutation.isPending}>İptal</Button><Button type="button" onClick={() => roleMutation.mutate({ id: roleUser.id, role: selectedRole })} disabled={roleMutation.isPending}>{roleMutation.isPending ? "Kaydediliyor..." : "Rolü Güncelle"}</Button></div>
          </section>
        </div>
      ) : null}

      {isActionPending ? <div className="admin-user-operation-pending" role="status">İşlem tamamlanıyor...</div> : null}
      {toast ? <OperationToast message={toast.message} variant={toast.variant} onClose={() => setToast(null)} /> : null}
    </section>
  );
}

function displayName(user: AdminUserDto) {
  return [user.firstName, user.lastName].filter(Boolean).join(" ") || user.username;
}

function userInitials(user: AdminUserDto) {
  const parts = [user.firstName, user.lastName].filter(Boolean) as string[];
  const source = parts.length > 0 ? parts : [user.username];
  return source.map((part) => part.charAt(0)).join("").slice(0, 2).toLocaleUpperCase("tr-TR");
}

function roleLabel(role?: DbScannerRole | null) {
  return roleOptions.find((option) => option.value === role)?.label ?? "Rol atanmamış";
}

function mfaLabel(user: AdminUserDto) {
  if (user.mfaConfigured === true) return "Etkin";
  if (user.mfaRequired) return "Kurulum gerekli";
  if (user.mfaConfigured === false) return "Etkin değil";
  return "Bilinmiyor";
}

function formatLastLogin(value?: string | null) {
  if (!value) return "—";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "—" : date.toLocaleString("tr-TR");
}
