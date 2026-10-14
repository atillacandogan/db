"use client";

import type { ReactNode } from "react";
import { useAuth } from "@/components/providers/auth-provider";
import { dbScannerRoles } from "@/lib/auth/authorization";

export function AdminRouteGuard({ children }: { children: ReactNode }) {
  const { hasRole } = useAuth();

  if (!hasRole(dbScannerRoles.admin)) {
    return (
      <section className="placeholder access-denied-state" role="alert" lang="tr">
        <p className="eyebrow">Erişim Engellendi</p>
        <h1>Bu sayfaya erişim yetkiniz bulunmuyor.</h1>
        <p className="muted">Bu alan yalnızca DBScanner yöneticilerine açıktır.</p>
      </section>
    );
  }

  return children;
}
