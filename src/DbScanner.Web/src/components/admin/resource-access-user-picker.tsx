"use client";

import { useEffect, useId, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { apiGet } from "@/lib/api/client";
import type { AccessIdentityUser, AccessUserPage } from "@/lib/api/resource-access";

type SinglePickerProps = {
  disabled?: boolean; allowDisabled?: boolean;
  existingUserIds?: readonly string[];
  selected: AccessIdentityUser | null; onSelect: (user: AccessIdentityUser | null) => void;
  selectedUsers?: undefined; onToggleUser?: undefined;
};
type MultiPickerProps = {
  disabled?: boolean; allowDisabled?: boolean;
  existingUserIds?: readonly string[];
  selected?: never; onSelect?: never;
  selectedUsers: readonly AccessIdentityUser[]; onToggleUser: (user: AccessIdentityUser) => void;
  maxSelected?: number;
};
type PickerProps = SinglePickerProps | MultiPickerProps;

export function ResourceAccessUserPicker(props: PickerProps) {
  const { disabled = false, allowDisabled = false, existingUserIds = [] } = props;
  const multiSelection = props.selectedUsers !== undefined ? props as MultiPickerProps : null;
  const multiSelect = multiSelection !== null;
  const singleSelection = multiSelection ? null : props as SinglePickerProps;
  const selected = singleSelection?.selected ?? null;
  const selectedUsers = multiSelection?.selectedUsers ?? [];
  const maxSelected = multiSelection?.maxSelected ?? 50;
  const selectionLimitReached = multiSelect && selectedUsers.length >= maxSelected;
  const isSelected = (user: AccessIdentityUser) => multiSelect
    ? selectedUsers.some(selectedUser => selectedUser.id === user.id)
    : selected?.id === user.id;
  const selectUser = (user: AccessIdentityUser) => {
    if (multiSelection) multiSelection.onToggleUser(user);
    else singleSelection?.onSelect(user);
  };
  const clearSingleSelection = () => singleSelection?.onSelect(null);
  const toggleSelectedUser = (user: AccessIdentityUser) => multiSelection?.onToggleUser(user);
  const id = useId();
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const [page, setPage] = useState(1);
  const tenant = useQuery({ queryKey: ["access-me"], queryFn: () => apiGet<{ tenantId: string; dedicatedTenant: boolean }>("/api/access/me") });
  useEffect(() => { const timer = setTimeout(() => setTerm(search.trim()), 300); return () => clearTimeout(timer); }, [search]);
  const users = useQuery({
    queryKey: ["resource-access-users", term, page],
    queryFn: ({ signal }) => apiGet<AccessUserPage>(`/api/access/users?search=${encodeURIComponent(term)}&page=${page}`, { signal }),
    enabled: tenant.isSuccess && !tenant.data.dedicatedTenant, retry: false, staleTime: 0,
  });
  const settled = search.trim() === term;
  return <div className="resource-access-picker" lang="tr">
    <label htmlFor={id}>Keycloak kullanıcılarında ara</label>
    <input id={id} type="search" autoComplete="off" maxLength={160} placeholder="Kullanıcı adı, ad soyad veya ID" value={search}
      disabled={disabled || tenant.data?.dedicatedTenant} onChange={event => { setSearch(event.target.value); setPage(1); clearSingleSelection(); }} aria-describedby={`${id}-help`} />
    <small id={`${id}-help`}>ID’ler doğrudan Keycloak’tan gelir. {multiSelect ? "Birden fazla kullanıcı seçebilirsiniz; seçimler sayfa değiştirince korunur (en fazla 50 kullanıcı)." : "Listeden kayıtlı bir kullanıcı seçin."}</small>
    <div className="resource-access-user-results" aria-label="Keycloak kullanıcı sonuçları" aria-busy={users.isFetching || !settled}>
      {tenant.isError ? <p role="alert">Kurum bilgileri yüklenemedi. <button type="button" onClick={() => void tenant.refetch()}>Tekrar dene</button></p>
        : tenant.data?.dedicatedTenant ? <p>Kullanıcı araması için kuruma özel bir kimlik dizini gereklidir.</p>
        : users.isError ? <p role="alert">{users.error.message}<button type="button" onClick={() => void users.refetch()}>Tekrar dene</button></p>
        : users.isPending || !settled ? <p role="status">Keycloak kullanıcıları aranıyor…</p>
        : users.data.items.length === 0 ? <p>Kullanıcı bulunamadı.</p>
        : <ul>{users.data.items.map(user => {
          const alreadyMember = existingUserIds.includes(user.id);
          const chosen = isSelected(user);
          return <li key={user.id}><button type="button" className={`resource-access-user-option${chosen ? " is-selected" : ""}`}
            disabled={disabled || alreadyMember || (!allowDisabled && !user.enabled) || (selectionLimitReached && !chosen)} aria-pressed={chosen} onClick={() => selectUser(user)}>
            <span className="resource-access-user-title"><strong>{[user.firstName, user.lastName].filter(Boolean).join(" ") || user.username}</strong><span>{alreadyMember ? "Ekip üyesi" : !user.enabled ? "Devre dışı" : chosen ? "Seçildi" : selectionLimitReached ? `En fazla ${maxSelected}` : "Seç"}</span></span>
            <span className="resource-access-user-handle">@{user.username}{!user.role ? " · Uygulama rolü yok" : ""}</span><code>{user.id}</code>
          </button></li>;
        })}</ul>}
    </div>
    {users.isSuccess && <div className="resource-access-pager"><button type="button" disabled={page === 1 || !settled || users.isFetching} onClick={() => { setPage(value => value - 1); clearSingleSelection(); }}>Önceki</button><span>Sayfa {page}</span>
      <button type="button" disabled={page * users.data.pageSize >= users.data.totalCount || !settled || users.isFetching} onClick={() => { setPage(value => value + 1); clearSingleSelection(); }}>Sonraki</button></div>}
    {multiSelect && selectedUsers.length > 0
      ? <div className="resource-access-selection"><strong>{selectedUsers.length} kullanıcı seçildi{multiSelection ? ` · ${selectedUsers.length}/${maxSelected}` : ""}</strong>
        <ul className="resource-access-selected-users">{selectedUsers.map(user => <li key={user.id}><span>@{user.username}<code>{user.id}</code></span>
          <button type="button" disabled={disabled} aria-label={`@${user.username} seçimini kaldır`} onClick={() => toggleSelectedUser(user)}>Kaldır</button></li>)}</ul>
        {selectedUsers.some(user => !user.role) && <small>Ekip üyeliği rol atamaz; uygulama rolü ayrıca atanmalıdır.</small>}
      </div>
      : selected && <div className="resource-access-selection"><strong>Seçilen: @{selected.username}</strong><code>{selected.id}</code>{!selected.role && <small>Ekip üyeliği rol atamaz; uygulama rolü ayrıca atanmalıdır.</small>}</div>}
  </div>;
}
