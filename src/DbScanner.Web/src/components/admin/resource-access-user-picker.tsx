"use client";

import { useEffect, useId, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { apiGet } from "@/lib/api/client";
import type { AccessIdentityUser, AccessUserPage } from "@/lib/api/resource-access";

export function ResourceAccessUserPicker({ selected, onSelect, disabled = false, allowDisabled = false }: {
  selected: AccessIdentityUser | null; onSelect: (user: AccessIdentityUser | null) => void;
  disabled?: boolean; allowDisabled?: boolean;
}) {
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
      disabled={disabled || tenant.data?.dedicatedTenant} onChange={event => { setSearch(event.target.value); setPage(1); onSelect(null); }} aria-describedby={`${id}-help`} />
    <small id={`${id}-help`}>ID’ler doğrudan Keycloak’tan gelir. Listeden kayıtlı bir kullanıcı seçin.</small>
    <div className="resource-access-user-results" aria-label="Keycloak kullanıcı sonuçları" aria-busy={users.isFetching || !settled}>
      {tenant.isError ? <p role="alert">Kurum bilgileri yüklenemedi. <button type="button" onClick={() => void tenant.refetch()}>Tekrar dene</button></p>
        : tenant.data?.dedicatedTenant ? <p>Kullanıcı araması için kuruma özel bir kimlik dizini gereklidir.</p>
        : users.isError ? <p role="alert">{users.error.message}<button type="button" onClick={() => void users.refetch()}>Tekrar dene</button></p>
        : users.isPending || !settled ? <p role="status">Keycloak kullanıcıları aranıyor…</p>
        : users.data.items.length === 0 ? <p>Kullanıcı bulunamadı.</p>
        : <ul>{users.data.items.map(user => <li key={user.id}><button type="button" className={`resource-access-user-option${selected?.id === user.id ? " is-selected" : ""}`}
            disabled={disabled || (!allowDisabled && !user.enabled)} aria-pressed={selected?.id === user.id} onClick={() => onSelect(user)}>
            <span className="resource-access-user-title"><strong>{[user.firstName, user.lastName].filter(Boolean).join(" ") || user.username}</strong><span>{!user.enabled ? "Devre dışı" : selected?.id === user.id ? "Seçildi" : "Seç"}</span></span>
            <span className="resource-access-user-handle">@{user.username}{!user.role ? " · Uygulama rolü yok" : ""}</span><code>{user.id}</code>
          </button></li>)}</ul>}
    </div>
    {users.isSuccess && <div className="resource-access-pager"><button type="button" disabled={page === 1 || !settled || users.isFetching} onClick={() => { setPage(value => value - 1); onSelect(null); }}>Önceki</button><span>Sayfa {page}</span>
      <button type="button" disabled={page * users.data.pageSize >= users.data.totalCount || !settled || users.isFetching} onClick={() => { setPage(value => value + 1); onSelect(null); }}>Sonraki</button></div>}
    {selected && <div className="resource-access-selection"><strong>Seçilen: @{selected.username}</strong><code>{selected.id}</code>{!selected.role && <small>Ekip üyeliği rol atamaz; uygulama rolü ayrıca atanmalıdır.</small>}</div>}
  </div>;
}
