"use client";

import Link from "next/link";
import type { ReactNode } from "react";
import { useMemo } from "react";
import { usePathname } from "next/navigation";
import { navigationItems } from "@/lib/const/navigation";
import { useAuth } from "@/components/providers/auth-provider";

type AppShellProps = {
  children: ReactNode;
};

export function AppShell({ children }: AppShellProps) {
  const pathname = usePathname();
  const { username, roles, logout } = useAuth();
  const roleLabel = roles.includes("dbscanner-admin")
    ? "Admin"
    : roles.includes("dbscanner-operator")
      ? "Operator"
      : "Viewer";
  const currentItem = useMemo(
    () => navigationItems.find((item) => pathname.startsWith(item.href)) ?? navigationItems[0],
    [pathname],
  );

  return (
    <div className="app-shell">
      <aside className="app-sidebar" aria-label="Primary navigation">
        <div className="brand-mark">DB</div>
        <nav className="nav-list">
          {navigationItems.map((item) => (
            <Link key={item.href} href={item.href} className={pathname.startsWith(item.href) ? "nav-link nav-link--active" : "nav-link"}>
              {item.label}
            </Link>
          ))}
        </nav>
      </aside>
      <div className="app-main">
        <header className="app-header">
          <div className="app-header__content">
            <div>
              <p className="eyebrow">Credentialless Scan</p>
              <strong>Database Scanner POC</strong>
            </div>
            <div className="app-header__meta">
              <span className="app-breadcrumb">Overview / {currentItem.label}</span>
              <span className="app-user" title={roles.join(", ")}>
                {username} · {roleLabel}
              </span>
              <button className="app-logout" type="button" onClick={() => void logout()}>
                Çıkış
              </button>
            </div>
          </div>
        </header>
        <main className="content-surface">{children}</main>
      </div>
    </div>
  );
}
