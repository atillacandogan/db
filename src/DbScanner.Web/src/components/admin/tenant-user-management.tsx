"use client";
import { useQuery } from "@tanstack/react-query";
import { apiGet } from "@/lib/api/client";
import { AdminUsersClient } from "@/components/admin/admin-users-client";

export function TenantUserManagement() {
  const context = useQuery({ queryKey: ["access-me"], queryFn: () => apiGet<{ dedicatedTenant: boolean }>("/api/access/me") });
  if (context.isPending) return <p role="status" lang="tr">Kurum bağlamı doğrulanıyor…</p>;
  if (context.isError) return <p role="alert" lang="tr">{context.error.message}</p>;
  if (context.data.dedicatedTenant) return <section className="placeholder" lang="tr"><h1>Kimlik Yönetimi</h1><p>Çok kurumlu kurulumda kullanıcılar kurumun kimlik sağlayıcısı üzerinden yönetilir. Ortak realm üzerindeki kullanıcı ve rol değişiklikleri güvenlik nedeniyle kapalıdır.</p><p>Mevcut kullanıcıların ekip ve hedef yetkilerini “Ekip ve Hedef Yetkileri” sayfasından yönetebilirsiniz.</p></section>;
  return <AdminUsersClient />;
}
