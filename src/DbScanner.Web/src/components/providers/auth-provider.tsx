"use client";

import { createContext, type ReactNode, useCallback, useContext, useEffect, useMemo, useState } from "react";
import {
  getCurrentIdentity,
  initializeKeycloak,
  logoutFromKeycloak,
} from "@/lib/auth/keycloak";
import {
  hasAnyRole,
  hasPermission,
  type DbScannerPermission,
} from "@/lib/auth/authorization";

type AuthContextValue = {
  userId: string;
  username: string;
  email?: string;
  roles: string[];
  hasRole: (role: string) => boolean;
  hasAnyRole: (...roles: string[]) => boolean;
  can: (permission: DbScannerPermission) => boolean;
  logout: () => Promise<void>;
};

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [identity, setIdentity] = useState<{
    userId: string;
    username: string;
    email?: string;
    roles: string[];
  } | null>(null);
  const [error, setError] = useState("");
  const [isLoggingOut, setIsLoggingOut] = useState(false);

  useEffect(() => {
    let active = true;

    initializeKeycloak()
      .then((authenticated) => {
        if (active && authenticated) {
          setIdentity(getCurrentIdentity());
        }
      })
      .catch((reason: unknown) => {
        if (active) {
          setError(reason instanceof Error ? reason.message : "Kimlik doğrulama başlatılamadı.");
        }
      });

    return () => {
      active = false;
    };
  }, []);

  const logout = useCallback(async () => {
    setIsLoggingOut(true);

    try {
      await logoutFromKeycloak();
    } catch (reason: unknown) {
      setIsLoggingOut(false);
      setError(reason instanceof Error ? reason.message : "Güvenli çıkış tamamlanamadı.");
    }
  }, []);

  const value = useMemo<AuthContextValue | null>(() => {
    if (!identity) {
      return null;
    }

    return {
      ...identity,
      hasRole: (role) => identity.roles.includes(role),
      hasAnyRole: (...roles) => hasAnyRole(identity.roles, roles),
      can: (permission) => hasPermission(identity.roles, permission),
      logout,
    };
  }, [identity, logout]);

  if (isLoggingOut) {
    return (
      <main className="auth-state" aria-live="polite" lang="tr">
        <strong>Güvenli çıkış yapılıyor...</strong>
        <p>Keycloak oturumunuz kapatılıyor.</p>
      </main>
    );
  }

  if (error) {
    return (
      <main className="auth-state" role="alert" lang="tr">
        <strong>Oturum açılamadı.</strong>
        <p>{error}</p>
        <button className="button" type="button" onClick={() => window.location.reload()}>
          Tekrar Dene
        </button>
      </main>
    );
  }

  if (!value) {
    return (
      <main className="auth-state" aria-live="polite" lang="tr">
        <strong>Güvenli oturum hazırlanıyor...</strong>
        <p>Keycloak giriş ekranına yönlendiriliyorsunuz.</p>
      </main>
    );
  }

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext);
  if (!value) {
    throw new Error("useAuth must be used inside AuthProvider.");
  }

  return value;
}
